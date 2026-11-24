namespace PmTool.Blueprint.Model;

public sealed record RoleDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
}
