using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Events;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Activities;

// 留言的編輯、刪除、復原與版本。四個方法都先鎖住項目那一列（看版本除外），規則見 internal/HANDOFF-comment-edit.md 第 4 節。
internal sealed partial class ItemActivityService
{
    /// <summary>找到的留言連同專案、項目與它的鍵。</summary>
    private sealed record CommentTarget(ProjectAccess Access, ItemEntity Item, ItemActivityEntity Comment)
    {
        public string ItemKey => ItemKeys.Format(Access.Project.Key, Item.Number);
    }

    public async Task<Result<ItemActivity>> EditCommentAsync(
        Actor actor, string itemKey, Guid commentId, string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var found = await FindCommentAsync(db, actor, itemKey, commentId, forUpdate: true, cancellationToken);
        if (found.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, row) = found.Value;
        if (CheckEditAccess(access, actor, row) is { } denied)
        {
            return denied;
        }

        var comment = CommentRules.Check(body);
        if (comment.Failure is { } invalid)
        {
            return invalid;
        }

        var previous = ActivityRecords.StoredComment(row);
        if (string.Equals(previous, comment.Value, StringComparison.Ordinal))
        {
            return ActivityRecords.ToActivity(row, found.Value.ItemKey);
        }

        var now = WorkClock.Now(time);
        var content = ActivityRecords.CommentContent(comment.Value);
        if (await KeepRevisionAsync(db, row, previous, cancellationToken) is { } tooMany)
        {
            return tooMany;
        }

        await db.ItemActivities
            .Where(a => a.OrganizationId == row.OrganizationId && a.Id == row.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(a => a.Content, content).SetProperty(a => a.EditedAt, (DateTimeOffset?)now),
                cancellationToken);
        var mentioned = await Mentions.NotifiableAsync(db, access, actor, item, previous, comment.Value, cancellationToken);
        db.Events.Add(ItemEvents.CommentEdited(actor, access, item, now, comment.Value, mentioned, row.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ActivityRecords.ToActivity(row with { Content = content, EditedAt = now }, found.Value.ItemKey);
    }

    /// <summary>刪除的要先復原；編輯要有 comment.create（封存的專案沒有）而且是作者本人。</summary>
    private static Failure? CheckEditAccess(ProjectAccess access, Actor actor, ItemActivityEntity row)
    {
        if (row.DeletedAt is not null)
        {
            return CommentIsDeleted("這則留言已經刪除，先復原才能編輯。");
        }

        if (!access.Can(WorkPermissions.CommentCreate))
        {
            return access.Missing(WorkPermissions.CommentCreate);
        }

        // 匯入的留言署的是被歸屬的人：作者本人也不是真正寫它的人，所以誰都不能編輯。
        if (row.ImportedFrom is not null)
        {
            return new Failure(FailureKind.Conflict, WorkErrorCodes.CommentImported, "從其他系統匯入的留言不能編輯。");
        }

        return row.ActorId == actor.PersonId ? null : CommentNotAuthor("只能編輯自己的留言。");
    }

    /// <summary>
    /// 把現在的內容存成舊版本。現在的版本不在版本表裡，所以舊版本的數量加一才是現在的版本號；現在是第 100 版就不能再改。
    /// </summary>
    private async Task<Failure?> KeepRevisionAsync(WorkDbContext db, ItemActivityEntity row, string previous, CancellationToken cancellationToken)
    {
        var revisionCount = await db.CommentRevisions.CountAsync(
            r => r.OrganizationId == row.OrganizationId && r.CommentId == row.Id, cancellationToken);
        if (revisionCount >= WorkRules.MaxCommentRevisions - 1)
        {
            return new Failure(FailureKind.Conflict, WorkErrorCodes.CommentTooManyEdits, "這則留言已經改過太多次，請另外留一則。");
        }

        db.CommentRevisions.Add(new CommentRevisionEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = row.OrganizationId,
            ItemId = row.ItemId,
            CommentId = row.Id,
            Number = revisionCount + 1,
            Body = previous,
            WrittenAt = row.EditedAt ?? row.At,
        });
        return null;
    }

    public async Task<Result<ItemActivity>> DeleteCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var found = await FindCommentAsync(db, actor, itemKey, commentId, forUpdate: true, cancellationToken);
        if (found.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, row) = found.Value;
        if (row.DeletedAt is not null)
        {
            return ActivityRecords.ToActivity(row, found.Value.ItemKey);
        }

        // project.manage 在封存的專案還在，不明確擋的話管理者能在唯讀的專案裡刪留言。
        if (access.IsArchived)
        {
            return WorkFailures.ProjectArchived(access.Project.Name);
        }

        if (CheckDeleteAccess(access, actor, row) is { } denied)
        {
            return denied;
        }

        var now = WorkClock.Now(time);
        await db.ItemActivities
            .Where(a => a.OrganizationId == row.OrganizationId && a.Id == row.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(a => a.DeletedAt, (DateTimeOffset?)now).SetProperty(a => a.DeletedBy, (Guid?)actor.PersonId),
                cancellationToken);
        db.Events.Add(ItemEvents.CommentDeleted(actor, access, item, now, row.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ActivityRecords.ToActivity(row with { DeletedAt = now, DeletedBy = actor.PersonId }, found.Value.ItemKey);
    }

    public async Task<Result<ItemActivity>> RestoreCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var found = await FindCommentAsync(db, actor, itemKey, commentId, forUpdate: true, cancellationToken);
        if (found.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, row) = found.Value;
        if (row.DeletedAt is null)
        {
            return ActivityRecords.ToActivity(row, found.Value.ItemKey);
        }

        if (access.IsArchived)
        {
            return WorkFailures.ProjectArchived(access.Project.Name);
        }

        if (CheckRestoreAccess(access, actor, row) is { } denied)
        {
            return denied;
        }

        await db.ItemActivities
            .Where(a => a.OrganizationId == row.OrganizationId && a.Id == row.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(a => a.DeletedAt, (DateTimeOffset?)null).SetProperty(a => a.DeletedBy, (Guid?)null),
                cancellationToken);
        db.Events.Add(ItemEvents.CommentRestored(actor, access, item, WorkClock.Now(time), row.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ActivityRecords.ToActivity(row with { DeletedAt = null, DeletedBy = null }, found.Value.ItemKey);
    }

    public async Task<Result<IReadOnlyList<CommentRevision>>> ListCommentRevisionsAsync(
        Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = await FindCommentAsync(db, actor, itemKey, commentId, forUpdate: false, cancellationToken);
        if (found.Failure is { } failure)
        {
            return failure;
        }

        var row = found.Value.Comment;
        if (row.DeletedAt is not null)
        {
            return CommentIsDeleted("這則留言已經刪除，復原之後才能看版本。");
        }

        var older = await db.CommentRevisions
            .AsNoTracking()
            .Where(r => r.OrganizationId == row.OrganizationId && r.CommentId == row.Id)
            .OrderByDescending(r => r.Number)
            .ToListAsync(cancellationToken);
        var current = new CommentRevision(older.Count + 1, ActivityRecords.StoredComment(row), row.EditedAt ?? row.At, IsCurrent: true);
        return new[] { current }.Concat(older.Select(r => new CommentRevision(r.Number, r.Body, r.WrittenAt, IsCurrent: false))).ToArray();
    }

    /// <summary>項目看得到、留言存在且是這個項目的留言；不然是項目或留言的 NotFound。</summary>
    private async Task<Result<CommentTarget>> FindCommentAsync(
        WorkDbContext db, Actor actor, string itemKey, Guid commentId, bool forUpdate, CancellationToken cancellationToken)
    {
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemRead, forUpdate, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, _) = target.Value;
        var commented = ActivityRecords.CommentedKind;
        var row = await db.ItemActivities.AsNoTracking().FirstOrDefaultAsync(
            a => a.Id == commentId && a.OrganizationId == actor.OrganizationId && a.ItemId == item.Id && a.Kind == commented,
            cancellationToken);
        return row is null
            ? new Failure(FailureKind.NotFound, WorkErrorCodes.CommentNotFound, "找不到這則留言。")
            : new CommentTarget(access, item, row);
    }

    private static Failure? CheckDeleteAccess(ProjectAccess access, Actor actor, ItemActivityEntity row)
    {
        if (row.ImportedFrom is not null)
        {
            return access.Can(WorkPermissions.ProjectManage) ? null : CommentNotAuthor("匯入的留言只有管理專案的人能刪除。");
        }

        if (row.ActorId == actor.PersonId)
        {
            return access.Can(WorkPermissions.CommentCreate) ? null : access.Missing(WorkPermissions.CommentCreate);
        }

        return access.Can(WorkPermissions.ProjectManage)
            ? null
            : CommentNotAuthor("只能刪除自己的留言；要刪除別人的留言，需要管理專案的權限。");
    }

    private static Failure? CheckRestoreAccess(ProjectAccess access, Actor actor, ItemActivityEntity row)
    {
        if (access.Can(WorkPermissions.ProjectManage))
        {
            return null;
        }

        if (row.ImportedFrom is not null)
        {
            return CommentNotAuthor("匯入的留言只有管理專案的人能復原。");
        }

        var deletedByAuthor = row.DeletedBy == row.ActorId;
        var isAuthor = row.ActorId == actor.PersonId;
        if (deletedByAuthor && isAuthor)
        {
            // 作者自己刪的、本人來復原，卡住的只是權限，回缺少的權限才不會誤導成「不是作者」。
            return access.Can(WorkPermissions.CommentCreate) ? null : access.Missing(WorkPermissions.CommentCreate);
        }

        return CommentNotAuthor(!deletedByAuthor && isAuthor
            ? "這則留言是管理專案的人刪除的，只有管理專案的人能復原。"
            : "只能復原自己的留言；要復原別人的留言，需要管理專案的權限。");
    }

    private static Failure CommentIsDeleted(string message) => new(FailureKind.Conflict, WorkErrorCodes.CommentDeleted, message);

    private static Failure CommentNotAuthor(string message) => new(FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor, message);
}
