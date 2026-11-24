using System.Text.Json;
using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;
using PmTool.Work.ChangeSets;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 復原之後項目的樣子，以及要記進活動與封存表的內容。純計算，不碰資料庫；上層是不是刪除、類型還在不在，在這之前已經檢查過。
/// 刪除的項目不跟著變更集搬移，所以復原時才照現在的藍圖整理：狀態、自訂欄位、上層。
/// </summary>
/// <param name="Changes">先是封存的自訂欄位（依類型裡欄位的順序），最後是拿掉上層（有的話）。</param>
internal sealed record RestorePlan(
    ItemEntity After, IReadOnlyList<FieldChange> Changes, IReadOnlyList<ArchivedValue> Archived, string? FromState, string? ToState)
{
    /// <param name="parent">項目目前的上層（沒刪除）；沒有上層是 null。</param>
    /// <param name="now">復原的時刻，項目新的 updatedAt。</param>
    public static RestorePlan Make(
        ProjectSchema schema, ItemType type, ItemEntity item, ItemEntity? parent, string projectKey, DateTimeOffset now)
    {
        var state = TargetState(schema.WorkflowOf(type), item);
        var moved = state.Key != item.State;
        var (values, detached) = SplitFields(schema, type, item);
        var changes = detached.Select(d => d.Change).ToList();
        var dropsParent = parent is not null && !type.Parents.Contains(parent.Type);
        if (parent is not null && dropsParent)
        {
            var from = JsonSerializer.SerializeToElement(ItemKeys.Format(projectKey, parent.Number));
            changes.Add(new FieldChange(CoreFields.Parent, from, null));
        }

        var after = item with
        {
            State = state.Key,
            StateCategory = StateCategoryText.ToText(state.Category),
            Fields = detached.Count == 0 ? item.Fields : FieldValues.Serialize(values),
            ParentId = dropsParent ? null : item.ParentId,
            DeletedAt = null,
            DeletedBy = null,
            UpdatedAt = now,
        };
        return new RestorePlan(after, changes, [.. detached.Select(d => d.Archived)], moved ? item.State : null, moved ? state.Key : null);
    }

    // 還在流程裡的狀態照舊（類別取現在的）；已經被拿掉的回到流程的第一個狀態，和新項目的起點一樣：
    // 刪除之後的變更集沒有替它指定搬移的目的地。
    private static WorkflowState TargetState(Workflow workflow, ItemEntity item) =>
        workflow.States.FirstOrDefault(s => s.Key == item.State) ?? workflow.States[0];

    /// <returns>留下的值，與要拿掉封存的值（類型已經沒有的欄位，或值不再符合現在的欄位定義）。</returns>
    private static (Dictionary<string, JsonElement> Kept, List<Detached> Detached) SplitFields(
        ProjectSchema schema, ItemType type, ItemEntity item)
    {
        var values = FieldValues.Parse(item.Fields);
        var ordered = type.Fields.Where(values.ContainsKey)
            .Concat(values.Keys.Except(type.Fields, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var kept = values.ToDictionary(StringComparer.Ordinal);
        var detached = new List<Detached>();
        foreach (var key in ordered)
        {
            var definition = schema.Fields.GetValueOrDefault(key);
            var stillFits = type.Fields.Contains(key) && definition is not null && FieldValueRules.Check(definition, values[key]).Problem is null;
            if (!stillFits)
            {
                detached.Add(Detach(definition, key, values[key]));
                kept.Remove(key);
            }
        }

        return (kept, detached);
    }

    // 欄位定義已經整個不在藍圖裡時不知道它的型別：比單行文字的上限還長的字串當成可能是長文字，活動不記內容（可能很長），其餘照記。
    private static Detached Detach(FieldDefinition? definition, string key, JsonElement value)
    {
        if (definition is not null)
        {
            var (change, archived) = ItemMigrator.Detach(definition, value);
            return new Detached(change, archived);
        }

        var isLong = value.ValueKind == JsonValueKind.String && value.GetString()!.Length > WorkRules.MaxTextFieldLength;
        JsonElement? shown = isLong ? null : value;
        return new Detached(new FieldChange(key, shown, null), new ArchivedValue(key, value));
    }

    private sealed record Detached(FieldChange Change, ArchivedValue Archived);
}
