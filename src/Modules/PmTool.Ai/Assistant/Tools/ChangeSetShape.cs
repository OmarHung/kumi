using PmTool.Abstractions;
using PmTool.Blueprint.Planning;
using PmTool.Work;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 變更集的差異與資料影響，精簡成給模型讀的形狀（和 API 的 <c>ChangeData</c>／<c>ImpactData</c> 不必相同，讀得懂就好）。
/// 不認得的影響種類寫成 unknown 而不是丟例外：讓模型至少知道有這一項。
/// </summary>
internal static class ChangeSetShape
{
    public static object Change(BlueprintChange change) => new
    {
        kind = change.Kind,
        resource = change.Resource,
        key = change.Key,
        parentKey = change.ParentKey,
        changedProperties = change.ChangedProperties,
    };

    public static object Blocker(FailureDetail blocker) => new { code = blocker.Code, path = blocker.Path, message = blocker.Message };

    public static Dictionary<string, object?> Impact(PlannedImpact planned)
    {
        var shape = new Dictionary<string, object?>
        {
            ["affected"] = planned.Affected,
            ["blocksApply"] = planned.BlocksApply,
        };
        foreach (var (name, value) in Describe(planned.Impact))
        {
            shape[name] = value;
        }

        return shape;
    }

    private static IEnumerable<KeyValuePair<string, object?>> Describe(DataImpact impact) => impact switch
    {
        StateMoveImpact move => Of("state_move", ("workflow", move.Workflow), ("from", move.From), ("to", move.To)),
        FieldDropImpact drop => Of("field_drop", ("field", drop.Field)),
        FieldConversionImpact conversion => Of(
            "field_conversion",
            ("field", conversion.Field), ("fromType", conversion.From), ("toType", conversion.To), ("onFailure", conversion.OnFailure)),
        ItemTypeRemovalImpact removal => Of("item_type_removal", ("itemType", removal.ItemType)),
        ParentRemovalImpact parent => Of("parent_removal", ("itemType", parent.ItemType), ("parent", parent.Parent)),
        RoleRemovalImpact role => Of("role_removal", ("role", role.Role)),
        FieldDetachImpact detach => Of("field_detach", ("itemType", detach.ItemType), ("field", detach.Field)),
        _ => Of("unknown"),
    };

    private static IEnumerable<KeyValuePair<string, object?>> Of(string kind, params (string Name, object? Value)[] properties) =>
        [new("kind", kind), .. properties.Select(p => new KeyValuePair<string, object?>(p.Name, p.Value))];
}
