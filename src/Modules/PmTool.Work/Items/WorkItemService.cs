using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Activities;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Events;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

internal sealed partial class WorkItemService(
    IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, TimeProvider time) : IWorkItemService
{
    public async Task<Result<WorkItem>> CreateAsync(Actor actor, string projectKey, NewWorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(item);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 讀藍圖之前先對專案取共用鎖：套用藍圖的變更集進行中時等它做完，新增的項目照新的藍圖檢查。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.ItemCreate))
        {
            return access.Missing(WorkPermissions.ItemCreate);
        }

        if (item.Type is null || !access.Schema.ItemTypes.TryGetValue(item.Type, out var itemType))
        {
            return WorkFailures.Invalid(WorkErrorCodes.ItemUnknownType, "type", $"這個專案沒有{Quote(item.Type)}這種項目類型。");
        }

        var changes = await ItemRules.CheckAsync(db, access, itemType, null, ItemChanges.AsPatch(item), cancellationToken);
        if (changes.Failure is { } failure)
        {
            return failure;
        }

        var created = await ItemCreation.InsertAsync(db, time, actor, access, itemType, changes.Value, null, imported: false, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ItemReader.ToWorkItem(created, access.Project.Key, changes.Value.Parent.Value?.Number);
    }

    public async Task<Result<WorkItem>> GetAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemRead, forUpdate: false, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        return await ReadAsync(db, actor, target.Value.Item, cancellationToken);
    }

    public async Task<Result<WorkItem>> UpdateAsync(Actor actor, string itemKey, WorkItemPatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(patch);

        // 設上層時先鎖住自己、再對上層取共用鎖，和別的請求仍可能互鎖（見 ParentRules）；被退回的那一次整個重做。
        return await DeadlockRetry.OnceAsync(() => UpdateOnceAsync(actor, itemKey, patch, cancellationToken));
    }

    private async Task<Result<WorkItem>> UpdateOnceAsync(Actor actor, string itemKey, WorkItemPatch patch, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 先鎖住這一列再檢查與合併：同時進來的修改會排隊，後到的讀到的是前一個改完的值，兩邊的修改都留得下來。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemUpdate, forUpdate: true, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, itemType) = target.Value;

        // 鎖住之後才比對：版本是在同一把鎖底下讀的，兩個人帶著同一個版本同時存，只有排在前面的那一個過得了。
        if (DescriptionIsStale(patch, item))
        {
            return new Failure(FailureKind.Conflict, WorkErrorCodes.ItemDescriptionConflict, "這段說明在你編輯時被別人改過了。");
        }

        var checkedChanges = await ItemRules.CheckAsync(db, access, itemType, item, patch, cancellationToken);
        if (checkedChanges.Failure is { } invalid)
        {
            return invalid;
        }

        // 什麼都沒變就不寫也不記：活動裡不會有空的修改，updatedAt 也不會因為重送同樣的值而跳動。
        var changes = checkedChanges.Value.WithoutUnchanged(item, access.Schema);
        if (changes.IsEmpty)
        {
            return await ReadAsync(db, actor, item, cancellationToken);
        }

        var updated = changes.ApplyTo(item, WorkClock.Now(time));
        await ItemStore.WriteAsync(db, updated, changes, cancellationToken);
        var described = await ActivityChanges.DescribeAsync(db, access, itemType, item, changes, cancellationToken);
        var activity = ActivityRecords.Updated(actor, item, updated.UpdatedAt, described);
        db.ItemActivities.Add(activity);

        // 說明只算新加的提及：改別的欄位或改說明裡的別處時，原本就提及的人不會再被通知一次。
        var mentioned = changes.Description.HasValue
            ? await Mentions.NotifiableAsync(db, access, actor, updated, item.Description, updated.Description, cancellationToken)
            : [];
        db.Events.Add(ItemEvents.Updated(actor, access, item, updated, described, mentioned, activity.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadAsync(db, actor, updated, cancellationToken);
    }

    private static bool DescriptionIsStale(WorkItemPatch patch, ItemEntity item) =>
        patch.Description.HasValue
        && patch.ExpectedDescriptionRevision is { } expected
        && !string.Equals(expected, DescriptionRevisions.Of(item.Description), StringComparison.Ordinal);

    public async Task<Result<WorkItem>> TransitionAsync(Actor actor, string itemKey, string toState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 鎖住這一列再檢查：要求先填的欄位要看最新的值，不能讓同時進來的修改把它清掉之後還轉過去。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemTransition, forUpdate: true, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, itemType) = target.Value;
        var allowed = TransitionGuard.Check(access.Schema, itemType, item, access.Role, toState);
        if (allowed.Failure is { } blocked)
        {
            return blocked;
        }

        var updated = item with
        {
            State = allowed.Value.Key,
            StateCategory = StateCategoryText.ToText(allowed.Value.Category),
            UpdatedAt = WorkClock.Now(time),
        };
        await ItemStore.WriteStateAsync(db, updated, cancellationToken);
        var activity = ActivityRecords.Transitioned(actor, item, updated.UpdatedAt, item.State, updated.State);
        db.ItemActivities.Add(activity);
        db.Events.Add(ItemEvents.Transitioned(actor, access, itemType, item, updated, activity.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadAsync(db, actor, updated, cancellationToken);
    }

    public async Task<Result<DeletedWorkItem>> DeleteAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 先鎖住這一列再檢查下層：同時要掛到它底下的新增或修改會等這裡提交，之後就找不到這個上層（見 ItemStore.ShareAsync）。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemDelete, forUpdate: true, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, _) = target.Value;
        var key = ItemKeys.Format(access.Project.Key, item.Number);
        var children = await ChildItems.OfAsync(db, item, access.Items, cancellationToken);
        if (children.Count > 0)
        {
            return ChildItems.HasChildren(access.Project.Key, key, children);
        }

        var now = WorkClock.Now(time);
        await ItemStore.WriteDeletedAsync(db, item, actor.PersonId, now, cancellationToken);
        var activity = ActivityRecords.Deleted(actor, item, now);
        db.ItemActivities.Add(activity);
        db.Events.Add(ItemEvents.Deleted(actor, access, item, now, activity.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DeletedWorkItem(key, now);
    }

    public async Task<Result<WorkItem>> ChangeTypeAsync(Actor actor, string itemKey, string type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // 和修改一樣先鎖住自己、再對上層取共用鎖，被選為互鎖的犧牲者時整個重做一次。
        return await DeadlockRetry.OnceAsync(() => ChangeTypeOnceAsync(actor, itemKey, type, cancellationToken));
    }

    private async Task<Result<WorkItem>> ChangeTypeOnceAsync(Actor actor, string itemKey, string type, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 先鎖住這一列再檢查上下層：同時要掛到它底下的新增或修改會等這裡提交，照新的類型檢查（見 ItemStore.ShareAsync）。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemUpdate, forUpdate: true, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, current) = target.Value;
        if (type is null || !access.Schema.ItemTypes.TryGetValue(type, out var next))
        {
            return WorkFailures.Invalid(WorkErrorCodes.ItemUnknownType, CoreFields.Type, $"這個專案沒有{Quote(type)}這種項目類型。");
        }

        if (next.Key == current.Key)
        {
            return await ReadAsync(db, actor, item, cancellationToken);
        }

        var problems = await TypeChangeRules.CheckAsync(db, access, item, next, cancellationToken);
        if (problems.Count > 0)
        {
            return WorkFailures.Invalid(problems);
        }

        var change = TypeChange.Plan(access.Schema, current, next, item, access.Can(WorkPermissions.ItemTransition), WorkClock.Now(time));
        await ItemStore.WriteTypeAsync(db, change.After, cancellationToken);
        await RecordTypeChangeAsync(db, actor, access, change, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadAsync(db, actor, change.After, cancellationToken);
    }

    public async Task<Result<WorkItemPage>> ListAsync(Actor actor, string projectKey, WorkItemQuery query, CancellationToken cancellationToken)
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

        var plan = ItemQueryPlan.From(query);
        if (plan.Failure is { } invalid)
        {
            return invalid;
        }

        if (plan.Value.CheckAgainst(access.Schema) is { } mismatch)
        {
            return mismatch;
        }

        // 多拿一筆，看得出還有沒有下一頁，不必另外算總數。
        var rows = await plan.Value.Apply(db.Items.AsNoTracking(), access, actor.PersonId).Select(ItemReader.WithoutDescription).ToListAsync(cancellationToken);
        var page = rows.Take(query.Limit).ToArray();
        var next = rows.Count > query.Limit ? ItemCursor.Encode(plan.Value.Offset + query.Limit) : null;
        return new WorkItemPage(await ItemReader.ToWorkItemsAsync(db, actor.OrganizationId, page, cancellationToken), next);
    }

    public async Task<IReadOnlyList<WorkItem>> ListAssignedToMeAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var closed = StateCategoryText.Closed.ToArray();
        var items = ItemStore.Live(db)
            .AsNoTracking()
            .Where(i => i.OrganizationId == actor.OrganizationId && i.AssigneeId == actor.PersonId && !closed.Contains(i.StateCategory))
            // 封存的專案收起來了，它的項目不再出現在「我的工作」。
            .Where(i => db.Projects.Any(p => p.OrganizationId == i.OrganizationId && p.Id == i.ProjectId && p.ArchivedAt == null));

        // 組織管理員看得到組織裡所有專案；其他人只看自己是成員、而且角色讀得到項目的專案。
        // 負責人就是自己，只看自己的角色也一定看得到，不必另外加範圍。
        if (!actor.IsOrganizationAdmin)
        {
            var readable = (await ReadableProjects.OfMemberAsync(db, cache, actor, cancellationToken)).ToArray();
            items = items.Where(i => readable.Contains(i.ProjectId));
        }

        var rows = await items
            .OrderBy(i => i.DueDate == null)
            .ThenBy(i => i.DueDate)
            .ThenBy(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Take(WorkItemQuery.MaxLimit)
            .Select(ItemReader.WithoutDescription)
            .ToListAsync(cancellationToken);
        return await ItemReader.ToWorkItemsAsync(db, actor.OrganizationId, rows, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<WorkItem>>> SearchAsync(Actor actor, string text, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var checkedText = ItemSearch.Validate(text, limit);
        if (checkedText.Failure is { } invalid)
        {
            return invalid;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = await ItemSearch.RunAsync(db, cache, actor, checkedText.Value, limit, cancellationToken);
        return found.ToArray();
    }

    /// <summary>留一筆 type_changed，封存的值指到它，和項目的修改在同一個交易裡。</summary>
    private async Task RecordTypeChangeAsync(
        WorkDbContext db, Actor actor, ProjectAccess access, TypeChange change, CancellationToken cancellationToken)
    {
        var (before, after) = (change.Before, change.After);
        var activity = ActivityRecords.TypeChanged(
            actor, before, after.UpdatedAt, change.Changes, change.StateMoved ? before.State : null, change.StateMoved ? after.State : null);
        db.ItemActivities.Add(activity);

        // 每個寫入點都要在同一個交易裡留一筆事件：即時推送與通知靠它知道項目變了。類型改了就是一次修改，欄位是 item_type 與封存的欄位。
        db.Events.Add(ItemEvents.Updated(actor, access, before, after, change.Changes, [], activity.Id));
        db.ArchivedFieldValues.AddRange(change.Archived.Select(value => new ArchivedFieldValueEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = before.OrganizationId,
            ProjectId = before.ProjectId,
            ItemId = before.Id,
            Field = value.Field,
            Value = value.Value.GetRawText(),
            ItemActivityId = activity.Id,
            ArchivedAt = after.UpdatedAt,
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<WorkItem> ReadAsync(WorkDbContext db, Actor actor, ItemEntity item, CancellationToken cancellationToken) =>
        (await ItemReader.ToWorkItemsAsync(db, actor.OrganizationId, [item], cancellationToken))[0];
}
