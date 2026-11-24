using System.Collections.Frozen;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Blueprints;

/// <summary>
/// 一版藍圖加上用 key 查資源的索引。藍圖已經通過驗證，key 不會重複、引用都存在，所以直接建字典。
/// 建一次就快取起來，每個請求都用同一份。
/// </summary>
internal sealed record ProjectSchema
{
    // 狀態排序用：先依類別、再依它在流程裡的位置。一個流程最多 30 個狀態，乘上 1000 不會和下一個類別重疊。
    private const int CategoryWeight = 1_000;

    public required ProjectBlueprint Blueprint { get; init; }

    public required FrozenDictionary<string, ItemType> ItemTypes { get; init; }

    public required FrozenDictionary<string, FieldDefinition> Fields { get; init; }

    public required FrozenDictionary<string, Workflow> Workflows { get; init; }

    public required FrozenDictionary<string, RoleDefinition> Roles { get; init; }

    /// <summary>狀態的排序值。不同流程有同一個 key 的狀態時取比較前面的那一個。</summary>
    public required FrozenDictionary<string, int> StateRanks { get; init; }

    public static ProjectSchema From(ProjectBlueprint blueprint) => new()
    {
        Blueprint = blueprint,
        ItemTypes = blueprint.ItemTypes.ToFrozenDictionary(t => t.Key, StringComparer.Ordinal),
        Fields = blueprint.Fields.ToFrozenDictionary(f => f.Key, StringComparer.Ordinal),
        Workflows = blueprint.Workflows.ToFrozenDictionary(w => w.Key, StringComparer.Ordinal),
        Roles = blueprint.Roles.ToFrozenDictionary(r => r.Key, StringComparer.Ordinal),
        StateRanks = RankStates(blueprint.Workflows),
    };

    public Workflow WorkflowOf(ItemType itemType) => Workflows[itemType.Workflow];

    public WorkflowState? StateOf(ItemType itemType, string state) =>
        WorkflowOf(itemType).States.FirstOrDefault(s => s.Key == state);

    private static FrozenDictionary<string, int> RankStates(IReadOnlyList<Workflow> workflows) =>
        workflows
            .SelectMany(w => w.States.Select((state, position) => (state.Key, Rank: ((int)state.Category * CategoryWeight) + position)))
            .GroupBy(s => s.Key, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.Min(s => s.Rank), StringComparer.Ordinal);
}
