using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Maintenance;

/// <summary>
/// 對話的保存期限（AI-06）：<see cref="AiRules.ConversationRetention"/> 沒有更新的對話整則刪掉（訊息由外鍵連帶刪除）。
/// 用量表（<c>connection_usage</c>）不動，刪對話不會讓配額歸零。分批刪，一次不鎖住一大片列；正在回覆的對話留著，下一輪再處理。
/// </summary>
internal static class ConversationRetention
{
    public const int DefaultBatchSize = 500;

    /// <returns>刪掉幾則對話。</returns>
    public static async Task<int> SweepAsync(
        AiDbContext db, DateTimeOffset now, CancellationToken cancellationToken, int batchSize = DefaultBatchSize)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        var cutoff = now - AiRules.ConversationRetention;
        var total = 0;
        while (true)
        {
            var batch = await db.Conversations
                .AsNoTracking()
                .Where(c => c.UpdatedAt < cutoff && (c.RunningUntil == null || c.RunningUntil < now))
                .OrderBy(c => c.UpdatedAt)
                .Select(c => c.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return total;
            }

            // 條件再寫一次：挑出來之後才開始回覆的對話不能刪。
            var deleted = await db.Conversations
                .Where(c => batch.Contains(c.Id) && c.UpdatedAt < cutoff && (c.RunningUntil == null || c.RunningUntil < now))
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
            if (deleted == 0 || batch.Count < batchSize)
            {
                return total;
            }
        }
    }
}
