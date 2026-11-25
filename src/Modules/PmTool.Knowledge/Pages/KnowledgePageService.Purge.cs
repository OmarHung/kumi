using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Pages;

internal sealed partial class KnowledgePageService
{
    /// <summary>
    /// 永久刪除垃圾桶裡的批：知識庫唯一真的刪資料的操作，不能復原。順序是
    /// 找得到空間（404）→ 管理權限（403）→ 空間唯讀（409）→ 在空間的鎖裡算到期時刻、選出要刪的批（量有上限）、檢查不變式
    /// → 先讀出附件編號（資料列會被 CASCADE 帶走）→ 一個陳述式刪頁面 → 提交 → 提交之後才刪附件的檔案。
    /// </summary>
    public async Task<Result<TrashPurge>> PurgeTrashAsync(
        Actor actor, string spaceKey, TrashPurgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        if ((space.CheckManage() ?? (space.IsReadOnly ? KnowledgeFailures.SpaceArchived(space.Space.Name) : null)) is { } denied)
        {
            return denied;
        }

        var spaceId = space.Space.Id;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await KnowledgeLocks.SpaceAsync(db, spaceId, cancellationToken);

        // 到期的時刻在鎖裡算一次：等鎖的時候過了幾分鐘，也不會刪到剛好跨過 30 天的另一批。
        var cutoff = clock.GetUtcNow() - KnowledgeRules.TrashPurgeAge;
        var chosen = await ChooseBatchesAsync(db, actor.OrganizationId, spaceId, request, cutoff, cancellationToken);
        if (chosen.Count == 0 && request.DeletionId is not null)
        {
            return new Failure(FailureKind.NotFound, KnowledgeErrorCodes.PageNotFound, "垃圾桶裡沒有這一批。");
        }

        var doomed = await DropBatchesThatWouldOrphanAsync(db, actor.OrganizationId, spaceId, chosen, cancellationToken);
        var pageIds = doomed.Count == 0
            ? []
            : await db.Pages.AsNoTracking()
                .Where(p => p.OrganizationId == actor.OrganizationId && p.SpaceId == spaceId && p.DeletionId != null && doomed.Contains(p.DeletionId.Value))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

        // 先讀附件編號：page_attachments 的資料列會隨頁面一起被 CASCADE 刪掉，刪了就不知道要清哪些檔案。只有知識庫自己的附件。
        var attachmentIds = pageIds.Count == 0
            ? []
            : await db.PageAttachments.AsNoTracking()
                .Where(a => a.OrganizationId == actor.OrganizationId && pageIds.Contains(a.PageId))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);
        if (pageIds.Count > 0)
        {
            await db.Pages.Where(p => p.OrganizationId == actor.OrganizationId && pageIds.Contains(p.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        var remaining = await db.Pages.AsNoTracking()
            .Where(p => p.OrganizationId == actor.OrganizationId && p.SpaceId == spaceId && p.DeletionId != null)
            .Select(p => p.DeletionId)
            .Distinct()
            .CountAsync(cancellationToken);

        // 下一次「清到期的」會不會清到東西：用同一套選批與不變式，所以「還有更多」不會是清不掉的批。
        var nextChosen = await ChooseExpiredAsync(db, actor.OrganizationId, spaceId, cutoff, cancellationToken);
        var hasMoreExpired = (await DropBatchesThatWouldOrphanAsync(db, actor.OrganizationId, spaceId, nextChosen, cancellationToken)).Count > 0;
        await transaction.CommitAsync(cancellationToken);

        await DeleteAttachmentFilesAsync(attachmentIds);
        if (doomed.Count > 0)
        {
            LogPurged(logger, actor.PersonId, space.Space.Key, doomed.Count, pageIds.Count, attachmentIds.Count);
        }

        return new TrashPurge(doomed.Count, pageIds.Count, attachmentIds.Count, remaining, hasMoreExpired);
    }

    /// <summary>要清的批：指定一批時是那一批（要在垃圾桶裡）；否則是到期的（<see cref="ChooseExpiredAsync"/>）。</summary>
    private async Task<IReadOnlyList<Guid>> ChooseBatchesAsync(
        KnowledgeDbContext db, Guid organizationId, Guid spaceId, TrashPurgeRequest request, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        if (request.DeletionId is not { } only)
        {
            return await ChooseExpiredAsync(db, organizationId, spaceId, cutoff, cancellationToken);
        }

        return await db.Pages.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.SpaceId == spaceId && p.DeletionId == only)
            .Select(p => p.DeletionId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 到期的批，最舊的在前：最多 <see cref="KnowledgeLimits.MaxBatchesPerPurge"/> 批，累計的頁數超過 <see cref="KnowledgeLimits.MaxPagesPerPurge"/> 就停，
    /// 但至少一批（一批不能拆，單獨超過上限時照樣整批清）。同一批的頁面刪除時間相同。
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ChooseExpiredAsync(
        KnowledgeDbContext db, Guid organizationId, Guid spaceId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var candidates = await db.Pages.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.SpaceId == spaceId && p.DeletionId != null && p.DeletedAt <= cutoff)
            .GroupBy(p => p.DeletionId!.Value)
            .Select(g => new { Id = g.Key, At = g.Min(p => p.DeletedAt), Pages = g.Count() })
            .OrderBy(g => g.At)
            .ThenBy(g => g.Id)
            .Take(limits.MaxBatchesPerPurge)
            .ToListAsync(cancellationToken);
        var chosen = new List<Guid>();
        var pages = 0;
        foreach (var candidate in candidates)
        {
            if (chosen.Count > 0 && pages + candidate.Pages > limits.MaxPagesPerPurge)
            {
                break;
            }

            chosen.Add(candidate.Id);
            pages += candidate.Pages;
        }

        return chosen;
    }

    /// <summary>
    /// 不變式：要刪的頁面集合必須往下封閉，也就是還留著的頁面（活的，或垃圾桶裡不刪的）的 parent_id 都不能指到要刪的頁面。
    /// 正常的樹一定成立：刪除時活的後代都在同一批、較早刪的子頁時間更早會先到期、復原時上層不在就放最上層。
    /// 萬一不成立，刪了的話留下來的頁面復原時就沿不到原本的上層鏈，繼承的限制會掉：那幾批這次不刪並記警告。
    /// 把一批拿掉之後，它自己的頁面又變成「留著」的，所以重複到沒有新的不合格為止。
    /// </summary>
    private async Task<IReadOnlyList<Guid>> DropBatchesThatWouldOrphanAsync(
        KnowledgeDbContext db, Guid organizationId, Guid spaceId, IReadOnlyList<Guid> chosen, CancellationToken cancellationToken)
    {
        var keep = chosen.ToList();
        while (keep.Count > 0)
        {
            var current = keep;
            var offendingBatches = await db.Pages.AsNoTracking()
                .Where(parent => parent.OrganizationId == organizationId && parent.SpaceId == spaceId
                    && parent.DeletionId != null && current.Contains(parent.DeletionId.Value))
                .Where(parent => db.Pages.Any(child => child.OrganizationId == organizationId && child.ParentId == parent.Id
                    && (child.DeletionId == null || !current.Contains(child.DeletionId.Value))))
                .Select(parent => parent.DeletionId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (offendingBatches.Count == 0)
            {
                break;
            }

            foreach (var batch in offendingBatches)
            {
                LogSkippedBatch(logger, spaceId, batch);
            }

            keep = [.. keep.Except(offendingBatches)];
        }

        return keep;
    }

    /// <summary>提交之後才刪檔案：盡力而為，失敗只留下一個沒人參考的檔案（佔空間、不影響資料），和項目的附件一樣。</summary>
    private async Task DeleteAttachmentFilesAsync(IReadOnlyList<Guid> attachmentIds)
    {
        foreach (var id in attachmentIds)
        {
            try
            {
                await files.DeleteAsync(id.ToString("D"), CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogFileDeleteFailed(logger, exception, id);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "知識庫永久刪除：人員 {PersonId} 在空間 {SpaceKey} 刪了 {Batches} 批、{Pages} 頁、{Attachments} 個附件。")]
    private static partial void LogPurged(ILogger logger, Guid personId, string spaceKey, int batches, int pages, int attachments);

    [LoggerMessage(Level = LogLevel.Warning, Message = "空間 {SpaceId} 垃圾桶的第 {DeletionId} 批底下還有留著的頁面指到它，這次沒有永久刪除。")]
    private static partial void LogSkippedBatch(ILogger logger, Guid spaceId, Guid deletionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "頁面附件 {AttachmentId} 的檔案沒有從儲存空間刪掉，留下一個沒人參考的檔案（佔空間，不影響資料）。")]
    private static partial void LogFileDeleteFailed(ILogger logger, Exception exception, Guid attachmentId);
}
