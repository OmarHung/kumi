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

    /// <param name="canTransition">呼叫者有沒有 item.transition。</param>
    /// <param name="now">改類型的時刻，項目新的 updatedAt。</param>
    public static TypeChange Plan(
        ProjectSchema schema, ItemType current, ItemType next, ItemEntity item, bool canTransition, DateTimeOffset now)
    {
        var state = TargetState(schema.WorkflowOf(next), item, canTransition);
        var values = FieldValues.Parse(item.Fields);
        var detached = DetachedFields(current, next, values)
            .Select(key => ItemMigrator.Detach(Definition(schema, item, key), values[key]))
            .ToArray();
        var removed = detached.Select(d => d.Archived.Field).ToHashSet(StringComparer.Ordinal);
        var after = item with
        {
            Type = next.Key,
            State = state.Key,
            StateCategory = StateCategoryText.ToText(state.Category),
            Fields = removed.Count == 0
                ? item.Fields
                : FieldValues.Serialize(values.Where(pair => !removed.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal)),
            UpdatedAt = now,
        };
        var typeChange = new FieldChange(CoreFields.ItemType, JsonSerializer.SerializeToElement(current.Key), JsonSerializer.SerializeToElement(next.Key));
        return new TypeChange(item, after, [typeChange, .. detached.Select(d => d.Change)], [.. detached.Select(d => d.Archived)]);
    }

    /// <summary>
    /// 新流程有同 key、同類別的狀態就留著；否則新流程不限制轉換、呼叫者也能改狀態時，搬到第一個同類別的狀態（沒有就是第一個）；
    /// 其他情況回到新流程的第一個狀態，也就是新項目的起點。
    /// 改類型不經過轉換的規則：有限制的流程只能從起點照轉換走，停在中途或終點就跳過了轉換的角色限制與要先填的欄位，
    /// 來回改兩次類型也能走出流程沒有的轉換；不能改狀態的人，本來就不該借改類型挪動狀態。
    /// </summary>
    private static WorkflowState TargetState(Workflow workflow, ItemEntity item, bool canTransition)
    {
        var category = StateCategoryText.Parse(item.StateCategory);
        var kept = workflow.States.FirstOrDefault(s => s.Key == item.State && s.Category == category);
        if (kept is not null)
        {
            return kept;
        }

        var movesFreely = workflow.Transitions is null && canTransition;
        return (movesFreely ? workflow.States.FirstOrDefault(s => s.Category == category) : null) ?? workflow.States[0];
    }

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
