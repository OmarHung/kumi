using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Activities;
using PmTool.Work.Common;
using PmTool.Work.Events;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

// 復原刪除的項目與已刪除的清單；其餘的單一項目操作在 WorkItemService.cs。
internal sealed partial class WorkItemService
{
    public async Task<Result<WorkItem>> RestoreAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // 檢查上層時對它取共用鎖，和別的請求仍可能互鎖（見 ItemRestore.LiveParentAsync）；被退回的那一次整個重做。
        return await DeadlockRetry.OnceAsync(() => RestoreOnceAsync(actor, itemKey, cancellationToken));
    }

    private async Task<Result<WorkItem>> RestoreOnceAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await ItemRestore.FindAsync(db, cache, actor, itemKey, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item) = target.Value;
        if (item.DeletedAt is null)
        {
            return await ReadAsync(db, actor, item, cancellationToken);
        }

        if (!access.Schema.ItemTypes.TryGetValue(item.Type, out var itemType))
        {
            return ItemRestore.TypeRemoved(item.Type);
        }

        var parent = await ItemRestore.LiveParentAsync(db, access, item, cancellationToken);
        if (parent.Failure is { } parentDeleted)
        {
            return parentDeleted;
        }

        var plan = RestorePlan.Make(access.Schema, itemType, item, parent.Value, access.Project.Key, WorkClock.Now(time));
        await ItemRestore.WriteAsync(db, plan.After, cancellationToken);
        var activity = ActivityRecords.Restored(actor, plan.After, plan.After.UpdatedAt, plan.Changes, plan.FromState, plan.ToState);
        db.ItemActivities.Add(activity);
        db.ArchivedFieldValues.AddRange(plan.Archived.Select(value => ArchivedBy(plan.After, activity.Id, value)));
        db.Events.Add(ItemEvents.Restored(actor, access, itemType, plan.After, plan.FromState, activity.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadAsync(db, actor, plan.After, cancellationToken);
    }

    public async Task<Result<DeletedItemPage>> ListDeletedAsync(
        Actor actor, string projectKey, DeletedItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.ItemRead))
        {
            return access.Missing(WorkPermissions.ItemRead);
        }

        if (query.Limit is < 1 or > DeletedItemQuery.MaxLimit)
        {
            return WorkFailures.QueryInvalid(QueryPaths.Limit, $"每頁的數量要在 1 到 {DeletedItemQuery.MaxLimit} 之間。");
        }

        if (!ItemCursor.TryDecode(query.Cursor, out var offset) || offset > int.MaxValue - DeletedItemQuery.MaxLimit)
        {
            return WorkFailures.QueryInvalid(QueryPaths.Cursor, "分頁的游標不對，請從第一頁重新查詢。");
        }

        // 多拿一筆，看得出還有沒有下一頁。同一刻刪除的依編號遞減，分頁才穩定。
        var rows = await access.Items.Apply(db.Items.AsNoTracking())
            .Where(i => i.OrganizationId == actor.OrganizationId && i.ProjectId == access.Project.Id && i.DeletedAt != null)
            .OrderByDescending(i => i.DeletedAt)
            .ThenByDescending(i => i.Number)
            .Skip(offset)
            .Take(query.Limit + 1)
            .Select(i => new DeletedRow(
                i.Number,
                i.Type,
                i.Title,
                i.State,
                i.StateCategory,
                db.Items.Where(p => p.OrganizationId == i.OrganizationId && p.Id == i.ParentId).Select(p => (long?)p.Number).FirstOrDefault(),
                i.DeletedBy,
                i.DeletedAt!.Value))
            .ToListAsync(cancellationToken);
        var summaries = rows.Take(query.Limit).Select(row => row.ToSummary(access.Project.Key)).ToArray();
        return new DeletedItemPage(summaries, rows.Count > query.Limit ? ItemCursor.Encode(offset + query.Limit) : null);
    }

    private ArchivedFieldValueEntity ArchivedBy(ItemEntity item, Guid activityId, ChangeSets.ArchivedValue value) => new()
    {
        Id = WorkClock.NewId(time),
        OrganizationId = item.OrganizationId,
        ProjectId = item.ProjectId,
        ItemId = item.Id,
        Field = value.Field,
        Value = value.Value.GetRawText(),
        ItemActivityId = activityId,
        ArchivedAt = item.UpdatedAt,
    };

    private sealed record DeletedRow(
        long Number, string Type, string Title, string State, string StateCategory, long? ParentNumber, Guid? DeletedBy, DateTimeOffset DeletedAt)
    {
        public DeletedItemSummary ToSummary(string projectKey) => new(
            ItemKeys.Format(projectKey, Number),
            projectKey,
            Number,
            Type,
            Title,
            State,
            StateCategoryText.Parse(StateCategory),
            ParentNumber is { } parent ? ItemKeys.Format(projectKey, parent) : null,
            DeletedBy,
            DeletedAt);
    }
}
