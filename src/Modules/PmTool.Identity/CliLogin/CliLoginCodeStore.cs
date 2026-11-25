using Microsoft.EntityFrameworkCore;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.CliLogin;

/// <summary>登入碼的寫入：改密碼、重設密碼、停用帳號讓還沒兌換的碼失效，三個地方共用同一句。</summary>
internal static class CliLoginCodeStore
{
    /// <summary>
    /// 把這個帳號還沒發出權杖的登入碼全部作廢：沒用過的標成用過；已經被燒掉、正等著帳號的鎖的兌換也一併擋下，
    /// 方法是把期限改成最早的時刻，兌換拿到鎖之後會重讀期限。已經發出權杖的碼不動，重複兌換時才認得出來。
    /// 要在同一個交易裡、改過帳號那一列之後呼叫（先帳號、再碼）。
    /// </summary>
    public static Task<int> InvalidateForAccountAsync(
        IdentityDbContext db, Guid organizationId, Guid accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            UPDATE identity.cli_login_codes
            SET used_at = COALESCE(used_at, {now}), expires_at = 'epoch'::timestamptz
            WHERE organization_id = {organizationId} AND account_id = {accountId}
              AND cardinality(issued_token_ids) = 0 AND (used_at IS NULL OR expires_at > 'epoch'::timestamptz)
            """,
            cancellationToken);
}
