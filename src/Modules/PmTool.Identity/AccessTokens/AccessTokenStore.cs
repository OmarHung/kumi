using Microsoft.EntityFrameworkCore;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.AccessTokens;

/// <summary>一把權杖，加上它的帳號現在的資料。</summary>
internal sealed record TokenWithAccount
{
    public required AccessTokenRow Token { get; init; }

    public required AccountRow Account { get; init; }

    public required PersonRow Person { get; init; }

    public required OrganizationRow Organization { get; init; }

    public AuthenticatedUser User => new(
        Account.Id, Person.Id, Organization.Id, Organization.Name, Person.DisplayName, Account.Email, Person.Role);
}

/// <summary>權杖的查詢與撤銷。「還有效」的定義只有一個，在這裡。</summary>
internal static class AccessTokenStore
{
    /// <summary>結束（撤銷或過期）超過這麼久的權杖才刪。</summary>
    private static readonly TimeSpan EndedRetention = TimeSpan.FromDays(30);

    /// <summary>依雜湊找。權杖在全部組織裡唯一，查到之前不知道是哪個組織，所以這一個查詢不依組織過濾。</summary>
    public static Task<TokenWithAccount?> FindAsync(IdentityDbContext db, byte[] tokenHash, CancellationToken cancellationToken) =>
        (from token in db.AccessTokens
         join account in db.Accounts on token.AccountId equals account.Id
         join person in db.People on account.PersonId equals person.Id
         join organization in db.Organizations on person.OrganizationId equals organization.Id
         where token.TokenHash == tokenHash
         select new TokenWithAccount { Token = token, Account = account, Person = person, Organization = organization })
        .AsNoTracking()
        .SingleOrDefaultAsync(cancellationToken);

    /// <summary>沒撤銷、沒過期、帳號沒停用。</summary>
    public static bool IsActive(TokenWithAccount found, DateTimeOffset now) =>
        found.Token.RevokedAt is null && found.Account.DisabledAt is null && (found.Token.ExpiresAt is null || now < found.Token.ExpiresAt);

    /// <summary>記下這次使用的時刻，回傳它是不是還有效；條件寫進更新本身，讀到之後剛被撤銷的不會被當成有效。</summary>
    public static async Task<bool> TouchAsync(IdentityDbContext db, AccessTokenRow token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var touched = await db.AccessTokens
            .Where(t => t.Id == token.Id && t.OrganizationId == token.OrganizationId
                && t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.LastUsedAt, (DateTimeOffset?)now), cancellationToken);
        return touched == 1;
    }

    /// <summary>撤銷一個帳號所有還沒撤銷的權杖。停用與重設密碼用，和作廢工作階段在同一個交易。呼叫端提交之後要清快取。</summary>
    public static Task<int> RevokeAllAsync(
        IdentityDbContext db, Guid organizationId, Guid accountId, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.AccessTokens
            .Where(t => t.OrganizationId == organizationId && t.AccountId == accountId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(t => t.RevokedReason, reason),
                cancellationToken);

    /// <summary>刪掉這個組織裡結束超過 <see cref="EndedRetention"/> 的權杖。</summary>
    public static Task<int> DeleteEndedAsync(IdentityDbContext db, Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - EndedRetention;
        return db.AccessTokens
            .Where(t => t.OrganizationId == organizationId && (t.RevokedAt < cutoff || t.ExpiresAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
