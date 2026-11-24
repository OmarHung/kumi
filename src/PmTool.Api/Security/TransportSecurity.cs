using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Extensions;
using PmTool.Api.Composition;
using PmTool.Api.Endpoints;

namespace PmTool.Api.Security;

/// <summary>
/// 部署在 HTTPS 後面時的設定：讀轉送的標頭（<see cref="ProxySettings"/>），以及一律走 HTTPS（<see cref="RequireHttps"/>）。
/// 兩個都預設關著，本機的 http://localhost:8080 照舊。
/// </summary>
/// <param name="Proxy">轉送的標頭。</param>
/// <param name="RequireHttps">
/// Cookie 一律 Secure、每個回應帶 HSTS、不是 HTTPS 的請求 308 轉到 https。
/// 放在代理後面時要同時打開轉送的標頭，不然每個請求看起來都是 http。
/// </param>
/// <param name="PublicHost">
/// 對外的主機名稱（可帶埠）。設了的話，轉到 https 一律轉到它，不用請求的 Host 標頭：
/// 標頭是用戶端自己填的，假的 Host 換來的 308 若被前面的快取存起來，別人就會被轉去別的網站。
/// </param>
internal sealed partial record TransportSecurity(ProxySettings Proxy, bool RequireHttps, string? PublicHost = null)
{
    public const string RequireHttpsSetting = "Security:RequireHttps";
    public const string PublicHostSetting = "Security:PublicHost";

    private const int MaxPort = 65535;

    /// <summary>主機名稱或 IPv4，後面可以接 :埠。不收協定、路徑、帳密與空白，才不會拼出奇怪的網址。</summary>
    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?(:(?<port>[0-9]{1,5}))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    /// <summary>
    /// 一年。不加 includeSubDomains 與 preload：同一個網域底下可能還有別的服務，
    /// 這兩個一旦送出就收不回來，要加由網域的擁有者決定。
    /// </summary>
    public const string StrictTransportSecurity = "max-age=31536000";

    /// <exception cref="InvalidOperationException">設定不合規定，訊息說得出是哪個設定。</exception>
    public static TransportSecurity From(IConfiguration configuration) =>
        new(
            ProxySettings.From(configuration),
            SettingValues.ReadFlag(configuration, RequireHttpsSetting, defaultValue: false),
            ReadPublicHost(configuration));

    private static string? ReadPublicHost(IConfiguration configuration)
    {
        var raw = SettingValues.ReadText(configuration, PublicHostSetting);
        if (raw is null)
        {
            return null;
        }

        var match = HostPattern().Match(raw);
        var portIsValid = !match.Groups["port"].Success || int.Parse(match.Groups["port"].Value, CultureInfo.InvariantCulture) is >= 1 and <= MaxPort;
        return match.Success && portIsValid
            ? raw
            : throw new InvalidOperationException(
                $"{PublicHostSetting} 只能是主機名稱，可以接 :埠（例如 pm.example.com 或 pm.example.com:8443），不含 https:// 與路徑，現在是「{raw}」。");
    }

    /// <summary>設定看起來有問題、但不到要讓啟動失敗的地方，記成警告讓部署的人看得到。</summary>
    /// <param name="isProduction">
    /// 環境是 Production。本機的 8080 也是 Production（容器預設），所以沒設 <see cref="PublicHostSetting"/> 只能警告，不能讓啟動失敗。
    /// </param>
    public IReadOnlyList<string> Warnings(bool isProduction = false)
    {
        var warnings = new List<string>();
        if (isProduction && PublicHost is null)
        {
            warnings.Add(
                $"{PublicHostSetting} 沒有設：OAuth 授權伺服器的 issuer 與 MCP 的網址會依請求的 Host 標頭組出來，標頭是用戶端自己填的。"
                + "對外提供服務時請設成對外的主機名稱（例如 pm.example.com）；沒設時中繼資料的回應不允許被快取（Cache-Control: no-store）。");
        }

        if (RequireHttps && !Proxy.Enabled)
        {
            // api 自己提供 HTTPS 時這是合法的，所以不讓啟動失敗；放在代理後面卻忘了設，就會一直轉址。
            warnings.Add(
                $"{RequireHttpsSetting} 是 true，但沒有打開 {ProxySettings.EnabledSetting}：若前面有終止 HTTPS 的代理，"
                + "每個請求看起來都是 http，會被無限轉址。放在代理後面時請同時設定 ForwardedHeaders:*。");
        }

        if (Proxy.TrustAllProxies && (Proxy.KnownProxies.Count > 0 || Proxy.KnownNetworks.Count > 0))
        {
            warnings.Add(
                $"{ProxySettings.TrustAllProxiesSetting} 是 true，{ProxySettings.KnownProxiesSetting} 與 {ProxySettings.KnownNetworksSetting} 會被忽略："
                + "任何來源送的轉送標頭都會被信任。只想信任特定代理的話，把 TrustAllProxies 拿掉。");
        }

        return warnings;
    }
}

internal static partial class TransportSecurityPipeline
{
    /// <summary>平台的健康檢查常走內部的 http，轉到 https 的話它會一直以為容器壞了。</summary>
    private static readonly PathString HealthPrefix = HealthEndpoints.Route;

    /// <summary>讀設定並註冊；設定不合規定時在這裡就讓啟動失敗。</summary>
    public static IServiceCollection AddTransportSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = TransportSecurity.From(configuration);
        services.AddSingleton(settings);
        if (settings.Proxy.Enabled)
        {
            services.Configure<ForwardedHeadersOptions>(settings.Proxy.Apply);
        }

        return services;
    }

    /// <summary>正式環境與開發都記：設定的組合看起來有問題但不到要失敗時，部署的人才看得到。</summary>
    public static void WarnAboutTransportSettings(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TransportSecurity).FullName!);
        foreach (var warning in app.Services.GetRequiredService<TransportSecurity>().Warnings(app.Environment.IsProduction()))
        {
            LogWarning(logger, warning);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Warning}")]
    private static partial void LogWarning(ILogger logger, string warning);

    /// <summary>
    /// 讀轉送的標頭。排在限流、確認登入與 Cookie 之前，它們看到的才是用戶端的 IP 與 scheme，不是代理的。
    /// </summary>
    public static IApplicationBuilder UseForwardedProxyHeaders(this IApplicationBuilder app) =>
        app.ApplicationServices.GetRequiredService<TransportSecurity>().Proxy.Enabled ? app.UseForwardedHeaders() : app;

    /// <summary>
    /// 不是 HTTPS 的請求回 308 到同一個網址的 https：308 保留方法與內文，POST 不會變成 GET。
    /// 排在讀轉送的標頭之後才知道用戶端用的是不是 HTTPS；排在其他東西之前，轉走的請求不讀 Cookie、不吃限流的額度。
    /// </summary>
    public static IApplicationBuilder UseHttpsRequirement(this IApplicationBuilder app) =>
        app.ApplicationServices.GetRequiredService<TransportSecurity>().RequireHttps ? app.Use(RedirectToHttpsAsync) : app;

    private static Task RedirectToHttpsAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (request.IsHttps || request.Path.StartsWithSegments(HealthPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return next(context);
        }

        // 設了 PublicHost 就只信它；沒設時才用請求的 Host，而且不帶連接埠：
        // http 的連接埠（例如代理後面的 8080）不是 https 的，https 用預設的 443。
        var publicHost = context.RequestServices.GetRequiredService<TransportSecurity>().PublicHost;
        var host = publicHost is null ? new HostString(request.Host.Host) : new HostString(publicHost);
        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = UriHelper.BuildAbsolute(
            Uri.UriSchemeHttps, host, request.PathBase, request.Path, request.QueryString);
        return Task.CompletedTask;
    }
}
