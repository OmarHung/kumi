using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>記憶體裡的一把權杖。</summary>
internal sealed record FakeAccessToken(
    AccessTokenSummary Summary, Guid AccountId, string Secret, bool Revoked, Guid? GrantId = null);

/// <summary>
/// 測試用的存取權杖。規則只做 API 測試看得到的大意（名稱、範圍、上限、撤銷、過期、帳號停用）；
/// 完整的規則在身分模組自己的測試裡。帳號現在的資料問 <see cref="FakeAuthenticationService"/>。
/// </summary>
internal sealed class FakeAccessTokenService(FakeAuthenticationService accounts, TimeProvider time) : IAccessTokenService
{
    private readonly ConcurrentDictionary<Guid, FakeAccessToken> _tokens = new();
    private readonly ConcurrentQueue<string> _findCalls = new();

    public IReadOnlyList<string> FindCalls => _findCalls.ToArray();

    /// <summary>查過而且當時有效的權杖，模擬真的服務程序裡的快取；<see cref="TryFindCached"/> 只看它。</summary>
    private readonly ConcurrentDictionary<string, bool> _cached = new();

    private readonly ConcurrentQueue<string> _cacheLookups = new();

    /// <summary><see cref="TryFindCached"/> 被問過哪些權杖（依序）。</summary>
    public IReadOnlyList<string> CacheLookups => _cacheLookups.ToArray();

    /// <summary>直接替帳號發一把權杖，回傳明文。測試準備權杖用。</summary>
    public string Issue(AuthenticatedUser user, params AccessTokenScope[] scopes) =>
        Issue(user, null, scopes);

    public string Issue(AuthenticatedUser user, int? expiresInDays, params AccessTokenScope[] scopes) =>
        Create(user.AccountId, new NewAccessToken("測試用", scopes, expiresInDays)).Secret;

    /// <summary>替帳號發一把用瀏覽器登入建立的權杖（帶電腦名稱）。</summary>
    public string IssueFromMachine(AuthenticatedUser user, string machineName, params AccessTokenScope[] scopes) =>
        Create(user.AccountId, new NewAccessToken("kumi", scopes, null), null, machineName).Secret;

    /// <summary>替帳號發一把 OAuth 授權發出的權杖（有授權編號，只能用在 MCP 端點），回傳明文。</summary>
    public string IssueForGrant(AuthenticatedUser user, Guid grantId, params AccessTokenScope[] scopes) =>
        Create(user.AccountId, new NewAccessToken("OAuth 授權", scopes, null), grantId).Secret;

    /// <summary>撤銷這個明文的權杖，模擬別的地方撤銷了它。</summary>
    public void Revoke(string secret)
    {
        foreach (var (id, token) in _tokens.Where(pair => pair.Value.Secret == secret))
        {
            _tokens[id] = token with { Revoked = true };
        }
    }

    public Task<Result<CreatedAccessToken>> CreateAsync(Actor actor, NewAccessToken token, CancellationToken cancellationToken)
    {
        if (token.Name.Trim().Length == 0)
        {
            return Task.FromResult<Result<CreatedAccessToken>>(Invalid(IdentityErrorCodes.AccessTokenNameRequired, "name"));
        }

        if (token.Scopes.Count == 0)
        {
            return Task.FromResult<Result<CreatedAccessToken>>(Invalid(IdentityErrorCodes.AccessTokenScopesRequired, "scopes"));
        }

        if (token.ExpiresInDays is < 1 or > AccessTokenRules.MaxExpiresInDays)
        {
            return Task.FromResult<Result<CreatedAccessToken>>(Invalid(IdentityErrorCodes.AccessTokenExpiryInvalid, "expiresInDays"));
        }

        var accountId = AccountIdOf(actor);
        if (_tokens.Values.Count(t => t.AccountId == accountId && IsUsable(t)) >= AccessTokenRules.MaxActiveTokensPerAccount)
        {
            return Task.FromResult<Result<CreatedAccessToken>>(
                new Failure(FailureKind.Conflict, IdentityErrorCodes.AccessTokenLimitReached, "權杖太多了。"));
        }

        var created = Create(accountId, token);
        return Task.FromResult<Result<CreatedAccessToken>>(new CreatedAccessToken(created.Summary, created.Secret));
    }

    public Task<IReadOnlyList<AccessTokenSummary>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        var accountId = AccountIdOf(actor);
        IReadOnlyList<AccessTokenSummary> list = [.. _tokens.Values
            .Where(t => t.AccountId == accountId && !t.Revoked)
            .OrderByDescending(t => t.Summary.CreatedAt)
            .Select(t => t.Summary)];
        return Task.FromResult(list);
    }

    public Task<Result<AccessTokenSummary>> RevokeAsync(Actor actor, Guid tokenId, CancellationToken cancellationToken)
    {
        var accountId = AccountIdOf(actor);
        if (!_tokens.TryGetValue(tokenId, out var token) || token.AccountId != accountId || token.Revoked)
        {
            return Task.FromResult<Result<AccessTokenSummary>>(
                new Failure(FailureKind.NotFound, IdentityErrorCodes.AccessTokenNotFound, "找不到這把權杖。"));
        }

        _tokens[tokenId] = token with { Revoked = true };
        return Task.FromResult<Result<AccessTokenSummary>>(token.Summary);
    }

    public Task<ActiveAccessToken?> FindAsync(string token, CancellationToken cancellationToken)
    {
        _findCalls.Enqueue(token);
        var found = _tokens.Values.FirstOrDefault(t => t.Secret == token);
        if (found is null || !IsUsable(found) || accounts.Current(found.AccountId) is not { } user)
        {
            return Task.FromResult<ActiveAccessToken?>(null);
        }

        _cached[token] = true;
        return Task.FromResult<ActiveAccessToken?>(
            new ActiveAccessToken(found.Summary.TokenId, user, found.Summary.Scopes) { GrantId = found.GrantId });
    }

    public ActiveAccessToken? TryFindCached(string token)
    {
        _cacheLookups.Enqueue(token);
        var found = _tokens.Values.FirstOrDefault(t => t.Secret == token);
        if (!_cached.ContainsKey(token) || found is null || !IsUsable(found) || accounts.Current(found.AccountId) is not { } user)
        {
            return null;
        }

        return new ActiveAccessToken(found.Summary.TokenId, user, found.Summary.Scopes) { GrantId = found.GrantId };
    }

    private FakeAccessToken Create(Guid accountId, NewAccessToken token, Guid? grantId = null, string? machineName = null)
    {
        var now = time.GetUtcNow();
        var secret = $"{AccessTokenRules.Prefix}{Guid.NewGuid():N}";
        var summary = new AccessTokenSummary(
            Guid.CreateVersion7(), token.Name.Trim(), secret[..8], token.Scopes, now, null,
            token.ExpiresInDays is { } days ? now.AddDays(days) : null) { MachineName = machineName };
        var created = new FakeAccessToken(summary, accountId, secret, Revoked: false, grantId);
        _tokens[summary.TokenId] = created;
        return created;
    }

    private bool IsUsable(FakeAccessToken token) =>
        !token.Revoked && (token.Summary.ExpiresAt is null || time.GetUtcNow() < token.Summary.ExpiresAt);

    private Guid AccountIdOf(Actor actor) =>
        TestUsers.All.Single(user => user.PersonId == actor.PersonId).AccountId;

    private static Failure Invalid(string code, string path) =>
        new(FailureKind.Invalid, code, "不合規定。") { Details = [new FailureDetail(code, path, "不合規定。")] };
}
