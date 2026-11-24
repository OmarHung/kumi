using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Validation;

/// <summary>
/// 語意規則共用的查表。key 可能重複：一律取第一個出現的，重複本身由 key 規則回報，
/// 所以這裡不能用遇到重複就丟例外的寫法。建表只走一次文件，之後的查詢都是雜湊。
/// </summary>
internal sealed class BlueprintIndex
{
    private static readonly IReadOnlyList<ItemType> NoItemTypes = [];

    private readonly IReadOnlyList<string> _workflowKeys;
    private readonly IReadOnlyList<IReadOnlySet<string>> _statesByPosition;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ItemType>> _itemTypesByWorkflow;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _fieldsByItemType;

    private BlueprintIndex(ProjectBlueprint blueprint)
    {
        Fields = FirstByKey(blueprint.Fields, f => f.Key);
        WorkflowPositions = KeyPositions.First(blueprint.Workflows, w => w.Key);
        ItemTypePositions = KeyPositions.First(blueprint.ItemTypes, t => t.Key);
        CustomRoles = blueprint.Roles.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        Connectors = blueprint.Bindings.Select(b => b.Connector).ToHashSet();

        _workflowKeys = blueprint.Workflows.Select(w => w.Key).ToArray();
        _statesByPosition = blueprint.Workflows
            .Select(w => (IReadOnlySet<string>)w.States.Select(s => s.Key).ToHashSet(StringComparer.Ordinal))
            .ToArray();
        AllStates = _statesByPosition.SelectMany(states => states).ToHashSet(StringComparer.Ordinal);

        var firstItemTypes = blueprint.ItemTypes.Where((t, i) => ItemTypePositions[t.Key] == i).ToArray();
        _itemTypesByWorkflow = firstItemTypes
            .GroupBy(t => t.Workflow, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ItemType>)g.ToArray(), StringComparer.Ordinal);
        _fieldsByItemType = firstItemTypes.ToDictionary(
            t => t.Key, t => (IReadOnlySet<string>)t.Fields.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>自訂欄位，依 key。</summary>
    public IReadOnlyDictionary<string, FieldDefinition> Fields { get; }

    /// <summary>每個流程 key 第一次出現在 workflows 裡的位置。</summary>
    public IReadOnlyDictionary<string, int> WorkflowPositions { get; }

    /// <summary>每個類型 key 第一次出現在 itemTypes 裡的位置。</summary>
    public IReadOnlyDictionary<string, int> ItemTypePositions { get; }

    /// <summary>所有流程的狀態 key 合在一起。檢視與自動化引用的狀態只要屬於某個流程就算存在。</summary>
    public IReadOnlySet<string> AllStates { get; }

    public IReadOnlySet<string> CustomRoles { get; }

    public IReadOnlySet<ConnectorType> Connectors { get; }

    public static BlueprintIndex Build(ProjectBlueprint blueprint) => new(blueprint);

    /// <summary>核心欄位或自訂欄位。</summary>
    public bool IsField(string key) => BlueprintVocabulary.CoreFields.Contains(key) || Fields.ContainsKey(key);

    /// <summary>內建角色或自訂角色。</summary>
    public bool IsRole(string key) => BlueprintVocabulary.BuiltInRoles.Contains(key) || CustomRoles.Contains(key);

    public bool IsItemType(string key) => ItemTypePositions.ContainsKey(key);

    /// <summary>文件裡第 position 個流程自己的狀態。重複 key 的流程也有自己的一份，規則才能檢查它本身的內容。</summary>
    public IReadOnlySet<string> StatesOfWorkflowAt(int position) => _statesByPosition[position];

    /// <summary>依 key 找流程的狀態；重複的 key 取第一個。流程不存在時回傳 null。</summary>
    public IReadOnlySet<string>? StatesOfWorkflow(string key) =>
        WorkflowPositions.TryGetValue(key, out var position) ? _statesByPosition[position] : null;

    /// <summary>
    /// 使用文件裡第 position 個流程的類型，依文件順序。類型用 key 引用流程，重複的 key 指到第一個，
    /// 所以重複出現的那個流程沒有類型在用。
    /// </summary>
    public IReadOnlyList<ItemType> ItemTypesUsingWorkflowAt(int position)
    {
        var key = _workflowKeys[position];
        return WorkflowPositions[key] == position && _itemTypesByWorkflow.TryGetValue(key, out var types)
            ? types
            : NoItemTypes;
    }

    /// <summary>類型（取第一個同 key 的）有沒有列這個自訂欄位。</summary>
    public bool ItemTypeHasField(ItemType itemType, string field) =>
        _fieldsByItemType.TryGetValue(itemType.Key, out var fields) && fields.Contains(field);

    private static Dictionary<string, T> FirstByKey<T>(IReadOnlyList<T> items, Func<T, string> keyOf)
    {
        var byKey = new Dictionary<string, T>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            byKey.TryAdd(keyOf(item), item);
        }

        return byKey;
    }
}
