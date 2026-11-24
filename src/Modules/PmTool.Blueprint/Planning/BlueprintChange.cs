namespace PmTool.Blueprint.Planning;

public enum ChangeKind
{
    Added,
    Removed,
    Modified,
}

public enum ResourceKind
{
    Blueprint,
    Metadata,
    Field,
    FieldOption,
    Workflow,
    State,
    Transition,
    ItemType,
    View,
    Automation,
    Role,
    Binding,
}

/// <summary>
/// 一個資源的差異。資源用 key 對應，所以改顯示名稱是「修改」，換 key 則是一個移除加一個新增。
/// </summary>
public sealed record BlueprintChange
{
    public required ChangeKind Kind { get; init; }

    public required ResourceKind Resource { get; init; }

    /// <summary>資源的 key。轉換沒有 key，用「起點->終點」；整合綁定用「連接器:資源」。</summary>
    public required string Key { get; init; }

    /// <summary>巢狀資源所屬資源的 key：選項屬於欄位，狀態與轉換屬於流程。最上層的資源是 null。</summary>
    public string? ParentKey { get; init; }

    /// <summary>在目前藍圖裡的位置（JSON Pointer，以藍圖為根）。新增的資源是 null。</summary>
    public string? CurrentPath { get; init; }

    /// <summary>在新藍圖裡的位置（JSON Pointer，以藍圖為根）。被移除的資源是 null。</summary>
    public string? NewPath { get; init; }

    /// <summary>
    /// 修改了哪些屬性，用文件裡的屬性名稱。清單型的屬性（例如流程的 states）出現在這裡，
    /// 表示兩版都有的項目順序變了；清單裡多了誰、少了誰，另外各有一筆差異。
    /// </summary>
    public IReadOnlyList<string> ChangedProperties { get; init; } = [];
}
