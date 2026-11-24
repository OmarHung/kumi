using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;
using PmTool.Storage;

namespace PmTool.Knowledge.Attachments;

/// <summary>
/// 頁面的附件。流程照項目的附件：先檢查權限與數量（不合格就不讀內容）→ 寫進儲存空間、邊寫邊算 →
/// 交易裡重新確認並記下 → 沒記成功就盡量把檔案刪掉。
/// </summary>
internal sealed partial class KnowledgeAttachmentService(
    IDbContextFactory<KnowledgeDbContext> contexts,
    SpaceAccess access,
    IFileStore store,
    TimeProvider clock,
    ILogger<KnowledgeAttachmentService> logger) : IKnowledgeAttachmentService
{
    /// <summary>空間、這一份看得到的頁面範圍，與要處理的那一頁。</summary>
    private sealed record PageTarget(SpaceContext Space, PageInfo Page);

    public async Task<Result<PageAttachment>> AddAsync(
        Actor actor, string spaceKey, int pageNumber, NewPageAttachment upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(upload);

        // 第一步：不合格就直接回失敗，不讀內容（內容可能有 25 MiB，擋得掉的不要讓它白白送進來）。
        var name = AttachmentNames.CheckFileName(upload.FileName);
        var contentType = AttachmentNames.NormalizeContentType(upload.ContentType);
        var precheck = await CheckBeforeWritingAsync(actor, spaceKey, pageNumber, name, cancellationToken);
        if (precheck.Failure is { } rejected)
        {
            return rejected;
        }

        var pageId = precheck.Value;

        // 第二步：寫進儲存空間。這時還沒有資料列，失敗或超過上限都只要清掉檔案。
        var id = Guid.CreateVersion7(clock.GetUtcNow());
        var key = id.ToString("D");
        var stored = await store.WriteAsync(key, upload.Content, contentType, KnowledgeRules.MaxAttachmentBytes, cancellationToken);
        if (stored is null)
        {
            return AttachmentFailures.TooLarge();
        }

        // 第三步：交易裡重新確認並寫資料列。權限與數量在寫檔的這段時間裡可能變了。
        // 沒有提交成功（失敗、例外、取消）就把剛寫的檔案刪掉，不留沒人參考的檔案。
        var committed = false;
        try
        {
            var added = await RecordAsync(actor, spaceKey, pageNumber, pageId, id, name.Value, contentType, stored, cancellationToken);
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

    public async Task<Result<IReadOnlyList<PageAttachment>>> ListAsync(
        Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await FindLiveAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var rows = await db.PageAttachments.AsNoTracking()
            .Where(a => a.OrganizationId == actor.OrganizationId && a.PageId == target.Value.Page.Id)
            .OrderBy(a => a.UploadedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToContract).ToList();
    }

    public async Task<Result<PageAttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.PageAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.OrganizationId == actor.OrganizationId && a.Id == attachmentId, cancellationToken);
        if (row is null || await FindPageOfAsync(db, actor, row, cancellationToken) is null)
        {
            return AttachmentFailures.NotFound();
        }

        var content = await store.OpenReadAsync(attachmentId.ToString("D"), cancellationToken);
        if (content is null)
        {
            // 資料庫有資料列、儲存空間沒有檔案：備份沒還原到位、或有人手動刪了檔案。使用者能做的只有回報，所以記錯誤。
            LogMissingFile(logger, attachmentId, row.FileName);
            return AttachmentFailures.NotFound();
        }

        return new PageAttachmentContent(ToContract(row), content);
    }

    /// <summary>附件所在的頁面：看得到、不在垃圾桶裡才算；其他一律是 null（當作附件不存在）。</summary>
    private async Task<PageTarget?> FindPageOfAsync(KnowledgeDbContext db, Actor actor, PageAttachmentRow row, CancellationToken cancellationToken)
    {
        var location = await db.Pages.AsNoTracking()
            .Where(p => p.OrganizationId == actor.OrganizationId && p.Id == row.PageId)
            .Join(db.Spaces, p => p.SpaceId, s => s.Id, (p, s) => new { p.Number, Space = s })
            .FirstOrDefaultAsync(cancellationToken);
        if (location is null || await access.ResolveAsync(actor, location.Space, cancellationToken) is not { } space)
        {
            return null;
        }

        var scope = await PageScope.LoadLiveAsync(db, actor, space.Space.Id, cancellationToken);
        return scope.FindVisible(location.Number) is { IsDeleted: false } page ? new PageTarget(space, page) : null;
    }

    private async Task<Result<PageTarget>> FindLiveAsync(
        KnowledgeDbContext db, Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        var scope = await PageScope.LoadLiveAsync(db, actor, space.Space.Id, cancellationToken);
        return scope.FindVisible(pageNumber) is { IsDeleted: false } page
            ? new PageTarget(space, page)
            : KnowledgeFailures.PageNotFound(pageNumber);
    }

    /// <summary>權限（先於檔名）與數量。成功時回傳那一頁的編號（鎖住頁面用），可以開始寫。</summary>
    private async Task<Result<Guid>> CheckBeforeWritingAsync(
        Actor actor, string spaceKey, int pageNumber, Result<string> name, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await FindLiveAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        if (target.Value.Space.CheckEdit() is { } forbidden)
        {
            return forbidden;
        }

        if (name.Failure is { } invalidName)
        {
            return invalidName;
        }

        var pageId = target.Value.Page.Id;
        return await CountAsync(db, actor, pageId, cancellationToken) >= KnowledgeRules.MaxAttachmentsPerPage
            ? AttachmentFailures.LimitReached()
            : pageId;
    }

    private async Task<Result<PageAttachment>> RecordAsync(
        Actor actor,
        string spaceKey,
        int pageNumber,
        Guid pageId,
        Guid id,
        string fileName,
        string contentType,
        StoredFile stored,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 鎖住頁面那一列：同時上傳的人在這裡排隊，數量的檢查才不會同時過關而超過上限。
        // 頁面編號是預檢時找到的，所以直接鎖；鎖完才確認「這一頁還在、還看得到、空間還能寫」，只載入一次範圍。
        await KnowledgeLocks.PageAsync(db, actor.OrganizationId, pageId, cancellationToken);
        var target = await FindLiveAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        if (target.Failure is { } gone)
        {
            return gone;
        }

        if (target.Value.Space.CheckEdit() is { } forbidden)
        {
            return forbidden;
        }

        if (await CountAsync(db, actor, target.Value.Page.Id, cancellationToken) >= KnowledgeRules.MaxAttachmentsPerPage)
        {
            return AttachmentFailures.LimitReached();
        }

        var row = new PageAttachmentRow
        {
            Id = id,
            OrganizationId = actor.OrganizationId,
            PageId = target.Value.Page.Id,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            UploadedBy = actor.PersonId,
            UploadedAt = Truncate(clock.GetUtcNow()),
        };
        db.PageAttachments.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToContract(row);
    }

    private static Task<int> CountAsync(KnowledgeDbContext db, Actor actor, Guid pageId, CancellationToken cancellationToken) =>
        db.PageAttachments.CountAsync(a => a.OrganizationId == actor.OrganizationId && a.PageId == pageId, cancellationToken);

    /// <summary>PostgreSQL 的 timestamptz 只存到微秒；不先截掉的話，回給呼叫端的時間會和之後讀回來的差一點點。</summary>
    private static DateTimeOffset Truncate(DateTimeOffset now) =>
        new(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);

    private static PageAttachment ToContract(PageAttachmentRow row) =>
        new(row.Id, row.PageId, row.FileName, row.ContentType, row.SizeBytes, row.Sha256, row.UploadedBy, row.UploadedAt);

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

    [LoggerMessage(Level = LogLevel.Error, Message = "頁面附件 {AttachmentId}（{FileName}）在資料庫裡有紀錄，儲存空間裡卻找不到檔案；請從備份還原。")]
    private static partial void LogMissingFile(ILogger logger, Guid attachmentId, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "頁面附件 {AttachmentId} 的檔案沒有從儲存空間刪掉，留下一個沒人參考的檔案（佔空間，不影響資料）。")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, Guid attachmentId);
}
