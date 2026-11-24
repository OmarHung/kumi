using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Tests.Schema;

/// <summary>
/// instance 的兩種來源。Json：JsonNode.Parse 的結果，純量背後是 JsonElement。
/// Clr：模仿讀 YAML 的做法，純量用 CLR 的 string、bool、long、double 建立。
/// 兩種來源對 TryGetValue 的反應不同，同一個測試要兩種都跑過。
/// </summary>
public enum InstanceSource
{
    Json,
    Clr,
}

internal static class Instances
{
    public static JsonNode? From(string json, InstanceSource source)
    {
        var parsed = JsonNode.Parse(json);
        return source == InstanceSource.Json ? parsed : ToClr(parsed);
    }

    private static JsonNode? ToClr(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => new JsonObject(obj.Select(property => KeyValuePair.Create(property.Key, ToClr(property.Value)))),
        JsonArray array => new JsonArray([.. array.Select(ToClr)]),
        JsonValue value => ToClrValue(value),
        _ => throw new ArgumentOutOfRangeException(nameof(node)),
    };

    // 整數樣式讀成 long，其他數字讀成 double，和讀 YAML 的轉換規則一致。
    private static JsonNode? ToClrValue(JsonValue value) => value.GetValueKind() switch
    {
        JsonValueKind.String => JsonValue.Create(value.GetValue<string>()),
        JsonValueKind.True => JsonValue.Create(true),
        JsonValueKind.False => JsonValue.Create(false),
        JsonValueKind.Number when value.TryGetValue<long>(out var integer) => JsonValue.Create(integer),
        JsonValueKind.Number => JsonValue.Create(value.GetValue<double>()),
        var kind => throw new ArgumentOutOfRangeException(nameof(value), kind, null),
    };
}

internal static class SourceCases
{
    /// <summary>把每一組參數各配上兩種 instance 來源，參數的最後一個是來源。</summary>
    public static IEnumerable<TestCaseData> WithEachSource(params object?[][] cases) =>
        from arguments in cases
        from source in Enum.GetValues<InstanceSource>()
        select new TestCaseData([.. arguments, source]);
}
