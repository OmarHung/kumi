using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Storage;
using PmTool.Work.Activities;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Events;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Attachments;

internal sealed partial class AttachmentService(
    IDbContextFactory<WorkDbContext> contexts,
    BlueprintCache cache,
    IFileStore store,
    IStorageQuota quota,
    TimeProvider time,
    ILogger<AttachmentService> logger) : IAttachmentService
{
    public async Task<Result<Attachment>> AddAsync(Actor actor, string itemKey, NewAttachment upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(upload);

        // 第一步：不合格就直接回失敗，不讀內容（內容可能有 25 MiB，擋得掉的不要讓它白白送進來）。
        var name = AttachmentNames.CheckFileName(upload.FileName);
        var contentType = AttachmentNames.NormalizeContentType(upload.ContentType);
        var precheck = await CheckBeforeWritingAsync(actor, itemKey, name, cancellationToken);
        if (precheck is { } rejected)
        {
            return rejected;
        }

        // 第二步：寫進儲存空間。這時還沒有資料列，失敗或超過上限都只要清掉檔案。
        var id = WorkClock.NewId(time);
        var key = id.ToString("D");
        var stored = await store.WriteAsync(key, upload.Content, contentType, AttachmentRules.MaxBytes, cancellationToken);
        if (stored is null)
        {
            return WorkFailures.AttachmentTooLarge();
        }

        // 第三步：交易裡重新確認並寫資料列。權限與數量在寫檔的這段時間裡可能變了（被移出專案、別人也在上傳）。
        // 沒有提交成功（失敗、例外、取消）就把剛寫的檔案刪掉，不留沒人參考的檔案。
        var committed = false;
        try
        {
            var added = await RecordAsync(actor, itemKey, id, name.Value, contentType, stored, cancellationToken);
            committed = added.Succeeded;
            return added;
        }
        finally
        {
            if (!committed)
            {
                await DeleteFileQuietlyAsync(key, id);
            }
        }
    }

    public async Task<Result<IReadOnlyList<Attachment>>> ListAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemRead, forUpdate: false, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var item = target.Value.Item;
        var key = ItemKeys.Format(target.Value.Access.Project.Key, item.Number);
        var rows = await db.Attachments
            .AsNoTracking()
            .Where(a => a.OrganizationId == actor.OrganizationId && a.ItemId == item.Id)
            .OrderBy(a => a.UploadedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(row => AttachmentLookup.ToContract(row, key)).ToList();
    }

    public async Task<Result<AttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = await AttachmentLookup.FindAsync(db, cache, actor, attachmentId, WorkPermissions.ItemRead, forUpdate: false, cancellationToken);
        if (found.Failure is { } failure)
        {
            return failure;
        }

        var (row, target) = found.Value;
        var content = await store.OpenReadAsync(attachmentId.ToString("D"), cancellationToken);
        if (content is null)
        {
            // 資料庫有資料列、儲存空間沒有檔案：備份沒還原到位、或有人手動刪了檔案。使用者能做的只有回報，所以記錯誤。
            LogMissingFile(logger, attachmentId, row.FileName);
            return WorkFailures.AttachmentNotFound();
        }

        var itemKey = ItemKeys.Format(target.Access.Project.Key, target.Item.Number);
        return new AttachmentContent(AttachmentLookup.ToContract(row, itemKey), content);
    }

    public async Task<Result<Attachment>> DeleteAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 和上傳一樣先鎖項目那一列：同一個項目的上傳與刪除排隊，活動的順序就是真正發生的順序。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var found = await AttachmentLookup.FindAsync(db, cache, actor, attachmentId, WorkPermissions.ItemUpdate, forUpdate: true, cancellationToken);
        if (found.Failure is { } failure)
        {
            return failure;
        }

        var (row, target) = found.Value;
        var removed = await db.Attachments
            .Where(a => a.OrganizationId == actor.OrganizationId && a.Id == attachmentId)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            return WorkFailures.AttachmentNotFound();
        }

        var now = WorkClock.Now(time);
        var activity = ActivityRecords.AttachmentRemoved(actor, target.Item, now, row.FileName);
        db.ItemActivities.Add(activity);
        db.Events.Add(ItemEvents.AttachmentRemoved(actor, target.Access, target.Item, now, activity.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 提交之後才刪檔案：先刪檔案、交易卻失敗的話，資料列就指著一個不存在的檔案。
        // 刪不掉只留下沒人參考的檔案（佔空間，不會遺失資料），不讓使用者的刪除失敗。
        await DeleteFileQuietlyAsync(attachmentId.ToString("D"), attachmentId);
        return AttachmentLookup.ToContract(row, ItemKeys.Format(target.Access.Project.Key, target.Item.Number));
    }

    /// <summary>權限與數量。回 null 表示可以開始寫。</summary>
    private async Task<Failure?> CheckBeforeWritingAsync(Actor actor, string itemKey, Result<string> name, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemUpdate, forUpdate: false, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        if (name.Failure is { } invalidName)
        {
            return invalidName;
        }

        if (await CountAsync(db, actor, target.Value.Item.Id, cancellationToken) >= AttachmentRules.MaxPerItem)
        {
            return WorkFailures.AttachmentLimitReached(ItemKeys.Format(target.Value.Access.Project.Key, target.Value.Item.Number));
        }

        // 已經用滿就不必讀內容；沒滿的話寫完之後還會在交易裡用實際的大小再確認一次。
        var room = await quota.CheckAsync(actor.OrganizationId, actor.PersonId, incomingBytes: 0, cancellationToken);
        return room.IsAllowed ? null : WorkFailures.AttachmentQuota(room);
    }

    private async Task<Result<Attachment>> RecordAsync(
        Actor actor, string itemKey, Guid id, string fileName, string contentType, StoredFile stored, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 鎖住項目那一列：同時上傳的人在這裡排隊，數量的檢查才不會同時過關而超過 100 個。
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemUpdate, forUpdate: true, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, _) = target.Value;
        var key = ItemKeys.Format(access.Project.Key, item.Number);
        if (await CountAsync(db, actor, item.Id, cancellationToken) >= AttachmentRules.MaxPerItem)
        {
            return WorkFailures.AttachmentLimitReached(key);
        }

        // 儲存空間的鎖是最後一把：和知識庫的上傳排在同一個隊伍，加總到的是已經提交的量。
        await StorageLocks.OrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var room = await quota.CheckAsync(actor.OrganizationId, actor.PersonId, stored.SizeBytes, cancellationToken);
        if (!room.IsAllowed)
        {
            return WorkFailures.AttachmentQuota(room);
        }

        var now = WorkClock.Now(time);
        var row = new AttachmentEntity
        {
            Id = id,
            OrganizationId = actor.OrganizationId,
            ProjectId = access.Project.Id,
            ItemId = item.Id,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            UploadedBy = actor.PersonId,
            UploadedAt = now,
        };
        db.Attachments.Add(row);
        var activity = ActivityRecords.AttachmentAdded(actor, item, now, fileName);
        db.ItemActivities.Add(activity);
        db.Events.Add(ItemEvents.AttachmentAdded(actor, access, item, now, activity.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AttachmentLookup.ToContract(row, key);
    }

    internal static Task<int> CountAsync(WorkDbContext db, Actor actor, Guid itemId, CancellationToken cancellationToken) =>
        db.Attachments.CountAsync(a => a.OrganizationId == actor.OrganizationId && a.ItemId == itemId, cancellationToken);

    /// <summary>盡力而為：取消的請求也要清掉，所以不用呼叫端的取消權杖；失敗只記 Warning，留下的檔案不會造成錯。</summary>
    private async Task DeleteFileQuietlyAsync(string key, Guid attachmentId)
    {
        try
        {
            await store.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogDeleteFailed(logger, exception, attachmentId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "附件 {AttachmentId}（{FileName}）在資料庫裡有紀錄，儲存空間裡卻找不到檔案；請從備份還原。")]
    private static partial void LogMissingFile(ILogger logger, Guid attachmentId, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "附件 {AttachmentId} 的檔案沒有從儲存空間刪掉，留下一個沒人參考的檔案（佔空間，不影響資料）。")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, Guid attachmentId);
}
