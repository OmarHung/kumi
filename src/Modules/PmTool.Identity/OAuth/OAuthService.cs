using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.Audit;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.OAuth;

/// <summary>
/// <see cref="IOAuthService"/>：授權請求交給 <see cref="OAuthAuthorization"/>、token 端點交給 <see cref="OAuthTokenEndpoint"/>、
/// 用戶端交給 <see cref="OAuthClientDirectory"/>；這裡只剩「我的帳號」的授權清單與撤銷。
/// </summary>
internal sealed class OAuthService(
    IDbContextFactory<IdentityDbContext> contexts,
    OAuthAuthorization authorization,
    OAuthTokenEndpoint tokens,
    OAuthClientDirectory clients,
    SessionCache cache,
    AuditTrail audit,
    TimeProvider time) : IOAuthService
{
    private static readonly Failure GrantNotFound = new(FailureKind.NotFound, IdentityErrorCodes.OAuthGrantNotFound, "找不到這筆授權。");

    public Task<OAuthResult<OAuthAuthorizationRequest>> CheckAuthorizationAsync(
        OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken) =>
        authorization.CheckAsync(server, parameters, cancellationToken);

    public Task<OAuthResult<string>> ApproveAsync(
        OAuthServer server,
        Actor actor,
        OAuthAuthorizeParameters parameters,
        IReadOnlyList<AccessTokenScope> grantedScopes,
        CancellationToken cancellationToken) =>
        authorization.ApproveAsync(server, actor, parameters, grantedScopes, cancellationToken);

    public Task<OAuthResult<string>> DenyAsync(OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken) =>
        authorization.DenyAsync(server, parameters, cancellationToken);

    public Task<OAuthResult<OAuthTokens>> ExchangeCodeAsync(OAuthServer server, OAuthCodeExchange request, CancellationToken cancellationToken) =>
        tokens.ExchangeCodeAsync(request, cancellationToken);

    public Task<OAuthResult<OAuthTokens>> RefreshAsync(OAuthServer server, OAuthRefreshRequest request, CancellationToken cancellationToken) =>
        tokens.RefreshAsync(request, cancellationToken);

    public Task RevokeTokenAsync(string token, CancellationToken cancellationToken) => tokens.RevokeTokenAsync(token, cancellationToken);

    public Task<OAuthResult<OAuthRegisteredClient>> RegisterClientAsync(OAuthClientRegistration registration, CancellationToken cancellationToken) =>
        clients.RegisterAsync(registration, cancellationToken);

    public async Task<IReadOnlyList<OAuthGrantSummary>> ListGrantsAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var accountId = await AccountIdOfAsync(db, actor, cancellationToken);
        var rows = await db.OAuthGrants
            .Where(g => g.OrganizationId == actor.OrganizationId && g.AccountId == accountId && g.RevokedAt == null)
            .OrderByDescending(g => g.LastUsedAt ?? g.CreatedAt)
            .ThenByDescending(g => g.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return
        [
            .. rows.Select(g => new OAuthGrantSummary(
                g.Id, g.ClientName, g.ClientHost, OAuthText.ScopesFromNames(g.Scopes), g.CreatedAt, g.LastUsedAt)),
        ];
    }

    public async Task<Result<Unit>> RevokeGrantAsync(Actor actor, Guid grantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var accountId = await AccountIdOfAsync(db, actor, cancellationToken);
        var grant = await db.OAuthGrants.AsNoTracking().SingleOrDefaultAsync(
            g => g.Id == grantId && g.OrganizationId == actor.OrganizationId && g.AccountId == accountId && g.RevokedAt == null, cancellationToken);
        if (grant is null)
        {
            return GrantNotFound;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var revoked = await OAuthGrantStore.RevokeGrantAsync(
            db, actor.OrganizationId, grantId, SessionEndReasons.OAuthGrantRevoked, time.GetUtcNow(), cancellationToken);
        if (revoked)
        {
            await audit.AddAsync(
                db,
                new AuditRecord(actor.OrganizationId, AuditActions.OAuthGrantRevoked)
                {
                    ActorPersonId = actor.PersonId,
                    TargetPersonId = actor.PersonId,
                    Details = new Dictionary<string, string> { ["client"] = grant.ClientName },
                },
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // 提交之後才清快取：同一個程序裡這筆授權的權杖立刻失效。
        cache.Forget(accountId);
        return revoked ? Unit.Value : GrantNotFound;
    }

    /// <summary>Actor 只有人的編號；授權掛在帳號上。沒有帳號的人不可能有授權（回 Empty，什麼都查不到）。</summary>
    private static async Task<Guid> AccountIdOfAsync(IdentityDbContext db, Actor actor, CancellationToken cancellationToken)
    {
        var ids = await db.Accounts
            .Where(a => a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        return ids.Count == 1 ? ids[0] : Guid.Empty;
    }
}
