using System.Web;
using PmTool.Abstractions;
using PmTool.Identity.Tests.OAuth;

namespace PmTool.Identity.Tests.CliLogin;

/// <summary>瀏覽器登入測試共用的步驟：組請求、核准、兌換。</summary>
internal static class CliLoginTestSupport
{
    public const int Port = 53682;

    public const string Origin = "203.0.113.7";

    public const string State = "abcdefghijklmnopqrstuv";

    public static readonly AccessTokenScope[] CliScopes = [AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead];

    public static CliLoginParameters Parameters(
        string challenge,
        string machine = "MacBook-Pro",
        IReadOnlyList<CliLoginTokenKind>? tokens = null,
        IReadOnlyList<AccessTokenScope>? scopes = null)
    {
        var kinds = tokens ?? [CliLoginTokenKind.Cli, CliLoginTokenKind.Agent];
        return new CliLoginParameters(Port, State, challenge, machine, kinds, scopes ?? (kinds.Contains(CliLoginTokenKind.Cli) ? CliScopes : []));
    }

    public static CliLoginGrant Grant(bool includeAgent = true, int? expiresInDays = 90, IReadOnlyList<AccessTokenScope>? cliScopes = null, string origin = Origin) =>
        new(cliScopes ?? CliScopes, includeAgent, expiresInDays, origin);

    /// <summary>核准一次，回傳碼與 PKCE 的 verifier。</summary>
    public static async Task<(string Code, string Verifier)> ApproveAsync(
        this IdentityTestHost host,
        AuthenticatedUser user,
        string machine = "MacBook-Pro",
        IReadOnlyList<CliLoginTokenKind>? tokens = null,
        CliLoginGrant? grant = null)
    {
        var (verifier, challenge) = OAuthTestSupport.NewPkce();
        var kinds = tokens ?? [CliLoginTokenKind.Cli, CliLoginTokenKind.Agent];
        var effectiveGrant = grant ?? (kinds.Contains(CliLoginTokenKind.Cli) ? Grant() : Grant(cliScopes: []));
        var result = await host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(challenge, machine, kinds), effectiveGrant, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
        return (QueryOf(result.Value.RedirectUri)["code"]!, verifier);
    }

    public static Task<Result<CliLoginTokens>> RedeemAsync(
        this IdentityTestHost host, string code, string verifier, params string[] replace) =>
        host.CliLogin.RedeemAsync(new CliLoginRedemption(code, verifier, replace, Origin), CancellationToken.None);

    public static Task<Result<CliLoginTokens>> RedeemFromAsync(this IdentityTestHost host, string code, string verifier, string origin) =>
        host.CliLogin.RedeemAsync(new CliLoginRedemption(code, verifier, [], origin), CancellationToken.None);

    public static System.Collections.Specialized.NameValueCollection QueryOf(string url) => HttpUtility.ParseQueryString(new Uri(url).Query);
}
