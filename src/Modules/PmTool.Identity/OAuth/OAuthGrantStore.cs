using Microsoft.EntityFrameworkCore;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.OAuth;

/// <summary>授權、授權碼、更新權杖的寫入。撤銷與清理的規則只有這一份。</summary>
internal static class OAuthGrantStore
{
    /// <summary>OAuth 的存取權杖過期這麼久之後才刪（短效，沒必要留 30 天）。</summary>
    private static readonly TimeSpan ExpiredAccessTokenRetention = TimeSpan.FromDays(1);

    private static readonly TimeSpan EndedRetention = TimeSpan.FromDays(30);

    public static Task<int> TouchAsync(
        IdentityDbContext db, Guid organizationId, Guid grantId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.OAuthGrants
            .Where(g => g.Id == grantId && g.OrganizationId == organizationId && g.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.LastUsedAt, (DateTimeOffset?)now), cancellationToken);

    /// <summary>
    /// 發權杖的交易一開始就呼叫：鎖住授權那一列，同時確認它還沒撤銷、帳號沒停用，回傳能不能發。
    /// 撤銷授權的路徑（<see cref="RevokeGrantAsync"/>、<see cref="RevokeAllForAccountAsync"/>）第一步都是更新同一列，
    /// 所以撤銷與發權杖一定一先一後：撤銷在前，這裡看得到；撤銷在後，它會等到權杖發完，再把剛發的一起撤銷。
    /// 兩個都不在鎖裡做的話，撤銷剛好擦身而過，會留下撤銷之後仍然有效的存取權杖（一小時）。
    /// </summary>
    public static async Task<bool> LockActiveAsync(
        IdentityDbContext db, Guid organizationId, Guid grantId, CancellationToken cancellationToken)
    {
        var locked = await db.Database
            .SqlQuery<Guid>($"""
                SELECT g.id AS "Value" FROM identity.oauth_grants g
                JOIN identity.accounts a ON a.id = g.account_id
                WHERE g.id = {grantId} AND g.organization_id = {organizationId}
                    AND g.revoked_at IS NULL AND a.disabled_at IS NULL
                FOR UPDATE OF g
                """)
            .ToListAsync(cancellationToken);
        return locked.Count == 1;
    }

    /// <summary>授權現在的範圍（要在 <see cref="LockActiveAsync"/> 之後讀，那時不會再被別人改）。</summary>
    public static async Task<IReadOnlyList<AccessTokenScope>> CurrentScopesAsync(
        IdentityDbContext db, Guid organizationId, Guid grantId, CancellationToken cancellationToken)
    {
        var names = await db.OAuthGrants.AsNoTracking()
            .Where(g => g.Id == grantId && g.OrganizationId == organizationId)
            .Select(g => g.Scopes)
            .SingleAsync(cancellationToken);
        return OAuthText.ScopesFromNames(names);
    }

    /// <summary>
    /// 授權縮小範圍時：這筆授權裡範圍超過新範圍的存取權杖當場撤銷。更新權杖不必撤銷，它之後換出的權杖本來就照授權現在的範圍
    /// （<see cref="CurrentScopesAsync"/>）；呼叫端提交之後要清快取。
    /// </summary>
    public static Task<int> RevokeBeyondScopesAsync(
        IdentityDbContext db, Guid organizationId, Guid grantId, string[] scopeNames, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE identity.access_tokens SET revoked_at = {now}, revoked_reason = {reason}
            WHERE organization_id = {organizationId} AND grant_id = {grantId} AND revoked_at IS NULL
                AND NOT (scopes <@ {scopeNames})
            """, cancellationToken);

    /// <summary>
    /// 撤銷整筆授權：授權本身、它所有的存取權杖與更新權杖。呼叫端在同一個交易裡呼叫，
    /// 提交之後要清快取（<see cref="Sessions.SessionCache.Forget"/>），同一個程序裡才立刻失效。
    /// 回傳這次是不是真的撤銷了一筆還沒撤銷的授權。
    /// </summary>
    public static async Task<bool> RevokeGrantAsync(
        IdentityDbContext db, Guid organizationId, Guid grantId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var revoked = await db.OAuthGrants
            .Where(g => g.Id == grantId && g.OrganizationId == organizationId && g.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(g => g.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(g => g.RevokedReason, reason),
                cancellationToken);

        // 授權已經撤銷時也再掃一次：之前沒撤乾淨的權杖（例如撤銷到一半的程序掛了）在這裡補上。
        await RevokeAccessTokensAsync(db.AccessTokens.Where(t => t.OrganizationId == organizationId && t.GrantId == grantId), reason, now, cancellationToken);
        await db.OAuthRefreshTokens
            .Where(t => t.OrganizationId == organizationId && t.GrantId == grantId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, (DateTimeOffset?)now), cancellationToken);
        return revoked == 1;
    }

    /// <summary>
    /// 撤銷一個帳號所有有效的授權：授權、它們的存取權杖與更新權杖。改密碼、停用、管理員重設密碼用，
    /// 和作廢工作階段在同一個交易。不然外流的更新權杖在重設密碼之後還能換新的，停用再啟用之後舊的更新權杖也會活過來。
    /// 呼叫端提交之後要清快取。
    /// </summary>
    public static async Task RevokeAllForAccountAsync(
        IdentityDbContext db, Guid organizationId, Guid accountId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.OAuthGrants
            .Where(g => g.OrganizationId == organizationId && g.AccountId == accountId && g.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(g => g.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(g => g.RevokedReason, reason),
                cancellationToken);
        await RevokeAccessTokensAsync(
            db.AccessTokens.Where(t => t.OrganizationId == organizationId && t.AccountId == accountId), reason, now, cancellationToken);
        await db.OAuthRefreshTokens
            .Where(t => t.OrganizationId == organizationId && t.RevokedAt == null
                && db.OAuthGrants.Any(g => g.Id == t.GrantId && g.AccountId == accountId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, (DateTimeOffset?)now), cancellationToken);
    }

    /// <summary>撤銷從同一個授權碼換下來的一整串更新權杖，與和它們同時發出的存取權杖。授權本身不動。</summary>
    public static async Task RevokeFamilyAsync(
        IdentityDbContext db, Guid organizationId, Guid familyId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var accessTokenIds = await db.OAuthRefreshTokens
            .Where(t => t.OrganizationId == organizationId && t.FamilyId == familyId)
            .Select(t => t.AccessTokenId)
            .ToListAsync(cancellationToken);
        await RevokeAccessTokensAsync(
            db.AccessTokens.Where(t => t.OrganizationId == organizationId && accessTokenIds.Contains(t.Id)), reason, now, cancellationToken);
        await db.OAuthRefreshTokens
            .Where(t => t.OrganizationId == organizationId && t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, (DateTimeOffset?)now), cancellationToken);
    }

    public static Task RevokeAccessTokenAsync(
        IdentityDbContext db, Guid organizationId, Guid tokenId, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        RevokeAccessTokensAsync(db.AccessTokens.Where(t => t.OrganizationId == organizationId && t.Id == tokenId), reason, now, cancellationToken);

    /// <summary>
    /// 發新權杖時順手清這個組織的舊資料：過期一天以上的 OAuth 存取權杖、過了一天的授權碼、過期或撤銷很久的更新權杖。
    /// 用掉但還沒過期的更新權杖要留著：它再出現時才認得出是重複使用。
    /// </summary>
    public static async Task DeleteEndedAsync(IdentityDbContext db, Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var accessCutoff = now - ExpiredAccessTokenRetention;
        var endedCutoff = now - EndedRetention;
        await db.AccessTokens
            .Where(t => t.OrganizationId == organizationId && t.GrantId != null && t.ExpiresAt < accessCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        await db.OAuthCodes
            .Where(c => c.OrganizationId == organizationId && c.ExpiresAt < accessCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        await db.OAuthRefreshTokens
            .Where(t => t.OrganizationId == organizationId && (t.ExpiresAt < now || t.RevokedAt < endedCutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static Task RevokeAccessTokensAsync(
        IQueryable<AccessTokenRow> tokens, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        tokens.Where(t => t.GrantId != null && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(t => t.RevokedReason, reason),
                cancellationToken);
}
