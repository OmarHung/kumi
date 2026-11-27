namespace PmTool.Ai.Items;

/// <summary>修改建議裡欄位的名稱（<see cref="ItemFieldChange.Field"/>）。</summary>
internal static class ItemFieldNames
{
    public const string Title = "title";

    public const string Description = "description";

    public const string Status = "status";

    public const string Assignee = "assignee";

    public const string Priority = "priority";

    public const string StartDate = "startDate";

    public const string DueDate = "dueDate";

    public const string Estimate = "estimate";

    /// <summary>類型的 key；套用時先改類型（<c>ChangeTypeAsync</c>），再改其他欄位。</summary>
    public const string Type = "type";

    /// <summary>上層的項目編號；null 是移到最上層。</summary>
    public const string ParentKey = "parentKey";

    /// <summary>週期的 id；null 是清掉。</summary>
    public const string CycleId = "cycleId";

    /// <summary>里程碑的 id；null 是清掉。</summary>
    public const string MilestoneId = "milestoneId";

    public const string CustomPrefix = "fields.";
}
