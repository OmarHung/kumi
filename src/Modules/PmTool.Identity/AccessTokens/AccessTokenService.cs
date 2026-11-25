using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.Audit;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.AccessTokens;

internal sealed class AccessTokenService(
    IDbContextFactory<IdentityDbContext> contexts,
    SessionCache cache,
    AuditTrail audit,
    AccountEvents events,
    IRequestSource requestSource,
    ICurrentSession currentSession,
    TimeProvider time) : IAccessTokenService
{
    private static readonly Failure SessionEnded =
        new(FailureKind.Forbidden, IdentityErrorCodes.SessionEnded, "工作階段已經失效，請重新登入。");

    private static readonly Failure LimitReached = new(
        FailureKind.Conflict, IdentityErrorCodes.AccessTokenLimitReached,
        $"最多同時有 {AccessTokenRules.MaxActiveTokensPerAccount} 把還能用的權杖，請先撤銷不用的。");

    private static readonly Failure NotFound =
        new(FailureKind.NotFound, IdentityErrorCodes.AccessTokenNotFound, "找不到這把權杖。");

    public async Task<Result<CreatedAccessToken>> CreateAsync(Actor actor, NewAccessToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(token);

        var checkedName = AccessTokenInput.Check(token);
        if (checkedName.Failure is { } invalid)
        {
            return invalid;
        }

        var now = time.GetUtcNow();
        var accountId = await AccountIdOfAsync(actor, cancellationToken);
        var (row, secret) = AccessTokenWriter.NewRow(
            actor.OrganizationId, accountId, checkedName.Value, token.Scopes, token.ExpiresInDays, null, requestSource.Current?.Address, now);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 拿到帳號的鎖之後才確認呼叫的工作階段：改密碼已經提交的話，這裡一定看得到它作廢了這個工作階段。
        if (!await AccessTokenWriter.LockAccountAsync(db, row.OrganizationId, row.AccountId, cancellationToken)
            || !await SessionStore.IsCallerSessionActiveAsync(db, row.OrganizationId, row.AccountId, currentSession.Token, now, cancellationToken))
        {
            return SessionEnded;
        }

        await AccessTokenStore.DeleteEndedAsync(db, row.OrganizationId, now, cancellationToken);
        var active = await AccessTokenWriter.CountActiveAsync(db, row.OrganizationId, row.AccountId, now, cancellationToken);
        if (active >= AccessTokenRules.MaxActiveTokensPerAccount)
        {
            return LimitReached;
        }

        db.AccessTokens.Add(row);
        var eventId = await audit.AddAsync(
            db,
            new AuditRecord(row.OrganizationId, AuditActions.AccessTokenCreated)
            {
                ActorPersonId = actor.PersonId,
                TargetPersonId = actor.PersonId,
                Details = new Dictionary<string, string>
                {
                    ["tokenName"] = row.Name,
                    ["scopes"] = string.Join(' ', row.Scopes),
                    ["expiresAt"] = row.ExpiresAt?.ToString("O") ?? string.Empty,
                    ["via"] = "web",
                },
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 提交之後才通知：回復的操作不該發出通知；通知失敗也不影響已經建好的權杖。
        var summary = Summarize(row);
        await events.PublishAsync(new AccountEvent(eventId, row.OrganizationId, actor.PersonId, AccountEventKind.AccessTokenCreated, now)
        {
            Tokens = [new AccountEventToken(row.Name, summary.Scopes, row.ExpiresAt)],
            SourceAddress = row.CreatedFrom,
        });
        return new CreatedAccessToken(summary, secret);
    }

    public async Task<IReadOnlyList<AccessTokenSummary>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var accountId = await AccountIdOfAsync(actor, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.AccessTokens
            .Where(t => t.OrganizationId == actor.OrganizationId && t.AccountId == accountId && t.GrantId == null && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return [.. rows.Select(Summarize)];
    }

    public async Task<Result<AccessTokenSummary>> RevokeAsync(Actor actor, Guid tokenId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var accountId = await AccountIdOfAsync(actor, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.AccessTokens
            .Where(t => t.Id == tokenId && t.OrganizationId == actor.OrganizationId && t.AccountId == accountId && t.GrantId == null && t.RevokedAt == null)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return NotFound;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var revoked = await db.AccessTokens
            .Where(t => t.Id == row.Id && t.OrganizationId == row.OrganizationId && t.GrantId == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, (DateTimeOffset?)time.GetUtcNow())
                    .SetProperty(t => t.RevokedReason, SessionEndReasons.TokenRevoked),
                cancellationToken);
        if (revoked == 0)
        {
            return NotFound;
        }

        await audit.AddAsync(
            db,
            new AuditRecord(row.OrganizationId, AuditActions.AccessTokenRevoked)
            {
                ActorPersonId = actor.PersonId,
                TargetPersonId = actor.PersonId,
                Details = new Dictionary<string, string> { ["tokenName"] = row.Name },
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        cache.Forget(accountId);
        return Summarize(row);
    }

    public async Task<ActiveAccessToken?> FindAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!AccessTokenSecret.TryHash(token, out var hash))
        {
            return null;
        }

        // 查無此權杖的不留在快取裡（keepUnknown: false）：任何人都能送出無數個格式正確的假權杖。
        var found = await cache.GetAsync(
            SessionToken.AccessTokenCacheKeyOf(hash), () => LookUpAsync(hash), cancellationToken, keepUnknown: false);
        return found.Token;
    }

    public ActiveAccessToken? TryFindCached(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return AccessTokenSecret.TryHash(token, out var hash)
            ? cache.TryPeek(SessionToken.AccessTokenCacheKeyOf(hash))?.Token
            : null;
    }

    /// <summary>
    /// 快取沒有可用的結果時才走到這裡。不給取消權杖：這次查詢由所有在等的請求共用，
    /// 不能因為觸發它的那一個斷線就中止。
    /// </summary>
    private async Task<SessionLookup> LookUpAsync(byte[] hash)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        var now = time.GetUtcNow();
        var found = await AccessTokenStore.FindAsync(db, hash, CancellationToken.None);
        if (found is null)
        {
            return SessionLookup.Unknown;
        }

        // 讀到之後、記下使用時刻之前，它可能剛被撤銷；記不下去就當作無效。
        if (!AccessTokenStore.IsActive(found, now) || !await AccessTokenStore.TouchAsync(db, found.Token, now, CancellationToken.None))
        {
            return new SessionLookup(found.Account.Id, null);
        }

        // 授權的最後使用時刻跟著權杖走：有快取，所以最多每個快取期間寫一次。
        if (found.Token.GrantId is { } grantId)
        {
            await OAuth.OAuthGrantStore.TouchAsync(db, found.Token.OrganizationId, grantId, now, CancellationToken.None);
        }

        return new SessionLookup(found.Account.Id, null)
        {
            Token = new ActiveAccessToken(found.Token.Id, found.User, ScopesOf(found.Token)) { GrantId = found.Token.GrantId },
            TokenExpiresAt = found.Token.ExpiresAt,
        };
    }

    /// <summary>Actor 只有人的編號，權杖掛在帳號上；沒有帳號的人不能有權杖。</summary>
    private async Task<Guid> AccountIdOfAsync(Actor actor, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var ids = await db.Accounts
            .Where(a => a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        return ids.Count == 1 ? ids[0] : Guid.Empty;
    }

    internal static IReadOnlyList<AccessTokenScope> ScopesOf(AccessTokenRow row) =>
        [.. row.Scopes.Select(name => AccessTokenScopes.TryParse(name, out var scope) ? (AccessTokenScope?)scope : null).OfType<AccessTokenScope>()];

    internal static AccessTokenSummary Summarize(AccessTokenRow row) =>
        new(row.Id, row.Name, row.DisplayPrefix, ScopesOf(row), row.CreatedAt, row.LastUsedAt, row.ExpiresAt) { MachineName = row.MachineName, CreatedFrom = row.CreatedFrom };
}
