namespace PmTool.Blueprint.Model;

public enum StateCategory
{
    Backlog,
    NotStarted,
    InProgress,
    Done,
    Canceled,
}

public sealed record Workflow
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<WorkflowState> States { get; init; }

    /// <summary>null 表示任何兩個狀態之間都可以互相轉換；有清單時只允許列出的轉換。</summary>
    public IReadOnlyList<WorkflowTransition>? Transitions { get; init; }
}

public sealed record WorkflowState
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required StateCategory Category { get; init; }

    public bool IsTerminal => Category is StateCategory.Done or StateCategory.Canceled;
}

public sealed record WorkflowTransition
{
    public required string From { get; init; }
    public required string To { get; init; }
    public IReadOnlyList<string> AllowedRoles { get; init; } = [];
    public IReadOnlyList<string> RequireFields { get; init; } = [];
}
