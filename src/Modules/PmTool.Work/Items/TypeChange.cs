using System.Text.Json;
using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;
using PmTool.Work.ChangeSets;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 改類型之後項目的樣子，以及要記進活動與封存表的內容。純計算，不碰資料庫；上下層的檢查在這之前已經做過。
/// </summary>
/// <param name="Changes">第一筆是 item_type 的前後值，接著是封存的自訂欄位，依舊類型裡欄位的順序。</param>
internal sealed record TypeChange(ItemEntity Before, ItemEntity After, IReadOnlyList<FieldChange> Changes, IReadOnlyList<ArchivedValue> Archived)
{
    /// <summary>狀態的 key 或類別有變：同 key 不同類別也算，活動要記下前後的狀態。</summary>
    public bool StateMoved => Before.State != After.State || Before.StateCategory != After.StateCategory;

    /// <param name="role">呼叫者在專案裡被指派的角色；不是成員時是 null。</param>
    /// <param name="canTransition">呼叫者有沒有 item.transition。</param>
    /// <param name="now">改類型的時刻，項目新的 updatedAt。</param>
    public static TypeChange Plan(
        ProjectSchema schema, ItemType current, ItemType next, ItemEntity item, string? role, bool canTransition, DateTimeOffset now)
    {
        var values = FieldValues.Parse(item.Fields);
        var detached = DetachedFields(current, next, values)
            .Select(key => ItemMigrator.Detach(Definition(schema, item, key), values[key]))
            .ToArray();
        var removed = detached.Select(d => d.Archived.Field).ToHashSet(StringComparer.Ordinal);
        var withoutRemoved = item with
        {
            Fields = removed.Count == 0
                ? item.Fields
                : FieldValues.Serialize(values.Where(pair => !removed.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal)),
        };
        var state = TargetState(schema.WorkflowOf(current), schema.WorkflowOf(next), withoutRemoved, role, canTransition);
        var after = withoutRemoved with
        {
            Type = next.Key,
            State = state.Key,
            StateCategory = StateCategoryText.ToText(state.Category),
            UpdatedAt = now,
        };
        var typeChange = new FieldChange(CoreFields.ItemType, JsonSerializer.SerializeToElement(current.Key), JsonSerializer.SerializeToElement(next.Key));
        return new TypeChange(item, after, [typeChange, .. detached.Select(d => d.Change)], [.. detached.Select(d => d.Archived)]);
    }

    /// <summary>
    /// 同一個流程照舊留著：限制相同，沒有跳過的東西。換流程時，新流程有同 key、同類別的狀態，而且新流程不限制轉換、
    /// 或呼叫者照這個項目現在的欄位值，能從新流程的起點只走自己符合的轉換（角色與要先填的欄位）走到那個狀態才留著；
    /// 沒有 item.transition 的人另外要符合「進入這個狀態的每一條轉換」，不比以前寬。
    /// 其他情況沒有同 key 的狀態時，新流程不限制轉換、呼叫者也能改狀態就搬到第一個同類別的狀態，不然回到新流程的第一個狀態，
    /// 也就是新項目的起點。改類型不能是跳過轉換限制的捷徑：有限制的流程只能從起點照轉換走，
    /// 停在中途或終點就跳過了前面步驟的角色限制與要先填的欄位；不能改狀態的人，本來就不該借改類型挪動狀態。
    /// </summary>
    private static WorkflowState TargetState(Workflow from, Workflow to, ItemEntity item, string? role, bool canTransition)
    {
        var category = StateCategoryText.Parse(item.StateCategory);
        var kept = to.States.FirstOrDefault(s => s.Key == item.State && s.Category == category);
        if (kept is not null && (from.Key == to.Key || CanKeep(to, kept, item, role, canTransition)))
        {
            return kept;
        }

        var movesFreely = kept is null && to.Transitions is null && canTransition;
        return (movesFreely ? to.States.FirstOrDefault(s => s.Category == category) : null) ?? to.States[0];
    }

    private static bool CanKeep(Workflow to, WorkflowState kept, ItemEntity item, string? role, bool canTransition)
    {
        if (to.Transitions is not { } transitions || to.States[0].Key == kept.Key)
        {
            return true;
        }

        return Reachable(to.States[0].Key, transitions, item, role).Contains(kept.Key)
            && (canTransition || EveryEntryAllowed(transitions, kept, item, role));
    }

    /// <summary>從起點出發，只走呼叫者符合的轉換，走得到的狀態；流程可以有環，走過的不再走。</summary>
    private static HashSet<string> Reachable(string start, IReadOnlyList<WorkflowTransition> transitions, ItemEntity item, string? role)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var pending = new Queue<string>([start]);
        while (pending.TryDequeue(out var state))
        {
            foreach (var transition in transitions.Where(t => t.From == state && IsAllowed(t, item, role)))
            {
                if (seen.Add(transition.To))
                {
                    pending.Enqueue(transition.To);
                }
            }
        }

        return seen;
    }

    /// <summary>進入這個狀態的轉換至少要有一條，而且每一條都要呼叫者符合。</summary>
    private static bool EveryEntryAllowed(IReadOnlyList<WorkflowTransition> transitions, WorkflowState kept, ItemEntity item, string? role)
    {
        var entries = transitions.Where(t => t.To == kept.Key).ToArray();
        return entries.Length > 0 && entries.All(t => IsAllowed(t, item, role));
    }

    private static bool IsAllowed(WorkflowTransition transition, ItemEntity item, string? role) =>
        (transition.AllowedRoles.Count == 0 || (role is not null && transition.AllowedRoles.Contains(role)))
        && TransitionGuard.MissingFields(item, transition.RequireFields).Count == 0;

    /// <summary>新類型沒有、項目上卻有值的欄位，依舊類型裡欄位的順序，和畫面上看到的順序一樣。</summary>
    private static IEnumerable<string> DetachedFields(ItemType current, ItemType next, IReadOnlyDictionary<string, JsonElement> values)
    {
        var ordered = current.Fields.Where(values.ContainsKey);
        var others = values.Keys.Except(current.Fields, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return ordered.Concat(others).Where(key => !next.Fields.Contains(key));
    }

    // 項目上的值都屬於它的類型：拿掉欄位時套用變更集會一起封存。找不到定義表示資料和藍圖對不上，是要修的錯。
    private static FieldDefinition Definition(ProjectSchema schema, ItemEntity item, string key) =>
        schema.Fields.GetValueOrDefault(key)
            ?? throw new InvalidOperationException($"項目 {item.Id} 有欄位 {key} 的值，但它不在專案目前的藍圖裡。");
}
