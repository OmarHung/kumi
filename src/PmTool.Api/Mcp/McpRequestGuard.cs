using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using PmTool.Api.OAuth;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Mcp;

/// <summary>
/// <c>/mcp</c> 的入口檢查，排在 SDK 的處理之前：
/// 先看 <c>Origin</c>（規格：防 DNS rebinding，帶了而不是自己的來源就 403；命令列的用戶端不帶，不擋），
/// 再要求 Bearer 權杖。Cookie 一律不算：瀏覽器會自己帶 Cookie，網頁上的任何程式碼都能用它打 /mcp，
/// 所以這個端點只認明確放進標頭的權杖。沒登入的 401 帶規格要求的 <c>WWW-Authenticate</c>，用戶端靠它找到授權伺服器。
/// 工具的範圍不在這裡看：權杖有哪些範圍要等到知道呼叫哪個工具才判斷（見 <see cref="McpToolRunner"/>）。
/// </summary>
internal static class McpRequestGuard
{
    public const string ProtectedResourcePath = "/.well-known/oauth-protected-resource";

    private static readonly ApiFailure OriginNotAllowed = ApiFailure.Forbidden with { Message = "這個請求的來源（Origin）不被允許。" };

    public static async ValueTask<object?> FilterAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var resolver = context.RequestServices.GetRequiredService<OAuthServerResolver>();
        var server = resolver.Resolve(context.Request);

        if (!IsAllowedOrigin(context.Request, server.Issuer))
        {
            return OriginNotAllowed.ToResult();
        }

        // 主機代理每件工作的工具授權（kjt_，HostCredentials 已經驗證過）：不是任何人的權杖，專案由工作決定，
        // 所以不看 X-Kumi-Project（帶了也不看）。無效的授權和沒帶一樣走下面的挑戰（401）。
        if (HostCredentials.TryGetGrant(context) is not null)
        {
            return LimitBody(context) ?? await next(invocation);
        }

        var token = SessionPrincipal.TryReadAccessToken(context.User);
        if (token is null)
        {
            return await ChallengeAsync(context, server.Issuer);
        }

        // 本機代理一定要說這個請求屬於哪個專案：沒說就整個請求拒絕，不讓它退回「看得到的全部專案」。
        if (McpProjectScope.Applies(token) && McpProjectScope.Read(context.Request) is null)
        {
            return (ApiFailure.BadRequest with { Message = McpProjectScope.MissingMessage }).ToResult();
        }

        // 排在確認登入之後：沒登入的人不必知道上限，也不能靠大內文試探。
        return LimitBody(context) ?? await next(invocation);
    }

    /// <summary>
    /// 內文超過 <see cref="McpLimits.MaxRequestBytes"/> 回 413。Content-Length 先擋；沒有 Content-Length 的（chunked）靠 Kestrel 的上限，
    /// 這裡把這個請求的上限調成同一個數字。測試用的伺服器沒有 Kestrel 的功能，所以 chunked 的那條路只能在真的伺服器上看到。
    /// </summary>
    private static IResult? LimitBody(HttpContext context)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        {
            feature.MaxRequestBodySize = McpLimits.MaxRequestBytes;
        }

        return context.Request.ContentLength > McpLimits.MaxRequestBytes
            ? ApiFailure.PayloadLargerThan(McpLimits.MaxRequestBytes).ToResult()
            : null;
    }

    /// <summary>沒帶 Origin 的請求（命令列的用戶端）不擋；帶了就要等於這個部署自己的來源。</summary>
    public static bool IsAllowedOrigin(HttpRequest request, string issuer)
    {
        if (!request.Headers.TryGetValue("Origin", out var values))
        {
            return true;
        }

        return values.Count == 1
            && Uri.TryCreate(values[0], UriKind.Absolute, out var origin)
            && Uri.TryCreate(issuer, UriKind.Absolute, out var own)
            && string.Equals(origin.GetLeftPart(UriPartial.Authority), own.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>WWW-Authenticate</c> 的值：中繼資料的網址與要求的範圍（RFC 9728、MCP 授權規格）。</summary>
    public static string ChallengeValue(string issuer) =>
        $"Bearer resource_metadata=\"{ResourceMetadataUrl(issuer)}\", scope=\"{string.Join(' ', AccessTokenScopes.Grantable.Select(AccessTokenScopes.NameOf))}\"";

    public static string ResourceMetadataUrl(string issuer) => $"{issuer}{ProtectedResourcePath}{OAuthServerResolver.McpPath}";

    private static async Task<IResult> ChallengeAsync(HttpContext context, string issuer)
    {
        var challenge = ChallengeValue(issuer);
        if (AccessTokenAuthentication.IsBearerRequest(context.Request))
        {
            // 帶了無效的權杖：交給權杖的驗證處理（失敗太多次會回 429 與 Retry-After，不是 401），
            // 只在最後把挑戰換成帶 resource_metadata 的那一個。
            context.Response.OnStarting(() =>
            {
                if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                {
                    context.Response.Headers.WWWAuthenticate = challenge;
                }

                return Task.CompletedTask;
            });
            await context.ChallengeAsync(AccessTokenAuthentication.Scheme);
            return Results.Empty;
        }

        context.Response.Headers.WWWAuthenticate = challenge;
        return ApiFailure.AuthRequired.ToResult();
    }
}
