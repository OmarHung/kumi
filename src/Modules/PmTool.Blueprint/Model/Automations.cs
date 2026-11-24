namespace PmTool.Blueprint.Model;

public enum TriggerSource
{
    Item,
    Gitlab,
    Github,
    Schedule,
    Webhook,
}

public enum ActionType
{
    Transition,
    SetField,
    Notify,
    Webhook,
}

public sealed record Automation
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public required AutomationTrigger Trigger { get; init; }
    public IReadOnlyList<AutomationCondition> Conditions { get; init; } = [];
    public required IReadOnlyList<AutomationAction> Actions { get; init; }
}

public sealed record AutomationTrigger
{
    public required TriggerSource Source { get; init; }
    public required string Event { get; init; }
}

public sealed record AutomationCondition
{
    public required string Field { get; init; }
    public required IReadOnlyList<string> In { get; init; }
}

public sealed record AutomationAction
{
    public required ActionType Type { get; init; }
    public string? To { get; init; }
    public string? Field { get; init; }
    public string? Value { get; init; }
    public string? Message { get; init; }
    public string? Url { get; init; }
}
