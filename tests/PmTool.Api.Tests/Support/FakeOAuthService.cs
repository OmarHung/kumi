using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 測試用的 OAuth 服務：回測試設定的結果，並記下收到的參數。規則在身分模組自己的測試裡；
/// 這裡只確認 API 把請求交對、把結果寫成對的形狀。
/// </summary>
internal sealed class FakeOAuthService : IOAuthService
{
    private readonly ConcurrentQueue<OAuthAuthorizeParameters> _authorizeCalls = new();
    private readonly ConcurrentQueue<OAuthServer> _servers = new();
    private readonly ConcurrentQueue<string> _revokedTokens = new();
    private readonly ConcurrentQueue<OAuthGrantSummary> _grants = new();

    public Func<OAuthAuthorizeParameters, OAuthResult<OAuthAuthorizationRequest>> OnCheck { get; set; } = _ => new OAuthAuthorizationRequest(
        "https://app.example.com/client.json", "範例代理", "app.example.com", "127.0.0.1:5000", true, AccessTokenScopes.Grantable);

    public Func<OAuthAuthorizeParameters, OAuthResult<string>> OnApprove { get; set; } = p =>
        $"{p.RedirectUri}?code=fake-code&state={p.State}&iss=https%3A%2F%2Fpm.example.com";

    public Func<OAuthAuthorizeParameters, OAuthResult<string>> OnDeny { get; set; } = p =>
        $"{p.RedirectUri}?error=access_denied&state={p.State}";

    public Func<OAuthCodeExchange, OAuthResult<OAuthTokens>> OnExchange { get; set; } =
        _ => new OAuthTokens("pmt_access", 3600, "pmr_refresh", "items:read");

    public Func<OAuthRefreshRequest, OAuthResult<OAuthTokens>> OnRefresh { get; set; } =
        _ => new OAuthTokens("pmt_access2", 3600, "pmr_refresh2", "items:read");

    public Func<OAuthClientRegistration, OAuthResult<OAuthRegisteredClient>> OnRegister { get; set; } = r =>
        new OAuthRegisteredClient("pmc_fake", r.ClientName ?? "", r.RedirectUris ?? [], new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));

    public IReadOnlyList<OAuthAuthorizeParameters> AuthorizeCalls => _authorizeCalls.ToArray();

    public IReadOnlyList<OAuthServer> Servers => _servers.ToArray();

    public IReadOnlyList<string> RevokedTokens => _revokedTokens.ToArray();

    public OAuthCodeExchange? LastExchange { get; private set; }

    public OAuthRefreshRequest? LastRefresh { get; private set; }

    public OAuthClientRegistration? LastRegistration { get; private set; }

    public IReadOnlyList<AccessTokenScope>? LastGrantedScopes { get; private set; }

    public Actor? LastActor { get; private set; }

    /// <summary>「我的帳號」的授權清單裡放一筆。</summary>
    public OAuthGrantSummary AddGrant(string name = "範例代理", string? host = "app.example.com")
    {
        var grant = new OAuthGrantSummary(
            Guid.CreateVersion7(), name, host, [AccessTokenScope.ItemsRead], new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero), null);
        _grants.Enqueue(grant);
        return grant;
    }

    public Task<OAuthResult<OAuthAuthorizationRequest>> CheckAuthorizationAsync(
        OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken)
    {
        Record(server, parameters);
        return Task.FromResult(OnCheck(parameters));
    }

    public Task<OAuthResult<string>> ApproveAsync(
        OAuthServer server, Actor actor, OAuthAuthorizeParameters parameters, IReadOnlyList<AccessTokenScope> grantedScopes, CancellationToken cancellationToken)
    {
        Record(server, parameters);
        LastActor = actor;
        LastGrantedScopes = grantedScopes;
        return Task.FromResult(OnApprove(parameters));
    }

    public Task<OAuthResult<string>> DenyAsync(OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken)
    {
        Record(server, parameters);
        return Task.FromResult(OnDeny(parameters));
    }

    public Task<OAuthResult<OAuthTokens>> ExchangeCodeAsync(OAuthServer server, OAuthCodeExchange request, CancellationToken cancellationToken)
    {
        _servers.Enqueue(server);
        LastExchange = request;
        return Task.FromResult(OnExchange(request));
    }

    public Task<OAuthResult<OAuthTokens>> RefreshAsync(OAuthServer server, OAuthRefreshRequest request, CancellationToken cancellationToken)
    {
        _servers.Enqueue(server);
        LastRefresh = request;
        return Task.FromResult(OnRefresh(request));
    }

    public Task RevokeTokenAsync(string token, CancellationToken cancellationToken)
    {
        _revokedTokens.Enqueue(token);
        return Task.CompletedTask;
    }

    public Task<OAuthResult<OAuthRegisteredClient>> RegisterClientAsync(OAuthClientRegistration registration, CancellationToken cancellationToken)
    {
        LastRegistration = registration;
        return Task.FromResult(OnRegister(registration));
    }

    public Task<IReadOnlyList<OAuthGrantSummary>> ListGrantsAsync(Actor actor, CancellationToken cancellationToken)
    {
        LastActor = actor;
        return Task.FromResult<IReadOnlyList<OAuthGrantSummary>>(_grants.ToArray());
    }

    public Task<Result<Unit>> RevokeGrantAsync(Actor actor, Guid grantId, CancellationToken cancellationToken)
    {
        var found = _grants.FirstOrDefault(g => g.GrantId == grantId);
        if (found is null)
        {
            return Task.FromResult<Result<Unit>>(new Failure(FailureKind.NotFound, IdentityErrorCodes.OAuthGrantNotFound, "找不到這筆授權。"));
        }

        var rest = _grants.Where(g => g.GrantId != grantId).ToArray();
        _grants.Clear();
        foreach (var grant in rest)
        {
            _grants.Enqueue(grant);
        }

        return Task.FromResult<Result<Unit>>(Unit.Value);
    }

    private void Record(OAuthServer server, OAuthAuthorizeParameters parameters)
    {
        _servers.Enqueue(server);
        _authorizeCalls.Enqueue(parameters);
    }
}
