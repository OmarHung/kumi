using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Ai.Attachments;
using PmTool.Ai.Persistence;
using PmTool.Storage;

namespace PmTool.Ai.Maintenance;

/// <summary>
/// 助理附件的清理（internal/HANDOFF-ai-attachments.md 第 4 節）：孤兒（對話刪掉了，訊息不在，資料列還留著）與過了
/// <see cref="AssistantAttachmentRules.PendingLifetime"/> 還沒送出的待送。先刪檔案再刪資料列：
/// 資料列是容量的依據，檔案還在的時候不能先把容量放掉；刪檔失敗只記 Warning 並記下時間（<c>SweepFailedAt</c>），那一筆排到最後、下一輪再試。
/// </summary>
internal static class AttachmentRetention
{
    public const int DefaultBatchSize = 500;

    /// <summary>
    /// 清理待送的多給的寬限：送出的檢查用的是自己程序的時鐘，這裡用另一個程序的時鐘，差一點點的時候兩邊不會同時認為「可以綁」與「可以刪」。
    /// </summary>
    public static readonly TimeSpan PendingGrace = TimeSpan.FromMinutes(5);

    /// <returns>刪掉的附件數（檔案與資料列都刪掉了的）。</returns>
    public static async Task<int> SweepAsync(
        AiDbContext db, IFileStore store, DateTimeOffset now, ILogger logger, CancellationToken cancellationToken, int batchSize = DefaultBatchSize)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        var stale = now - AssistantAttachmentRules.PendingLifetime - PendingGrace;
        var batch = await db.Attachments
            .AsNoTracking()
            .Where(a => (a.BoundAt != null && a.MessageId == null) || (a.BoundAt == null && a.UploadedAt < stale))
            .OrderBy(a => a.SweepFailedAt != null)
            .ThenBy(a => a.SweepFailedAt)
            .ThenBy(a => a.UploadedAt)
            .Select(a => a.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var removed = new List<Guid>(batch.Count);
        var failed = new List<Guid>();
        foreach (var id in batch)
        {
            (await TryDeleteFileAsync(store, id, logger, cancellationToken) ? removed : failed).Add(id);
        }

        // 刪不掉的記下時間：每批只取一部分、依這個時間排序，一直刪不掉的排到最後，不會永遠佔著前面的位置、卡住後面刪得掉的。
        if (failed.Count > 0)
        {
            await db.Attachments
                .Where(a => failed.Contains(a.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.SweepFailedAt, now), cancellationToken);
        }

        // 條件再寫一次：挑出來之後才被送出的待送不能刪（送出的 UPDATE 只綁得到沒過期的，過了寬限的不會被綁）。
        return removed.Count == 0
            ? 0
            : await db.Attachments
                .Where(a => removed.Contains(a.Id)
                    && ((a.BoundAt != null && a.MessageId == null) || (a.BoundAt == null && a.UploadedAt < stale)))
                .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<bool> TryDeleteFileAsync(IFileStore store, Guid id, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await store.DeleteAsync(AttachmentMapper.KeyOf(id), cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            logger.LogWarning(exception, "助理附件 {AttachmentId} 的檔案刪不掉，下一輪再試。", id);
            return false;
        }
    }
}
