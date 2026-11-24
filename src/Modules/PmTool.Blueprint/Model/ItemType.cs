namespace PmTool.Blueprint.Model;

public sealed record ItemType
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Workflow { get; init; }
    public IReadOnlyList<string> Fields { get; init; } = [];
    public IReadOnlyList<string> Parents { get; init; } = [];
    public string? Icon { get; init; }
}
