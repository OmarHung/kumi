using Microsoft.EntityFrameworkCore;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Sessions;

/// <summary>一個工作階段，加上它的帳號現在的資料。</summary>
internal sealed record SessionWithAccount
{
    public required SessionRow Session { get; init; }

    public required AccountRow Account { get; init; }

    public required PersonRow Person { get; init; }

    public required OrganizationRow Organization { get; init; }

    public AuthenticatedUser User => new(
        Account.Id, Person.Id, Organization.Id, Organization.Name, Person.DisplayName, Account.Email, Person.Role);
}

/// <summary>工作階段的查詢與作廢。查工作階段、登出其他裝置、改密碼、停用帳號都用這一份，「還有效」的定義才只有一個。</summary>
internal static class SessionStore
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromHours(IdentityRules.SessionIdleHours);

    public static readonly TimeSpan AbsoluteLimit = TimeSpan.FromDays(IdentityRules.SessionAbsoluteDays);

    /// <summary>結束（作廢或過期）超過這麼久的工作階段才刪：查問題時還看得到最近的紀錄，表又不會一直長大。</summary>
    private static readonly TimeSpan EndedRetention = TimeSpan.FromDays(30);

    /// <summary>依權杖的雜湊找。權杖在全部組織裡唯一，查到之前不知道是哪個組織，所以這一個查詢不依組織過濾。</summary>
    public static Task<SessionWithAccount?> FindAsync(IdentityDbContext db, byte[] tokenHash, CancellationToken cancellationToken) =>
        (from session in db.Sessions
         join account in db.Accounts on session.AccountId equals account.Id
         join person in db.People on account.PersonId equals person.Id
         join organization in db.Organizations on person.OrganizationId equals organization.Id
         where session.TokenHash == tokenHash
         select new SessionWithAccount { Session = session, Account = account, Person = person, Organization = organization })
        .AsNoTracking()
        .SingleOrDefaultAsync(cancellationToken);

    /// <summary>沒作廢、沒到絕對期限、沒閒置太久、帳號沒停用。</summary>
    public static bool IsActive(SessionWithAccount found, DateTimeOffset now) =>
        found.Session.RevokedAt is null
        && found.Account.DisabledAt is null
        && now < found.Session.ExpiresAt
        && now - found.Session.LastUsedAt < IdleLimit;

    /// <summary>
    /// 帳號的密碼還是 <paramref name="verifiedPasswordHash"/>、帳號沒停用，才插入這一筆，回傳有沒有插入。
    /// 條件和插入是同一個陳述式，並對帳號那一列加共享鎖：重設密碼、改密碼、停用都先改帳號那一列再作廢工作階段，
    /// 所以兩邊同時進行時，不是這裡等它們提交後看到新的密碼（不插入），就是它們等這裡提交後連這一筆一起作廢。
    /// </summary>
    public static async Task<bool> InsertForVerifiedAsync(
        IdentityDbContext db, SessionRow row, string verifiedPasswordHash, CancellationToken cancellationToken)
    {
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO identity.sessions (id, organization_id, account_id, token_hash, created_at, last_used_at, expires_at)
            SELECT {row.Id}, a.organization_id, a.id, {row.TokenHash}, {row.CreatedAt}, {row.LastUsedAt}, {row.ExpiresAt}
            FROM identity.accounts AS a
            WHERE a.id = {row.AccountId} AND a.organization_id = {row.OrganizationId}
              AND a.password_hash = {verifiedPasswordHash} AND a.disabled_at IS NULL
            FOR SHARE
            """,
            cancellationToken);
        return inserted == 1;
    }

    /// <summary>
    /// 記下這次使用的時刻，回傳它是不是還有效。條件寫進更新本身：讀到「還有效」之後、更新之前它可能剛被作廢，
    /// 或剛好跨過閒置期限，這時不能把它續命，要當作無效。
    /// </summary>
    public static async Task<bool> TouchAsync(IdentityDbContext db, SessionRow session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var idleSince = now - IdleLimit;
        var touched = await db.Sessions
            .Where(s => s.Id == session.Id && s.OrganizationId == session.OrganizationId
                && s.RevokedAt == null && s.LastUsedAt > idleSince && s.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastUsedAt, now), cancellationToken);
        return touched == 1;
    }

    /// <summary>
    /// 作廢一個帳號所有還沒作廢的工作階段（<paramref name="keep"/> 那一個除外），回傳其中還有效的有幾個。
    /// 已經閒置或過期的也一起作廢：只作廢還有效的話，剛好在閒置邊界上、正被另一個查詢續命的那一個會漏掉。
    /// 數字只算還有效的，否則「登出了幾個裝置」會把很久以前的登入也算進來。作廢與計數是同一個陳述式。
    /// 呼叫端在交易提交之後要叫 <see cref="SessionCache.Forget"/>。
    /// </summary>
    public static async Task<int> RevokeAsync(
        IdentityDbContext db, Guid organizationId, Guid accountId, Guid? keep, string reason, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var keepId = keep ?? Guid.Empty;
        var idleSince = now - IdleLimit;
        var counts = await db.Database.SqlQuery<int>(
            $"""
            WITH revoked AS (
                UPDATE identity.sessions SET revoked_at = {now}, revoked_reason = {reason}
                WHERE organization_id = {organizationId} AND account_id = {accountId} AND id <> {keepId} AND revoked_at IS NULL
                RETURNING expires_at, last_used_at)
            SELECT count(*)::int AS "Value" FROM revoked WHERE expires_at > {now} AND last_used_at > {idleSince}
            """)
            .ToListAsync(cancellationToken);
        return counts.Single();
    }

    /// <summary>
    /// 作廢一個工作階段（登出），回傳它屬於哪個帳號；查無此權杖時是 null。
    /// 依權杖找、不依組織過濾：權杖在全部組織裡唯一，登出時只有它，還不知道是哪個組織；
    /// 找到之後的更新就照規矩帶上組織。
    /// </summary>
    public static async Task<Guid?> RevokeOneAsync(
        IdentityDbContext db, byte[] tokenHash, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var session = await db.Sessions
            .Where(s => s.TokenHash == tokenHash)
            .Select(s => new { s.Id, s.OrganizationId, s.AccountId })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return null;
        }

        await db.Sessions
            .Where(s => s.Id == session.Id && s.OrganizationId == session.OrganizationId && s.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(s => s.RevokedReason, reason),
                cancellationToken);
        return session.AccountId;
    }

    /// <summary>刪掉這個組織裡結束超過 <see cref="EndedRetention"/> 的工作階段。閒置失效的，結束的時刻是最後使用加上閒置期限。</summary>
    public static Task<int> DeleteEndedAsync(
        IdentityDbContext db, Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - EndedRetention;
        var idleCutoff = cutoff - IdleLimit;
        return db.Sessions
            .Where(s => s.OrganizationId == organizationId
                && (s.RevokedAt < cutoff || s.ExpiresAt < cutoff || s.LastUsedAt < idleCutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
