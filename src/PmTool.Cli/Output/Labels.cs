namespace PmTool.Cli.Output;

/// <summary>
/// 給人看的名稱：狀態類別、優先順序、角色。和網頁（web/src/domain/labels.ts）用同樣的中文；
/// 不認得的值（例如自訂角色）原樣顯示。--json 不經過這裡，照舊是伺服器的原始資料。
/// </summary>
internal static class Labels
{
    private static readonly IReadOnlyDictionary<string, string> StateCategories = new Dictionary<string, string>
    {
        ["backlog"] = "待規劃", ["not_started"] = "未開始", ["in_progress"] = "進行中", ["done"] = "完成", ["canceled"] = "已取消",
    };

    private static readonly IReadOnlyDictionary<string, string> Priorities = new Dictionary<string, string>
    {
        ["none"] = "未設定", ["low"] = "低", ["medium"] = "中", ["high"] = "高", ["urgent"] = "緊急",
    };

    private static readonly IReadOnlyDictionary<string, string> OrganizationRoles = new Dictionary<string, string>
    {
        ["owner"] = "擁有者", ["admin"] = "管理員", ["member"] = "成員", ["guest"] = "訪客",
    };

    private static readonly IReadOnlyDictionary<string, string> ProjectRoles = new Dictionary<string, string>
    {
        ["admin"] = "管理員", ["editor"] = "編輯者", ["commenter"] = "留言者", ["viewer"] = "檢視者",
    };

    public static string StateCategory(string? key) => Lookup(StateCategories, key);

    public static string Priority(string? key) => Lookup(Priorities, key);

    public static string OrganizationRole(string? key) => Lookup(OrganizationRoles, key);

    public static string ProjectRole(string? key) => Lookup(ProjectRoles, key);

    private static string Lookup(IReadOnlyDictionary<string, string> names, string? key) =>
        key is null ? "-" : names.GetValueOrDefault(key, key);
}
