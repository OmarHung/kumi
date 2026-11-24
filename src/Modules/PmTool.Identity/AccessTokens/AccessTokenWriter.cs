using Microsoft.EntityFrameworkCore;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.AccessTokens;

/// <summary>
/// 建立權杖的寫入：網頁建立（<see cref="AccessTokenService"/>）與命令列瀏覽器登入（<c>CliLogin</c>）共用，
/// 上限與鎖的規則才不會兩邊各一份。呼叫端自己開交易。
/// </summary>
internal static class AccessTokenWriter
{
    public static (AccessTokenRow Row, string Secret) NewRow(
        Guid organizationId,
        Guid accountId,
        string name,
        IReadOnlyList<AccessTokenScope> scopes,
        int? expiresInDays,
        string? machineName,
        DateTimeOffset now)
    {
        var (secret, hash) = AccessTokenSecret.Create();
        var row = new AccessTokenRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            AccountId = accountId,
            Name = name,
            TokenHash = hash,
            DisplayPrefix = secret[..AccessTokenSecret.DisplayPrefixLength],
            Scopes = [.. scopes.Select(AccessTokenScopes.NameOf)],
            CreatedAt = now,
            ExpiresAt = expiresInDays is { } days ? now.AddDays(days) : null,
            MachineName = machineName,
        };
        return (row, secret);
    }

    /// <summary>
    /// 鎖住帳號那一列：同時建權杖的請求排隊，上限才算得準；停用帳號也要改這一列，所以不會在停用之後還建出權杖。
    /// 帳號不存在或已停用回 false。
    /// </summary>
    public static async Task<bool> LockAccountAsync(IdentityDbContext db, Guid organizationId, Guid accountId, CancellationToken cancellationToken)
    {
        var locked = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM identity.accounts
                WHERE id = {accountId} AND organization_id = {organizationId} AND disabled_at IS NULL
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return locked.Count != 0;
    }

    /// <summary>還能用的（沒撤銷、沒過期）個人權杖數；OAuth 發的不算。</summary>
    public static Task<int> CountActiveAsync(
        IdentityDbContext db, Guid organizationId, Guid accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.AccessTokens.CountAsync(
            t => t.OrganizationId == organizationId && t.AccountId == accountId && t.GrantId == null
                && t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > now),
            cancellationToken);
}
