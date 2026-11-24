namespace PmTool.Blueprint.Model;

public enum ViewType
{
    Table,
    Board,
}

public enum SortDirection
{
    Asc,
    Desc,
}

public enum AssigneeFilter
{
    Me,
    Unassigned,
}

public sealed record ViewDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required ViewType Type { get; init; }
    public string? GroupBy { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<ViewSort> Sort { get; init; } = [];
    public ViewFilter? Filter { get; init; }

    /// <summary>null 表示專案成員都看得到。</summary>
    public IReadOnlyList<string>? VisibleTo { get; init; }
}

public sealed record ViewSort
{
    public required string Field { get; init; }
    public required SortDirection Direction { get; init; }
}

public sealed record ViewFilter
{
    public IReadOnlyList<string> ItemTypes { get; init; } = [];
    public IReadOnlyList<string> States { get; init; } = [];
    public IReadOnlyList<StateCategory> StateCategories { get; init; } = [];
    public AssigneeFilter? Assignee { get; init; }
}
