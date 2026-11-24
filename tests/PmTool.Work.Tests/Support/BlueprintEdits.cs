using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests;

/// <summary>寫變更集時常用的藍圖改法。每一個都回傳新的藍圖，不改原本的。</summary>
internal static class BlueprintEdits
{
    public static ProjectBlueprint Workflow(this ProjectBlueprint blueprint, string key, Func<Workflow, Workflow> change) =>
        blueprint with { Workflows = [.. blueprint.Workflows.Select(w => w.Key == key ? change(w) : w)] };

    public static ProjectBlueprint State(this ProjectBlueprint blueprint, string workflow, string key, Func<WorkflowState, WorkflowState> change) =>
        blueprint.Workflow(workflow, w => w with { States = [.. w.States.Select(s => s.Key == key ? change(s) : s)] });

    /// <summary>拿掉一個狀態，連同進出它的轉換。</summary>
    public static ProjectBlueprint WithoutState(this ProjectBlueprint blueprint, string workflow, string key) =>
        blueprint.Workflow(workflow, w => w with
        {
            States = [.. w.States.Where(s => s.Key != key)],
            Transitions = w.Transitions is null ? null : [.. w.Transitions.Where(t => t.From != key && t.To != key)],
        });

    public static ProjectBlueprint Field(this ProjectBlueprint blueprint, string key, Func<FieldDefinition, FieldDefinition> change) =>
        blueprint with { Fields = [.. blueprint.Fields.Select(f => f.Key == key ? change(f) : f)] };

    /// <summary>拿掉一個欄位，連同每個類型的欄位清單裡的它。</summary>
    public static ProjectBlueprint WithoutField(this ProjectBlueprint blueprint, string key) =>
        blueprint with
        {
            Fields = [.. blueprint.Fields.Where(f => f.Key != key)],
            ItemTypes = [.. blueprint.ItemTypes.Select(t => t with { Fields = [.. t.Fields.Where(f => f != key)] })],
        };

    public static ProjectBlueprint ItemType(this ProjectBlueprint blueprint, string key, Func<ItemType, ItemType> change) =>
        blueprint with { ItemTypes = [.. blueprint.ItemTypes.Select(t => t.Key == key ? change(t) : t)] };

    public static MigrationHints Moves(params (string Workflow, string From, string To)[] moves) =>
        new() { MoveStates = [.. moves.Select(m => new StateMove { Workflow = m.Workflow, From = m.From, To = m.To })] };

    public static MigrationHints Drops(params string[] fields) => new() { DropFields = fields };

    public static MigrationHints Converts(string field, ConversionFailurePolicy onFailure) =>
        new() { ConvertFields = [new FieldConversion { Field = field, OnFailure = onFailure }] };
}
