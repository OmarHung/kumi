using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PmTool.Api.OAuth;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Identity;

namespace PmTool.Api.Security;

/// <summary>
/// 第二種驗證：<c>Authorization: Bearer pmt_…</c>。請求帶了這個標頭就只看它，不看 Cookie；
/// 權杖無效、過期、撤銷、帳號停用時身分是匿名，需要登入的端點回 401 <c>auth.required</c>。
/// </summary>
internal static class AccessTokenAuthentication
{
    /// <summary>預設的驗證方式：依標頭選 Cookie 或權杖，不自己驗證任何東西。</summary>
    public const string SelectorScheme = "pmtool";

    public const string Scheme = "AccessToken";

    private const string BearerPrefix = "Bearer ";

    /// <summary>有帶 Bearer 標頭（不管權杖有沒有效）。驗證方式的選擇與防偽標頭的豁免都靠它，兩邊才不會對「這是權杖請求」有不同的看法。</summary>
    public static bool IsBearerRequest(HttpRequest request) =>
        request.Headers.Authorization is [{ } value] && value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase);

    public static string? TryReadSecret(HttpRequest request) =>
        IsBearerRequest(request) ? request.Headers.Authorization[0]![BearerPrefix.Length..].Trim() : null;
}

internal sealed class AccessTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAccessTokenService tokens,
    BearerFailureLimiter failures) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string RetryAfterItem = "pmtool.bearer-blocked-retry-after";

    private const string McpOnlyItem = "pmtool.bearer-mcp-only";

    private const string McpOnlyMessage = "這把權杖是 OAuth 授權發的，只能用在 MCP 端點（/mcp），不能呼叫其他 API。";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (AccessTokenAuthentication.TryReadSecret(Request) is not { } secret)
        {
            return AuthenticateResult.NoResult();
        }

        // 主機代理的機器憑證與工具授權不是任何人的身分（HostCredentials 自己驗證）：在這裡一律匿名，也不算失敗。
        if (HostCredentials.IsMachineCredential(secret))
        {
            return AuthenticateResult.NoResult();
        }

        // 在查資料庫之前先看這個來源失敗了幾次：驗證排在限流之前，沒有這一關，亂數的假權杖每個都要查一次。
        var source = ApiRateLimiting.AddressKeyOf(Context.Connection.RemoteIpAddress);
        if (failures.IsBlocked(source, out var retryAfter))
        {
            Context.Items[RetryAfterItem] = retryAfter;
            return AuthenticateResult.Fail("存取權杖驗證失敗太多次。");
        }

        if (await tokens.FindAsync(secret, Context.RequestAborted) is not { } token)
        {
            failures.RecordFailure(source);
            return AuthenticateResult.Fail("存取權杖無效。");
        }

        // 權杖綁在發給它的 resource（RFC 8707）：OAuth 授權發的只能打 /mcp。權杖本身是有效的，所以不算驗證失敗。
        if (token.GrantId is not null && !Request.Path.StartsWithSegments(OAuthServerResolver.McpPath, StringComparison.OrdinalIgnoreCase))
        {
            Context.Items[McpOnlyItem] = true;
            return AuthenticateResult.Fail("這把權杖只能用在 MCP。");
        }

        var principal = SessionPrincipal.CreateForAccessToken(Scheme.Name, token);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.TryGetValue(RetryAfterItem, out var blocked) && blocked is TimeSpan retryAfter)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return ApiFailure.RateLimited.WriteAsync(Response, Context.RequestAborted);
        }

        Response.Headers.WWWAuthenticate = "Bearer";
        var failure = Context.Items.ContainsKey(McpOnlyItem) ? ApiFailure.AuthRequired with { Message = McpOnlyMessage } : ApiFailure.AuthRequired;
        return failure.WriteAsync(Response, Context.RequestAborted);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ApiFailure.Forbidden.WriteAsync(Response, Context.RequestAborted);
}
