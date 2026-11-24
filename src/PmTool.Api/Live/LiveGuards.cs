using Microsoft.Net.Http.Headers;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.Live;

/// <summary>標在即時連線的端點上（negotiate 與連線本身）：限流、來源檢查認得它。</summary>
internal sealed class LiveEndpointMarker
{
    public static readonly LiveEndpointMarker Instance = new();

    private LiveEndpointMarker()
    {
    }

    public static bool IsLive(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<LiveEndpointMarker>() is not null;
}

/// <summary>
/// 即時連線端點在進 hub 之前的兩道檢查，都在端點確定之後、授權之前：
/// <list type="bullet">
/// <item><description>來源：<c>Origin</c> 標頭存在而且和請求的主機不同就 403。Cookie 已經是 SameSite=Strict，這是多一層：
/// 瀏覽器的 WebSocket 不受同源政策限制，也不會先預檢，別的網站的頁面能對這裡開連線，靠的只有 Cookie 與這一道。</description></item>
/// <item><description>名額：這個人已經有 <see cref="LiveConnections.MaxPerPerson"/> 條連線就在 negotiate 回 429，
/// 不必等連線建立之後才斷。hub 登記時還會再算一次（同時進來的 negotiate 會一起通過這一道）。</description></item>
/// </list>
/// </summary>
internal static class LiveGuards
{
    private const string NegotiateSegment = "/negotiate";

    // 名額要等別的分頁關掉才會有，沒有「視窗還剩多久」可以回報；和同時請求數的限流一樣請用戶端一秒後再試。
    private const string RetryAfterSeconds = "1";

    public static IApplicationBuilder UseLiveGuards(this IApplicationBuilder app) => app.Use(CheckAsync);

    private static Task CheckAsync(HttpContext context, RequestDelegate next)
    {
        if (!LiveEndpointMarker.IsLive(context))
        {
            return next(context);
        }

        if (!OriginIsAllowed(context))
        {
            return ApiFailure.Forbidden.WriteAsync(context.Response, context.RequestAborted);
        }

        // 沒登入或用權杖的請求，後面的授權與範圍檢查會回 401 與 403，不必在這裡數名額。
        if (IsNegotiate(context.Request)
            && SessionPrincipal.TryReadToken(context.User) is not null
            && SessionPrincipal.TryRead(context.User) is { } user
            && context.RequestServices.GetRequiredService<LiveConnections>().IsFull(user.PersonId))
        {
            context.Response.Headers.RetryAfter = RetryAfterSeconds;
            return ApiFailure.TooManyLiveConnections.WriteAsync(context.Response, context.RequestAborted);
        }

        return next(context);
    }

    private static bool IsNegotiate(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) && request.Path.Value?.EndsWith(NegotiateSegment, StringComparison.OrdinalIgnoreCase) == true;

    private static bool OriginIsAllowed(HttpContext context)
    {
        var publicHost = context.RequestServices.GetRequiredService<TransportSecurity>().PublicHost;
        return publicHost is null ? OriginMatchesHost(context.Request) : OriginMatchesPublicHost(context.Request, publicHost);
    }

    /// <summary>
    /// 沒有 Origin（不是瀏覽器發的）放行；有的話，主機名稱要和 Host 標頭相同（不分大小寫）。
    /// Host 有明寫埠才比埠：反向代理常把埠拿掉再轉進來，那時比埠會把正常的使用者擋掉。
    /// </summary>
    internal static bool OriginMatchesHost(HttpRequest request) =>
        ReadOrigin(request) switch
        {
            (false, _) => true,
            (true, null) => false,
            (true, { } origin) => request.Host.HasValue
                && string.Equals(origin.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
                && (request.Host.Port is not { } port || origin.Port == port),
        };

    /// <summary>
    /// 設了 <c>Security:PublicHost</c> 就只信它，不信請求的 Host 標頭（那是用戶端自己填的，和轉到 https 的做法一樣）：
    /// Origin 的主機要等於它；它有寫埠時埠也要相同，沒寫埠就要是 Origin 的預設埠。
    /// </summary>
    internal static bool OriginMatchesPublicHost(HttpRequest request, string publicHost)
    {
        var expected = new HostString(publicHost);
        return ReadOrigin(request) switch
        {
            (false, _) => true,
            (true, null) => false,
            (true, { } origin) => string.Equals(origin.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
                && (expected.Port is { } port ? origin.Port == port : origin.IsDefaultPort),
        };
    }

    /// <summary>沒有 Origin 標頭是（false、null）；有但讀不懂（「null」、不只一個、不是網址）是（true、null）。</summary>
    private static (bool Present, Uri? Origin) ReadOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(HeaderNames.Origin, out var values))
        {
            return (false, null);
        }

        return (true, values.Count == 1 && Uri.TryCreate(values[0], UriKind.Absolute, out var origin) ? origin : null);
    }
}
