using System.Text.Json;

namespace PmTool.Work.Templates;

/// <summary>讀一筆範例項目的形狀；內容對不對交給 <see cref="ExampleChecker"/>。</summary>
internal sealed class ExampleItemReader(string templateKey, int position) : ExampleShapeReader
{
    private static readonly string[] KnownKeys =
        ["ref", "type", "title", "state", "priority", "description", "parent", "startInDays", "dueInDays", "estimate", "cycle", "milestone", "fields"];

    private static readonly Dictionary<string, Priority> Priorities =
        Enum.GetValues<Priority>().ToDictionary(p => p.ToString().ToLowerInvariant(), StringComparer.Ordinal);

    public ExampleItem? Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Add("(這一筆)", "每一筆要是一組欄位。");
            return null;
        }

        RejectUnknownKeys(element, KnownKeys);

        var type = Text(element, "type", required: true);
        var title = Text(element, "title", required: true);
        var priority = ReadPriority(element);
        var item = new ExampleItem(
            position,
            Text(element, "ref", required: false),
            type ?? string.Empty,
            title ?? string.Empty,
            Text(element, "state", required: false),
            priority,
            Text(element, "description", required: false),
            Text(element, "parent", required: false),
            Days(element, "startInDays"),
            Days(element, "dueInDays"),
            Fields(element),
            Estimate(element),
            Text(element, "cycle", required: false),
            Text(element, "milestone", required: false));
        return Problems.Count == 0 ? item : null;
    }

    private Priority ReadPriority(JsonElement element)
    {
        var text = Text(element, "priority", required: false);
        if (text is null)
        {
            return Priority.None;
        }

        if (Priorities.TryGetValue(text, out var priority))
        {
            return priority;
        }

        Add("priority", $"優先度只能是 {string.Join("、", Priorities.Keys)}，現在是「{text}」。");
        return Priority.None;
    }

    private decimal? Estimate(JsonElement element)
    {
        if (!element.TryGetProperty("estimate", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var estimate))
        {
            return estimate;
        }

        Add("estimate", "要寫成數字。");
        return null;
    }

    private IReadOnlyDictionary<string, JsonElement> Fields(JsonElement element)
    {
        if (!element.TryGetProperty("fields", out var fields) || fields.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, JsonElement>();
        }

        if (fields.ValueKind != JsonValueKind.Object)
        {
            Add("fields", "要是一組欄位的值。");
            return new Dictionary<string, JsonElement>();
        }

        return fields.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }

    protected override string Describe(string path, string message) => ExampleProblems.Item(templateKey, position, path, message);
}
