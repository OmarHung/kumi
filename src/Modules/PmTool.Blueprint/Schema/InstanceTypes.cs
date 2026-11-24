using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Schema;

/// <summary>
/// 判斷 instance 節點的型別。JSON 來源（背後是 JsonElement）與 YAML 來源（背後是 CLR 值）
/// 對 TryGetValue 的反應不同，所以種類一律看 GetValueKind()，數值交給 <see cref="NumberValue"/>。
/// </summary>
internal static class InstanceTypes
{
    public static bool Matches(JsonNode? node, SchemaType type) => type switch
    {
        SchemaType.Object => node is JsonObject,
        SchemaType.Array => node is JsonArray,
        SchemaType.String => KindOf(node) == JsonValueKind.String,
        SchemaType.Boolean => KindOf(node) is JsonValueKind.True or JsonValueKind.False,
        SchemaType.Number => TryReadNumber(node, out _),
        SchemaType.Integer => TryReadNumber(node, out var number) && number.IsInteger,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不認得的 Schema 型別。"),
    };

    /// <summary>給訊息用的型別名稱，不帶值。</summary>
    public static string Describe(JsonNode? node) => KindOf(node) switch
    {
        JsonValueKind.Object => "物件",
        JsonValueKind.Array => "陣列",
        JsonValueKind.String => "字串",
        JsonValueKind.True or JsonValueKind.False => "布林值",
        JsonValueKind.Number when TryReadNumber(node, out var number) => $"數字 {number}",
        JsonValueKind.Number => "無法表示的數值（NaN 或無限大）",
        _ => "空值（null）",
    };

    /// <summary>給訊息用的值。物件與陣列只寫種類，不把子樹放進訊息（長度沒有上限）。</summary>
    public static string DescribeValue(JsonNode? node) =>
        ScalarValue.TryRead(node, out var scalar) ? scalar.ToMessageText() : Describe(node);

    // JSON 的 null 與 YAML 的 ~ 在 JsonNode 裡都是 C# 的 null。
    private static JsonValueKind KindOf(JsonNode? node) => node?.GetValueKind() ?? JsonValueKind.Null;

    private static bool TryReadNumber(JsonNode? node, out NumberValue number)
    {
        if (node is JsonValue value && value.GetValueKind() == JsonValueKind.Number)
        {
            return NumberValue.TryRead(value, out number);
        }

        number = default;
        return false;
    }
}
