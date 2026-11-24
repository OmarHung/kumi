using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Activities;
using PmTool.Work.Attachments;
using PmTool.Work.Common;
using PmTool.Work.Events;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.History;

internal sealed partial class HistoryImportService
{
    public async Task<Result<HistoryAttachmentResult>> ImportAttachmentAsync(
        Actor actor, string itemKey, HistoryAttachment upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(upload);
        if (CheckAttachmentRequest(upload) is { } invalidRequest)
        {
            return invalidRequest;
        }

        // 第一步（和一般上傳一樣）：不合格或已經匯入過就直接回，不讀內容。
        var precheck = await CheckBeforeWritingAsync(actor, itemKey, upload, cancellationToken);
        if (precheck.Failure is { } rejected)
        {
            return rejected;
        }

        if (precheck.Value is { } existing)
        {
            return existing;
        }

        // 第二步：寫進儲存空間。這時還沒有資料列，失敗或超過上限都只要清掉檔案。
        var name = AttachmentNames.CheckFileName(upload.FileName).Value;
        var contentType = AttachmentNames.NormalizeContentType(upload.ContentType);
        var id = WorkClock.NewId(time);
        var key = id.ToString("D");
        var stored = await store.WriteAsync(key, upload.Content, contentType, AttachmentRules.MaxBytes, cancellationToken);
        if (stored is null)
        {
            return WorkFailures.AttachmentTooLarge();
        }

        // 第三步：交易裡重新確認並寫資料列；沒有提交成功（失敗、例外、取消，或同時有人先匯入了同一個）就把剛寫的檔案刪掉。
        var committed = false;
        try
        {
            var recorded = await RecordAttachmentAsync(actor, itemKey, upload, id, name, contentType, stored, cancellationToken);
            committed = recorded.Succeeded && !recorded.Value.Existing;
            return recorded;
        }
        finally
        {
            if (!committed)
            {
                await DeleteFileQuietlyAsync(key);
            }
        }
    }

    private static Failure? CheckAttachmentRequest(HistoryAttachment upload)
    {
        if (HistoryRules.CheckSource(upload.Source) is { } source)
        {
            return source;
        }

        return HistoryRules.ExternalIdProblem(upload.ExternalId) is { } problem
            ? WorkFailures.Invalid(WorkErrorCodes.HistoryInvalid, "externalId", problem)
            : null;
    }

    /// <summary>
    /// 權限、封存、已經匯入過、上傳的人、時間、檔名、數量。回 null 表示可以開始寫；
    /// 已經匯入過的回那一個（要看得到它所在的項目；不在這個專案時不回，免得從這裡讀到別的專案的東西）。
    /// </summary>
    private async Task<Result<HistoryAttachmentResult?>> CheckBeforeWritingAsync(
        Actor actor, string itemKey, HistoryAttachment upload, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await FindManageableItemAsync(db, actor, itemKey, forUpdate: false, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, _) = target.Value;
        var existing = await FindExistingAsync(db, actor, upload, access, cancellationToken);
        if (existing.Failure is not null || existing.Value is not null)
        {
            return existing;
        }

        if (await ProblemsOfUploadAsync(db, access, upload, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        return await AttachmentService.CountAsync(db, actor, item.Id, cancellationToken) >= AttachmentRules.MaxPerItem
            ? WorkFailures.AttachmentLimitReached(ItemKeys.Format(access.Project.Key, item.Number))
            : (HistoryAttachmentResult?)null;
    }

    private async Task<Result<HistoryAttachmentResult>> RecordAttachmentAsync(
        Actor actor,
        string itemKey,
        HistoryAttachment upload,
        Guid id,
        string fileName,
        string contentType,
        StoredFile stored,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HistoryRefs.SerializeAsync(db, actor.OrganizationId, upload.Source, cancellationToken);

        // 鎖住項目那一列：同時上傳的人在這裡排隊，數量的檢查才不會同時過關而超過 100 個。
        var target = await FindManageableItemAsync(db, actor, itemKey, forUpdate: true, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, _) = target.Value;

        // 寫檔的這段時間裡，同一個外部編號可能已經被另一個請求匯入了：回那一個，這一份檔案由呼叫端清掉。
        var existing = await FindExistingAsync(db, actor, upload, access, cancellationToken);
        if (existing.Failure is { } conflict)
        {
            return conflict;
        }

        if (existing.Value is { } already)
        {
            return already;
        }

        var key = ItemKeys.Format(access.Project.Key, item.Number);
        if (await AttachmentService.CountAsync(db, actor, item.Id, cancellationToken) >= AttachmentRules.MaxPerItem)
        {
            return WorkFailures.AttachmentLimitReached(key);
        }

        var at = HistoryRules.Normalize(upload.UploadedAt);
        var uploader = actor with { PersonId = upload.UploadedBy };
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
            UploadedBy = upload.UploadedBy,
            UploadedAt = at,
        };
        db.Attachments.Add(row);
        var activity = new HistoryActivityIds().Assign(ActivityRecords.AttachmentAdded(uploader, item, at, fileName, upload.Source));
        db.ItemActivities.Add(activity);
        db.Events.Add(ItemEvents.AttachmentAdded(uploader, access, item, at, activity.Id, history: true));
        db.HistoryRefs.Add(HistoryRefs.New(
            actor, upload.Source, HistoryRefKind.Attachment, upload.ExternalId, access.Project.Id, item.Id, id, WorkClock.Now(time)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HistoryAttachmentResult(AttachmentLookup.ToContract(row, key), Existing: false);
    }

    /// <summary>要寫的項目：看得到、有 project.manage、專案沒封存。</summary>
    private async Task<Result<ItemTarget>> FindManageableItemAsync(
        WorkDbContext db, Actor actor, string itemKey, bool forUpdate, CancellationToken cancellationToken)
    {
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ProjectManage, forUpdate, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        return target.Value.Access.IsArchived ? WorkFailures.ProjectArchived(target.Value.Access.Project.Name) : target;
    }

    /// <summary>之前匯入過的同一個外部編號。在別的專案的當成這個專案不能用的編號（問題），不把別的專案的附件交出去。</summary>
    private async Task<Result<HistoryAttachmentResult?>> FindExistingAsync(
        WorkDbContext db, Actor actor, HistoryAttachment upload, ProjectAccess access, CancellationToken cancellationToken)
    {
        var found = await HistoryRefs.FindAsync(
            db, actor.OrganizationId, upload.Source, HistoryRefKind.Attachment, [upload.ExternalId], cancellationToken);
        if (!found.TryGetValue(upload.ExternalId, out var reference))
        {
            return (HistoryAttachmentResult?)null;
        }

        if (reference.ProjectId != access.Project.Id)
        {
            // 看不到的專案不透露它的專案代號。
            var readable = await ReadableProjects.IdsAsync(db, cache, actor, cancellationToken);
            var where = readable.Contains(reference.ProjectId) ? $"（{reference.ProjectKey}）" : string.Empty;
            return WorkFailures.Invalid(
                WorkErrorCodes.HistoryInvalid, "externalId", $"外部編號{TextRules.Quote(upload.ExternalId)}已經匯入到別的專案{where}。");
        }

        var attachment = await db.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.OrganizationId == actor.OrganizationId && a.Id == reference.TargetId, cancellationToken);
        return attachment is null
            ? WorkFailures.AttachmentNotFound()
            : new HistoryAttachmentResult(AttachmentLookup.ToContract(attachment, reference.ItemKey), Existing: true);
    }

    /// <summary>上傳的人、時間與檔名；檔名用一般上傳的規則。</summary>
    private async Task<Failure?> ProblemsOfUploadAsync(
        WorkDbContext db, ProjectAccess access, HistoryAttachment upload, CancellationToken cancellationToken)
    {
        var at = HistoryRules.Normalize(upload.UploadedAt);
        var details = new List<FailureDetail>();
        if (!(await HistoryRefs.MembersAmongAsync(db, access, [upload.UploadedBy], cancellationToken)).Contains(upload.UploadedBy))
        {
            details.Add(new FailureDetail(WorkErrorCodes.HistoryInvalid, "uploadedBy", "上傳的人要是這個專案的成員，請先把這個人加進專案。"));
        }

        if (HistoryRules.TimeProblem(at, time) is { } timeProblem)
        {
            details.Add(new FailureDetail(WorkErrorCodes.HistoryInvalid, "uploadedAt", timeProblem));
        }

        if (AttachmentNames.CheckFileName(upload.FileName).Failure is { } badName)
        {
            return badName;
        }

        return details.Count == 0 ? null : WorkFailures.Invalid(details);
    }

    /// <summary>盡力而為：取消的請求也要清掉，所以不用呼叫端的取消權杖；刪不掉只留下沒人參考的檔案。</summary>
    private async Task DeleteFileQuietlyAsync(string key)
    {
        try
        {
            await store.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogDeleteFailed(logger, exception, key);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "歷史匯入的附件檔案 {Key} 沒有從儲存空間刪掉，留下一個沒人參考的檔案（佔空間，不影響資料）。")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string key);
}
