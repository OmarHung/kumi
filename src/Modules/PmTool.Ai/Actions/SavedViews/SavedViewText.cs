using PmTool.Work;

namespace PmTool.Ai.Actions.SavedViews;

/// <summary>把一個檢視寫成卡片看得懂的幾行字（之前、之後各一份，卡片逐行比對）。</summary>
internal static class SavedViewText
{
    public static string LayoutName(SavedViewLayout layout) => layout switch
    {
        SavedViewLayout.Table => "表格",
        SavedViewLayout.Board => "看板",
        SavedViewLayout.Calendar => "月曆",
        _ => "時間軸",
    };

    public static string Describe(string name, SavedViewLayout layout, SavedViewQuery query)
    {
        var lines = new List<string> { $"名稱：{name}", $"版面：{LayoutName(layout)}" };
        Add(lines, "類型", query.Type);
        Add(lines, "狀態", query.State);
        Add(lines, "狀態類別", query.Category);
        Add(lines, "負責人", query.Assignee);
        Add(lines, "建立者", query.CreatedBy);
        Add(lines, "專案", query.Project);
        Add(lines, "範圍", query.Mine);
        Add(lines, "搜尋", query.Search);
        Add(lines, "到期", query.Due);
        Add(lines, "優先順序", query.Priority);
        Add(lines, "週期", query.Cycle);
        Add(lines, "里程碑", query.Milestone);
        Add(lines, "分組", query.Group);
        Add(lines, "排序", query.Sort);
        if (query.Open == true)
        {
            lines.Add("只看還沒結束的");
        }

        if (query.Stale == true)
        {
            lines.Add("只看很久沒更新的");
        }

        foreach (var (key, value) in (query.Fields ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            lines.Add($"欄位 {key}：{value}");
        }

        return string.Join('\n', lines);
    }

    private static void Add(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            lines.Add($"{label}：{value}");
        }
    }
}
