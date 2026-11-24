using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PmTool.Work.Persistence;

namespace PmTool.Work.Access;

/// <summary>
/// 項目的寫入與藍圖的套用靠專案那一列互斥。套用拿 FOR UPDATE；其他會讀藍圖再寫入的操作（新增、修改、改狀態、改類型、刪除、留言、
/// 設定與移除成員、送出變更集），在讀藍圖之前拿 FOR KEY SHARE。這樣套用會等進行中的寫入做完才開始，
/// 套用期間進來的寫入會等它提交，之後讀到的是新的藍圖。共用鎖彼此不衝突，一般的寫入不會互相等。
/// 只在交易裡有意義：鎖一直留到交易結束。
/// </summary>
internal static class ProjectLocks
{
    /// <summary>不合格式的代號沒有對應的列，什麼都不做；呼叫端接著讀專案時會得到「找不到」。</summary>
    public static async Task ShareAsync(WorkDbContext db, Guid organizationId, string? projectKey, CancellationToken cancellationToken)
    {
        if (ProjectKeys.TryNormalize(projectKey, out var key))
        {
            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM work.projects WHERE organization_id = {organizationId} AND key = {key} FOR KEY SHARE",
                cancellationToken);
        }
    }

    /// <summary>套用變更集用：擋下所有會讀藍圖的寫入，直到交易結束。</summary>
    public static async Task ExclusiveAsync(WorkDbContext db, Guid organizationId, string? projectKey, CancellationToken cancellationToken)
    {
        if (ProjectKeys.TryNormalize(projectKey, out var key))
        {
            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM work.projects WHERE organization_id = {organizationId} AND key = {key} FOR UPDATE",
                cancellationToken);
        }
    }

    /// <summary>
    /// 之後在這個交易裡等鎖最多等 <paramref name="timeout"/>，等不到資料庫丟出鎖逾時（<see cref="PostgresErrors.IsLockTimeout"/>）。
    /// 排他鎖擋不住之後才來的共用鎖，專案一直有人在寫的話會一直排不到；只在這個交易裡有效，連線還回連線池之後不影響別的請求。
    /// </summary>
    public static Task LimitWaitAsync(WorkDbContext db, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var milliseconds = ((long)timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        return db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {milliseconds}, true)", cancellationToken);
    }
}
