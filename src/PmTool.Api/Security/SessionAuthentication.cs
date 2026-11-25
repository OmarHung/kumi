using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using PmTool.Api.Responses;
using PmTool.Identity;

namespace PmTool.Api.Security;

internal static class SessionAuthentication
{
    public const string CookieName = "pmtool_session";

    /// <summary>
    /// 一律走 HTTPS 時的名稱。<c>__Host-</c> 前綴要求 Secure、Path=/、不帶 Domain，瀏覽器才肯收：
    /// 同網域底下的別的子網域或明文的連線，設不了也蓋不掉它。
    /// </summary>
    public const string HostCookieName = "__Host-pmtool_session";

    /// <summary>不是訪客的組織成員。訪客是客戶或外包，不該看到整個組織的人員與 Email。</summary>
    public const string OrganizationMembersPolicy = "organization-members";

    // 金鑰環以應用程式名稱區分。預設用內容根目錄的路徑，換了容器的路徑就讀不到舊的金鑰，所以固定下來。
    private const string ApplicationName = "pm-tool";

    /// <summary>
    /// 工作階段的 Cookie 用 Data Protection 的金鑰加密。金鑰存在哪裡由組合根接上（身分模組的資料庫），
    /// 這裡只管它的名稱與憑證：所有容器共用同一份金鑰，換容器、多開容器都不會讓人被登出。
    /// </summary>
    public static IServiceCollection AddSessionAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var requireHttps = TransportSecurity.From(configuration).RequireHttps;
        services.AddDataProtection().SetApplicationName(ApplicationName).ProtectKeysFromConfiguration(configuration);
        services.AddAuthentication(AccessTokenAuthentication.SelectorScheme)
            .AddPolicyScheme(AccessTokenAuthentication.SelectorScheme, null, options =>
                options.ForwardDefaultSelector = context => AccessTokenAuthentication.IsBearerRequest(context.Request)
                    ? AccessTokenAuthentication.Scheme
                    : SessionPrincipal.Scheme)
            .AddCookie(SessionPrincipal.Scheme, options => ConfigureCookie(options, requireHttps))
            .AddScheme<AuthenticationSchemeOptions, AccessTokenHandler>(AccessTokenAuthentication.Scheme, null);
        services.AddAuthorizationBuilder().AddPolicy(OrganizationMembersPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => SessionPrincipal.TryRead(context.User) is { Role: not OrganizationRole.Guest }));
        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, bool requireHttps)
    {
        options.Cookie.Name = requireHttps ? HostCookieName : CookieName;
        options.Cookie.HttpOnly = true;

        // __Host- 的條件之一：Path=/、不設 Domain（預設就是，明寫出來避免之後被改掉）。
        options.Cookie.Path = "/";
        options.Cookie.Domain = null;

        // Strict：從別的網站連過來的請求不帶 Cookie。防偽標頭是第二道防線。
        options.Cookie.SameSite = SameSiteMode.Strict;

        // 一律走 HTTPS 時固定 Secure；不然跟著請求（讀了轉送的標頭之後，代理後面的 https 也算）。
        // 本機的 http://localhost 要能登入，所以預設不能固定。
        options.Cookie.SecurePolicy = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

        // 期限由伺服器端的工作階段判斷（閒置與絕對期限都在身分模組），票證的期限只是它的絕對期限，不滑動延長：
        // 滑動延長會讓 Cookie 隔一陣子就換發一次，每個請求比對身分模組的資料時才換發就夠了。
        options.ExpireTimeSpan = TimeSpan.FromDays(IdentityRules.SessionAbsoluteDays);
        options.SlidingExpiration = false;
        options.Events = new CookieAuthenticationEvents
        {
            // 預設會導向登入頁；這是 API，網頁看到 401 自己決定回登入頁。
            OnRedirectToLogin = context => ApiFailure.AuthRequired.WriteAsync(context.Response, context.HttpContext.RequestAborted),
            OnRedirectToAccessDenied = context => ApiFailure.Forbidden.WriteAsync(context.Response, context.HttpContext.RequestAborted),
            OnValidatePrincipal = SessionValidation.ValidateAsync,
        };
    }
}
