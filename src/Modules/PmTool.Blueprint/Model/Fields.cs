namespace PmTool.Blueprint.Model;

public enum FieldType
{
    Text,
    LongText,
    Number,
    Currency,
    Date,
    SingleSelect,
    MultiSelect,
    Person,
    Checkbox,
    Url,
}

public enum OptionColor
{
    Gray,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
}

public sealed record FieldDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required FieldType Type { get; init; }
    public string? Description { get; init; }

    /// <summary>null 表示文件沒有寫 options；空清單表示寫了但沒有內容。兩者在驗證時意義不同。</summary>
    public IReadOnlyList<FieldOption>? Options { get; init; }

    public string? Currency { get; init; }

    public bool IsSelect => Type is FieldType.SingleSelect or FieldType.MultiSelect;
}

public sealed record FieldOption
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public OptionColor? Color { get; init; }
}
