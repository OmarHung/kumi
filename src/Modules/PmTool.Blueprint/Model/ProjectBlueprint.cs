namespace PmTool.Blueprint.Model;

/// <summary>專案藍圖：描述一個專案怎麼運作的設定。資源都是平的清單，彼此用 key 引用。</summary>
public sealed record ProjectBlueprint
{
    public required string ApiVersion { get; init; }
    public required string Kind { get; init; }
    public required BlueprintMetadata Metadata { get; init; }
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }
    public required IReadOnlyList<Workflow> Workflows { get; init; }
    public required IReadOnlyList<ItemType> ItemTypes { get; init; }
    public IReadOnlyList<ViewDefinition> Views { get; init; } = [];
    public IReadOnlyList<Automation> Automations { get; init; } = [];
    public IReadOnlyList<RoleDefinition> Roles { get; init; } = [];
    public IReadOnlyList<IntegrationBinding> Bindings { get; init; } = [];
}

public sealed record BlueprintMetadata
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public TemplateReference? BasedOn { get; init; }
}

public sealed record TemplateReference
{
    public required string Template { get; init; }
    public required string Version { get; init; }
}
