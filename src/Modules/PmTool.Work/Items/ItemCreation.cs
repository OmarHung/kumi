using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Activities;
using PmTool.Work.Common;
using PmTool.Work.Events;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 歷史匯入的項目：原本的建立時間（也是 updatedAt）、來源與要關注的人。建立的人是呼叫端傳進來的 actor
/// （匯入的人替原作者署名），活動與事件都記成他。
/// </summary>
internal sealed record HistoryStamp(string Source, DateTimeOffset At, IReadOnlyList<Guid> Followers, History.HistoryActivityIds Ids);

/// <summary>
/// 一個新項目寫進資料庫時一起要寫的東西：項目、「建立」活動、<c>item_created</c> 事件（帶活動的編號）。
/// 新增項目與建立專案時附上範例項目都走這裡，兩邊留下的紀錄才不會漂移，關注、通知、事件流的行為也才一樣。
/// 只加進 <paramref name="db"/> 的變更追蹤，儲存與交易由呼叫端決定。
/// </summary>
internal static class ItemCreation
{
    public static void Add(
        WorkDbContext db,
        Actor actor,
        ProjectAccess access,
        ItemType itemType,
        ItemEntity item,
        IReadOnlyList<Guid> mentioned,
        bool imported = false,
        HistoryStamp? history = null)
    {
        db.Items.Add(item);
        var activity = ActivityRecords.Created(actor, item, history?.Source);
        activity = history is null ? activity : history.Ids.Assign(activity);
        db.ItemActivities.Add(activity);
        db.Events.Add(ItemEvents.Created(actor, access, itemType, item, mentioned, activity.Id, imported, history?.Followers));
    }

    /// <summary>
    /// 發下一個編號、組出項目並加進變更追蹤（不儲存）：新增與匯入共用，兩邊的項目長得一樣。
    /// <paramref name="state"/> 是 null 時從流程的第一個狀態開始；匯入指定了狀態就直接用它（呼叫端已經確認它在流程裡）。
    /// </summary>
    public static async Task<ItemEntity> InsertAsync(
        WorkDbContext db,
        TimeProvider time,
        Actor actor,
        ProjectAccess access,
        ItemType itemType,
        ItemChanges changes,
        WorkflowState? state,
        bool imported,
        CancellationToken cancellationToken,
        HistoryStamp? history = null)
    {
        var start = state ?? access.Schema.WorkflowOf(itemType).States[0];
        var now = history?.At ?? WorkClock.Now(time);
        var item = new ItemEntity
        {
            Id = history is null ? WorkClock.NewId(time) : Guid.CreateVersion7(now),
            OrganizationId = actor.OrganizationId,
            ProjectId = access.Project.Id,
            Number = await ItemStore.NextNumberAsync(db, access.Project, cancellationToken),
            Type = itemType.Key,
            Title = changes.Title.Value,
            Description = changes.Description.Value,
            State = start.Key,
            StateCategory = StateCategoryText.ToText(start.Category),
            AssigneeId = changes.AssigneeId.Value,
            Priority = (short)changes.Priority.Value,
            StartDate = changes.StartDate.Value,
            DueDate = changes.DueDate.Value,
            Estimate = changes.Estimate.Value,
            ParentId = changes.Parent.Value?.Id,
            CycleId = changes.CycleId.Value,
            MilestoneId = changes.MilestoneId.Value,
            Fields = FieldValues.Serialize(FieldValues.Merge(FieldValues.None, changes.Fields)),
            CreatedBy = actor.PersonId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // 歷史不通知任何人，不必算提及的人。
        IReadOnlyList<Guid> mentioned = history is null
            ? await Mentions.NotifiableAsync(db, access, actor, item, null, item.Description, cancellationToken)
            : [];
        Add(db, actor, access, itemType, item, mentioned, imported, history);
        return item;
    }
}
