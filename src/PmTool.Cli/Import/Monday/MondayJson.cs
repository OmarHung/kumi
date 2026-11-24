using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday;

/// <summary>讀 monday 的 JSON 用的小工具：monday 的編號有時是字串、有時是數字，缺的欄位給 null，不丟例外。</summary>
internal static class MondayJson
{
    public static string? Text(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.GetValueKind() == JsonValueKind.Number => value.ToJsonString(),
        _ => null,
    };

    public static long? Long(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<long>(out var number) => number,
        JsonValue value when value.TryGetValue<string>(out var text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    public static DateTimeOffset? Date(JsonNode? node) =>
        Text(node) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    public static IReadOnlyList<JsonNode> Items(JsonNode? node) =>
        node is JsonArray list ? [.. list.Where(item => item is not null).Select(item => item!)] : [];

    public static JsonNode? Get(JsonNode? node, string name) => node is JsonObject obj ? obj[name] : null;
}
