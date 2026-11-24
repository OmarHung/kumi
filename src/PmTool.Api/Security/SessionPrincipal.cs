using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PmTool.Identity;

namespace PmTool.Api.Security;

/// <summary>
/// 工作階段的票證裡放什麼。票證整個加密在 Cookie 裡，用戶端看不到也改不了。
/// 真正說了算的是伺服器端的工作階段（權杖）；放齊 <see cref="AuthenticatedUser"/> 的欄位，
/// 是為了每個請求比對身分模組回的資料，沒變就不必換發 Cookie。
/// </summary>
internal static class SessionPrincipal
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    private const string AccountIdClaim = "pmtool:account_id";
    private const string PersonIdClaim = "pmtool:person_id";
    private const string OrganizationIdClaim = "pmtool:organization_id";
    private const string OrganizationNameClaim = "pmtool:organization_name";
    private const string DisplayNameClaim = "pmtool:display_name";
    private const string EmailClaim = "pmtool:email";
    private const string RoleClaim = "pmtool:organization_role";

    private const string AccessTokenIdClaim = "pmtool:access_token_id";
    private const string AccessTokenScopeClaim = "pmtool:access_token_scope";
    private const string GrantIdClaim = "pmtool:oauth_grant_id";

    /// <summary>伺服器端工作階段的權杖（明文）。只存在加密的 Cookie 裡，不寫進記錄。</summary>
    private const string SessionTokenClaim = "pmtool:session_token";

    public static ClaimsPrincipal Create(AuthenticatedUser user, string sessionToken) =>
        new(new ClaimsIdentity([.. UserClaims(user), new Claim(SessionTokenClaim, sessionToken)], Scheme, DisplayNameClaim, RoleClaim));

    /// <summary>
    /// 用存取權杖呼叫時的身分：使用者的資料和 Cookie 的一樣（<see cref="Read"/> 與限流照常可用），
    /// 另外帶權杖的編號與範圍。不放權杖的明文，也沒有工作階段的權杖。
    /// </summary>
    public static ClaimsPrincipal CreateForAccessToken(string scheme, ActiveAccessToken token)
    {
        var claims = UserClaims(token.User)
            .Append(new Claim(AccessTokenIdClaim, token.TokenId.ToString()))
            .Concat(token.Scopes.Select(scope => new Claim(AccessTokenScopeClaim, AccessTokenScopes.NameOf(scope))))
            .Concat(token.GrantId is { } grantId ? [new Claim(GrantIdClaim, grantId.ToString())] : []);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme, DisplayNameClaim, RoleClaim));
    }

    /// <summary>用存取權杖驗證的請求才有值：權杖的編號與範圍。Cookie 的票證與沒登入的都是 null。</summary>
    public static (Guid TokenId, IReadOnlyList<AccessTokenScope> Scopes)? TryReadAccessToken(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true || !TryGuid(principal, AccessTokenIdClaim, out var tokenId))
        {
            return null;
        }

        var scopes = principal.FindAll(AccessTokenScopeClaim)
            .Select(claim => AccessTokenScopes.TryParse(claim.Value, out var scope) ? (AccessTokenScope?)scope : null)
            .OfType<AccessTokenScope>()
            .ToArray();
        return (tokenId, scopes);
    }

    /// <summary>OAuth 授權發的權杖才有值：那筆授權的編號。個人存取權杖、Cookie 與沒登入的是 null。MCP 端點靠它分辨連線是哪個應用。</summary>
    public static Guid? TryReadGrantId(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true && TryGuid(principal, GrantIdClaim, out var grantId) ? grantId : null;

    private static Claim[] UserClaims(AuthenticatedUser user) =>
    [
        new(AccountIdClaim, user.AccountId.ToString()),
        new(PersonIdClaim, user.PersonId.ToString()),
        new(OrganizationIdClaim, user.OrganizationId.ToString()),
        new(OrganizationNameClaim, user.OrganizationName),
        new(DisplayNameClaim, user.DisplayName),
        new(EmailClaim, user.Email),
        new(RoleClaim, user.Role.ToString()),
    ];

    /// <summary>票證裡的使用者；沒登入或票證的內容不完整時是 null。</summary>
    public static AuthenticatedUser? TryRead(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true
            || !TryGuid(principal, AccountIdClaim, out var accountId)
            || !TryGuid(principal, PersonIdClaim, out var personId)
            || !TryGuid(principal, OrganizationIdClaim, out var organizationId)
            || principal.FindFirstValue(OrganizationNameClaim) is not { } organizationName
            || principal.FindFirstValue(DisplayNameClaim) is not { } displayName
            || principal.FindFirstValue(EmailClaim) is not { } email
            || !TryRole(principal, out var role))
        {
            return null;
        }

        return new AuthenticatedUser(accountId, personId, organizationId, organizationName, displayName, email, role);
    }

    /// <summary>給需要登入的端點用。走到這裡卻讀不到，表示授權的設定有漏洞，所以丟例外而不是默默當成匿名。</summary>
    public static AuthenticatedUser Read(ClaimsPrincipal principal) =>
        TryRead(principal) ?? throw new InvalidOperationException("這個端點需要登入，卻沒有有效的工作階段。");

    /// <summary>票證裡的工作階段權杖；沒登入或是加上伺服器端工作階段之前發的票證時是 null。</summary>
    public static string? TryReadToken(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true ? principal.FindFirstValue(SessionTokenClaim) : null;

    /// <summary>給需要登入的端點用，理由同 <see cref="Read"/>。</summary>
    public static string ReadToken(ClaimsPrincipal principal) =>
        TryReadToken(principal) ?? throw new InvalidOperationException("這個端點需要登入，卻沒有工作階段的權杖。");

    /// <summary>
    /// 剛登入的工作階段。票證的期限設成伺服器端的絕對期限：伺服器那一邊說了算，
    /// 但萬一沒問到，過了期限的 Cookie 在解開時就被擋下。不是持續性的 Cookie，關掉瀏覽器就沒了。
    /// </summary>
    public static AuthenticationProperties NewProperties(DateTimeOffset expiresAt) =>
        new() { IsPersistent = false, ExpiresUtc = expiresAt, AllowRefresh = false };

    private static bool TryGuid(ClaimsPrincipal principal, string type, out Guid value) =>
        Guid.TryParse(principal.FindFirstValue(type), out value);

    private static bool TryRole(ClaimsPrincipal principal, out OrganizationRole role) =>
        Enum.TryParse(principal.FindFirstValue(RoleClaim), ignoreCase: false, out role) && Enum.IsDefined(role);
}
