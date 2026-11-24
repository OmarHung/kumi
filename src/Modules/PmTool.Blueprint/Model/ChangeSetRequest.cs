namespace PmTool.Blueprint.Model;

public enum ConversionFailurePolicy
{
    LeaveEmpty,
    Abort,
}

/// <summary>四個入口送出的共同單位：想要的藍圖全文，加上資料搬移的指示。差異由伺服器對照 BaseVersion 算出來。</summary>
public sealed record ChangeSetRequest
{
    public required long BaseVersion { get; init; }
    public string? Summary { get; init; }
    public required ProjectBlueprint Blueprint { get; init; }
    public MigrationHints? Migrations { get; init; }
}

public sealed record MigrationHints
{
    public IReadOnlyList<StateMove> MoveStates { get; init; } = [];
    public IReadOnlyList<string> DropFields { get; init; } = [];
    public IReadOnlyList<FieldConversion> ConvertFields { get; init; } = [];
}

public sealed record StateMove
{
    public required string Workflow { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
}

public sealed record FieldConversion
{
    public required string Field { get; init; }
    public required ConversionFailurePolicy OnFailure { get; init; }
}
