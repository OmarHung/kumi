using System.Text.Json;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Work.Items;

namespace PmTool.Work.ChangeSets;

/// <summary>
/// 套用時要搬移的項目，只有搬移用得到的欄位。一次最多幾千個項目，說明與其他欄位可能很長、也用不到，不讀。
/// </summary>
/// <param name="Fields">自訂欄位的值（jsonb 的文字）。</param>
internal sealed record MigratingItem(
    Guid Id, Guid OrganizationId, Guid ProjectId, string Type, string State, string StateCategory, string Fields, DateTimeOffset UpdatedAt);

/// <summary>從項目上拿掉、要存進封存表的一個值。</summary>
internal sealed record ArchivedValue(string Field, JsonElement Value);

/// <param name="Changes">欄位的前後值，依計畫裡資料影響的順序；形狀見 <see cref="FieldChange"/>。</param>
internal sealed record ItemMigration(MigratingItem Before, MigratingItem After, IReadOnlyList<FieldChange> Changes, IReadOnlyList<ArchivedValue> Archived)
{
    public bool StateMoved => Before.State != After.State;

    /// <summary>狀態或欄位有變才留活動。只有類別跟著藍圖變的，項目本身的資料沒有變。</summary>
    public bool IsRecorded => StateMoved || Changes.Count > 0;
}

/// <summary>一個項目在新藍圖底下的樣子。純計算，不碰資料庫；擋住套用的情況在這之前已經檢查過。</summary>
internal static class ItemMigrator
{
    /// <param name="now">套用的時刻；有留活動的項目，updatedAt 也是它。</param>
    public static ItemMigration Migrate(MigratingItem item, MigrationScope scope, DateTimeOffset now)
    {
        var state = TargetState(item, scope);
        var outcomes = FieldOutcomes(item, scope);
        var recorded = state.Key != item.State || outcomes.Length > 0;
        var after = item with
        {
            State = state.Key,
            StateCategory = StateCategoryText.ToText(state.Category),
            Fields = outcomes.Length == 0
                ? item.Fields
                : FieldValues.Serialize(FieldValues.Merge(FieldValues.Parse(item.Fields), outcomes.ToDictionary(o => o.Field, o => o.Value))),
            UpdatedAt = recorded ? now : item.UpdatedAt,
        };
        return new ItemMigration(item, after, [.. outcomes.Select(o => o.Change)], [.. outcomes.Select(o => o.Archived).OfType<ArchivedValue>()]);
    }

    /// <summary>還在新流程裡的狀態照舊；被拿掉的照計畫搬。類型與流程換了也一樣，類別一律取新藍圖的。</summary>
    private static WorkflowState TargetState(MigratingItem item, MigrationScope scope)
    {
        // 類型被拿掉、狀態沒有搬移指示，都表示資料和藍圖對不上（套用前已經擋下被拿掉類型底下的項目），是要修的錯。
        var type = scope.Next.ItemTypes.GetValueOrDefault(item.Type)
            ?? throw new InvalidOperationException($"項目 {item.Id} 的類型 {item.Type} 不在新的藍圖裡。");
        var workflow = scope.Next.WorkflowOf(type);
        var to = workflow.States.Any(s => s.Key == item.State)
            ? item.State
            : scope.Moves.FirstOrDefault(m => m.Workflow == type.Workflow && m.From == item.State)?.To
                ?? throw new InvalidOperationException($"項目 {item.Id} 停在 {item.State}，新的流程 {type.Workflow} 沒有這個狀態，也沒有搬移指示。");
        return workflow.States.First(s => s.Key == to);
    }

    private static FieldOutcome[] FieldOutcomes(MigratingItem item, MigrationScope scope)
    {
        var values = FieldValues.Parse(item.Fields);
        return [.. scope.Impacts.Select(impact => impact switch
        {
            FieldDropImpact drop when values.TryGetValue(drop.Field, out var value) => Dropped(scope.Current.Fields[drop.Field], value),

            // 這個類型不再有這個欄位：值原樣封存，不必轉換，轉不過去也不影響。
            FieldConversionImpact conversion when !scope.IsDetached(item.Type, conversion.Field) && values.TryGetValue(conversion.Field, out var value) =>
                Converted(item, scope, conversion, value),
            FieldDetachImpact detach when detach.ItemType == item.Type && values.TryGetValue(detach.Field, out var value) =>
                Dropped(scope.Current.Fields[detach.Field], value),
            _ => null,
        }).OfType<FieldOutcome>()];
    }

    /// <summary>
    /// 欄位的值從項目上拿掉、原樣封存：活動裡怎麼記、封存表裡存什麼。套用變更集拿掉欄位與改類型共用，兩邊記法一樣。
    /// </summary>
    public static (FieldChange Change, ArchivedValue Archived) Detach(FieldDefinition field, JsonElement value) =>
        (Describe(field.Key, [field.Type], value, null), new ArchivedValue(field.Key, value));

    private static FieldOutcome Dropped(FieldDefinition field, JsonElement value)
    {
        var (change, archived) = Detach(field, value);
        return new(field.Key, null, change, archived);
    }

    private static FieldOutcome Converted(MigratingItem item, MigrationScope scope, FieldConversionImpact conversion, JsonElement value)
    {
        var from = scope.Current.Fields[conversion.Field];
        var to = scope.Next.Fields[conversion.Field];
        if (ValueConversions.Convert(from, to, value) is { } converted)
        {
            return new FieldOutcome(conversion.Field, converted, Describe(conversion.Field, [from.Type, to.Type], value, converted), null);
        }

        // 不會失敗的轉換失敗了、或指定擋下的轉換走到這裡，都表示套用前的檢查漏了，不能默默丟掉資料。
        return conversion.OnFailure == ConversionFailurePolicy.LeaveEmpty
            ? new FieldOutcome(conversion.Field, null, Describe(conversion.Field, [from.Type, to.Type], value, null), new ArchivedValue(conversion.Field, value))
            : throw new InvalidOperationException($"項目 {item.Id} 的欄位 {conversion.Field} 轉換失敗，但套用前沒有被擋下。");
    }

    /// <summary>和修改時的紀錄一樣：長文字只記「有改」，兩邊一律是 null，內容可能很長。</summary>
    private static FieldChange Describe(string field, FieldType[] types, JsonElement from, JsonElement? to) =>
        types.Contains(FieldType.LongText) ? new FieldChange(field, null, null) : new FieldChange(field, from, to);

    /// <param name="Value">新的值；null 表示從項目上拿掉。</param>
    private sealed record FieldOutcome(string Field, JsonElement? Value, FieldChange Change, ArchivedValue? Archived);
}
