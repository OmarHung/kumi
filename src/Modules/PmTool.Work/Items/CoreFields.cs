namespace PmTool.Work.Items;

/// <summary>
/// 核心欄位在文件裡的名字（Details 的 Path、藍圖的 requireFields 都用它）與顯示名稱。
/// </summary>
internal static class CoreFields
{
    public const string Type = "type";
    public const string ItemType = "item_type";
    public const string Title = "title";
    public const string State = "state";
    public const string Assignee = "assignee";
    public const string Priority = "priority";
    public const string StartDate = "start_date";
    public const string DueDate = "due_date";
    public const string Estimate = "estimate";
    public const string Description = "description";

    /// <summary>週期與里程碑不是藍圖的核心欄位，但每個項目都有；活動與事件的欄位名稱用這兩個。</summary>
    public const string Cycle = "cycle";

    public const string Milestone = "milestone";

    /// <summary>上層項目不是藍圖的核心欄位，但新增與修改都會帶，路徑用這個名字。</summary>
    public const string Parent = "parent";

    public static string? DisplayName(string key) => key switch
    {
        Type or ItemType => "類型",
        Title => "標題",
        State => "狀態",
        Assignee => "負責人",
        Priority => "優先順序",
        StartDate => "開始日",
        DueDate => "到期日",
        Estimate => "估計",
        Description => "說明",
        Parent => "上層項目",
        Cycle => "週期",
        Milestone => "里程碑",
        _ => null,
    };
}
