using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.HostAgents;
using PmTool.Ai.Persistence;
using PmTool.Storage;
using PmTool.Work;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 助理訊息的附件（internal/HANDOFF-ai-attachments.md 第 4 節）。上傳的流程照項目與知識庫的附件：先檢查權限與數量（不合格就不讀內容）→
/// 寫進儲存空間（上限是三種類型裡最大的）→ 看內容判斷類型、依類型的上限 → 交易裡重新確認並記下 → 沒記成功就盡量把檔案刪掉。
/// 專案代號是 null 時是組織對話的附件（<c>project_key</c> 是 null）：不問工作引擎，和專案的附件分開。
/// </summary>
internal sealed partial class AssistantAttachmentService(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    HostSourceOptions hostSources,
    IFileStore store,
    IStorageQuota quota,
    TimeProvider clock,
    ILogger<AssistantAttachmentService> logger,
    ItemFinding.ConversationReadGuard readGuard) : IAssistantAttachmentService
{
    public async Task<Result<AssistantAttachment>> UploadAsync(
        Actor actor, string? projectKey, NewAssistantAttachment upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(upload);

        // 第一步：不合格就直接回失敗，不讀內容（內容可能有 10 MiB，擋得掉的不要讓它白白送進來）。權限先於檔名。
        var precheck = await CheckBeforeWritingAsync(actor, projectKey, upload.FileName, cancellationToken);
        if (precheck.Failure is { } rejected)
        {
            return rejected;
        }

        var (name, scope) = precheck.Value;

        // 第二步：寫進儲存空間。這時還沒有資料列，失敗或超過上限都只要清掉檔案。
        var id = Guid.CreateVersion7(clock.GetUtcNow());
        var key = AttachmentMapper.KeyOf(id);
        var committed = false;
        try
        {
            var stored = await store.WriteAsync(
                key, upload.Content, "application/octet-stream", AssistantAttachmentRules.MaxUploadBytes, cancellationToken);
            if (stored is null)
            {
                return AttachmentFailures.TooLargeToRead();
            }

            var sniffed = await SniffStoredAsync(key, cancellationToken);
            if (sniffed is null)
            {
                return AttachmentFailures.TypeNotAllowed();
            }

            if (stored.SizeBytes > AttachmentKinds.MaxBytesOf(sniffed.Kind))
            {
                return AttachmentFailures.TooLarge(sniffed.Kind);
            }

            if (sniffed.Problem is { } problem)
            {
                return AttachmentFailures.Unreadable(problem);
            }

            // 第三步：交易裡重新確認並寫資料列。數量與容量在寫檔的這段時間裡可能變了。
            var added = await RecordAsync(actor, scope, id, name, sniffed, stored, cancellationToken);
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

    public async Task<Result<Unit>> DeletePendingAsync(Actor actor, string? projectKey, Guid attachmentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var scope = await OpenScopeAsync(actor, projectKey, cancellationToken);
        if (scope.Failure is { } failure)
        {
            return failure;
        }

        var key = scope.Value.ProjectKey;

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 先把待送的標成孤兒（有綁定時間、沒有訊息）：條件寫在更新裡，檢查和更新之間剛好被綁到訊息上的不動；
        // 標好之後送出的 UPDATE 綁不到它、下載與再刪都當作不存在，維護也認得它。這樣先刪檔、再刪資料列，
        // 刪檔失敗時資料列還在（容量的依據不能在檔案還在的時候放掉），留給維護下一輪清。
        var claimed = await db.Attachments
            .Where(a => a.Id == attachmentId
                && a.OrganizationId == actor.OrganizationId
                && a.PersonId == actor.PersonId
                && a.ProjectKey == key
                && a.BoundAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.BoundAt, clock.GetUtcNow()), cancellationToken);
        if (claimed == 0)
        {
            return AttachmentFailures.NotFound();
        }

        if (await DeleteFileQuietlyAsync(AttachmentMapper.KeyOf(attachmentId), attachmentId))
        {
            // 請求取消了也要把資料列刪掉：檔案已經不在，留著只會等維護來清。
            await db.Attachments
                .Where(a => a.Id == attachmentId && a.BoundAt != null && a.MessageId == null)
                .ExecuteDeleteAsync(CancellationToken.None);
        }

        return Unit.Value;
    }

    public async Task<Result<AssistantAttachmentContent>> OpenAsync(
        Actor actor, string? projectKey, Guid attachmentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var scope = await OpenScopeAsync(actor, projectKey, cancellationToken);
        if (scope.Failure is { } failure)
        {
            return failure;
        }

        var key = scope.Value.ProjectKey;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(
            a => a.Id == attachmentId
                && a.OrganizationId == actor.OrganizationId
                && a.PersonId == actor.PersonId
                && a.ProjectKey == key,
            cancellationToken);
        if (row is null || !await IsVisibleAsync(db, actor, scope.Value, row, cancellationToken))
        {
            return AttachmentFailures.NotFound();
        }

        var content = await store.OpenReadAsync(AttachmentMapper.KeyOf(row.Id), cancellationToken);
        if (content is null)
        {
            // 資料列剛好被刪（使用者刪了、維護清了）、檔案隨後也不見：已經清掉了，不是資料不一致，不必驚動人。
            if (!await db.Attachments.AsNoTracking().AnyAsync(a => a.Id == row.Id, cancellationToken))
            {
                return AttachmentFailures.NotFound();
            }

            // 資料庫有資料列、儲存空間沒有檔案：備份沒還原到位、或有人手動刪了檔案。使用者能做的只有回報，所以記錯誤。
            LogMissingFile(logger, row.Id, row.FileName);
            return AttachmentFailures.NotFound();
        }

        return new AssistantAttachmentContent(AttachmentMapper.ToContract(row), row.Sha256, content);
    }

    /// <summary>
    /// 待送的自己看得到；已送出的要綁在還在的對話上（孤兒＝對話已經刪了，不給），而且那則對話現在還看得到
    /// （在「看得到全部項目」時回覆過、或讀過別人項目的專案（find_items）現在只剩自己的項目的，對話整則當作不存在，附件也一樣）。組織對話的附件要綁在組織對話上。
    /// </summary>
    private async Task<bool> IsVisibleAsync(
        AiDbContext db, Actor actor, AttachmentScope scope, AiAttachmentRow row, CancellationToken cancellationToken)
    {
        if (row.BoundAt is null)
        {
            return true;
        }

        if (row.MessageId is null || row.ConversationId is not { } conversationId)
        {
            return false;
        }

        var projectId = scope.ProjectId;
        var canSeeAll = scope.CanReadAllItems;
        var visible = await db.Conversations.AsNoTracking().AnyAsync(
            c => c.Id == conversationId
                && c.OrganizationId == actor.OrganizationId
                && c.PersonId == actor.PersonId
                && c.ProjectId == projectId
                && (canSeeAll || !c.SawAllItems),
            cancellationToken);
        if (!visible)
        {
            return false;
        }

        var stale = await readGuard.DeleteStaleAsync(db, actor, conversationId, clock.GetUtcNow(), cancellationToken);
        return !stale.Contains(conversationId);
    }

    /// <summary>權限（先於檔名）與待送的數量；成功時回傳整理過的檔名與附件屬於哪一邊。</summary>
    private async Task<Result<(string Name, AttachmentScope Scope)>> CheckBeforeWritingAsync(
        Actor actor, string? projectKey, string? fileName, CancellationToken cancellationToken)
    {
        var scope = await OpenScopeAsync(actor, projectKey, cancellationToken);
        if (scope.Failure is { } noProject)
        {
            return noProject;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await AssistantAccess.IsAvailableAsync(db, hostSources, actor, cancellationToken))
        {
            return AssistantFailures.Unavailable();
        }

        var name = AttachmentNames.Check(fileName);
        if (name.Failure is { } invalidName)
        {
            return invalidName;
        }

        if (await CountPendingAsync(db, actor, clock.GetUtcNow(), cancellationToken) >= AssistantAttachmentRules.MaxPendingPerPerson)
        {
            return AttachmentFailures.TooManyPending();
        }

        // 已經用滿就不必讀內容；沒滿的話寫完之後還會在交易裡用實際的大小再確認一次。
        var room = await quota.CheckAsync(actor.OrganizationId, actor.PersonId, incomingBytes: 0, cancellationToken);
        if (!room.IsAllowed)
        {
            return AttachmentFailures.Quota(room);
        }

        return (name.Value, scope.Value);
    }

    /// <summary>
    /// 專案的附件要看得到專案、有讀設定的權限（和開對話同一份檢查）；組織對話的附件不看任何專案（組織的有效成員都能開組織對話），
    /// 組織有沒有開 AI 由呼叫的地方各自檢查。
    /// </summary>
    private async Task<Result<AttachmentScope>> OpenScopeAsync(Actor actor, string? projectKey, CancellationToken cancellationToken)
    {
        if (projectKey is null)
        {
            return new AttachmentScope(null);
        }

        var project = await AssistantAccess.OpenProjectAsync(projects, actor, projectKey, cancellationToken);
        return project.Failure is { } failure ? failure : new AttachmentScope(project.Value);
    }

    /// <summary>附件屬於哪一邊：專案（<see cref="Project"/> 有值）或組織對話（null）。</summary>
    private sealed record AttachmentScope(ProjectDetail? Project)
    {
        /// <summary>工作引擎回的專案代號（呼叫的人給的可能大小寫不同）；組織對話的是 null。</summary>
        public string? ProjectKey => Project?.Project.Key;

        public Guid? ProjectId => Project?.Project.Id;

        // 組織對話不讀項目，沒有「看得到全部項目」的問題。
        public bool CanReadAllItems => Project is null || AssistantAccess.CanReadAllItems(Project);
    }

    private async Task<SniffedFile?> SniffStoredAsync(string key, CancellationToken cancellationToken)
    {
        await using var content = await store.OpenReadAsync(key, cancellationToken)
            ?? throw new InvalidOperationException("剛寫進儲存空間的附件讀不到。");
        return await AttachmentSniffer.SniffAsync(content, cancellationToken);
    }

    private async Task<Result<AssistantAttachment>> RecordAsync(
        Actor actor, AttachmentScope scope, Guid id, string fileName, SniffedFile sniffed, StoredFile stored, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 同一個人同時上傳的在這裡排隊，待送數量的檢查才不會同時過關而超過上限。
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_attachments:" + actor.OrganizationId + ":" + actor.PersonId}, 0))",
            cancellationToken);
        if (await CountPendingAsync(db, actor, now, cancellationToken) >= AssistantAttachmentRules.MaxPendingPerPerson)
        {
            return AttachmentFailures.TooManyPending();
        }

        // 儲存空間的鎖是最後一把：和項目、知識庫的上傳排在同一個隊伍，加總到的是已經提交的量。
        await StorageLocks.OrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var room = await quota.CheckAsync(actor.OrganizationId, actor.PersonId, stored.SizeBytes, cancellationToken);
        if (!room.IsAllowed)
        {
            return AttachmentFailures.Quota(room);
        }

        var row = new AiAttachmentRow
        {
            Id = id,
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ProjectKey = scope.ProjectKey,
            FileName = fileName,
            ContentType = sniffed.ContentType,
            Kind = AttachmentKinds.NameOf(sniffed.Kind),
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            UploadedAt = AttachmentMapper.Truncate(now),
        };
        db.Attachments.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AttachmentMapper.ToContract(row);
    }

    private static Task<int> CountPendingAsync(AiDbContext db, Actor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // 過了期限還沒被清掉的不算：它們不能再送出，不該擋住使用者上傳新的。
        var oldest = now - AssistantAttachmentRules.PendingLifetime;
        return db.Attachments.CountAsync(
            a => a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId && a.BoundAt == null && a.UploadedAt >= oldest,
            cancellationToken);
    }

    /// <summary>盡力而為：取消的請求也要清掉，所以不用呼叫端的取消權杖；失敗只記 Warning（回傳 false），留下的檔案不會造成錯。</summary>
    private async Task<bool> DeleteFileQuietlyAsync(string key, Guid attachmentId)
    {
        try
        {
            await store.DeleteAsync(key, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogDeleteFailed(logger, exception, attachmentId);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "助理附件 {AttachmentId}（{FileName}）在資料庫裡有紀錄，儲存空間裡卻找不到檔案；請從備份還原。")]
    private static partial void LogMissingFile(ILogger logger, Guid attachmentId, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "助理附件 {AttachmentId} 的檔案沒有從儲存空間刪掉，留下一個沒人參考的檔案（佔空間，不影響資料）。")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, Guid attachmentId);
}
