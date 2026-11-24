using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Schema;

/// <summary>
/// enum 與 const 能用的值：字串、有限的數值、布林。相等就是 record 的相等：
/// 種類相同，而且字串逐字相同、數值相等（見 <see cref="NumberValue"/>）。
/// </summary>
/// <remarks>
/// 載入時就把 enum 與 const 限制成純量：比較規則簡單，也符合拿去約束語言模型輸出的用途。
/// </remarks>
internal readonly record struct ScalarValue
{
    private ScalarValue(JsonValueKind kind, string? text, NumberValue number)
    {
        Kind = kind;
        Text = text;
        Number = number;
    }

    /// <summary>String、Number、True、False 其中一種。</summary>
    public JsonValueKind Kind { get; }

    public string? Text { get; }

    public NumberValue Number { get; }

    public static bool TryRead(JsonElement element, out ScalarValue scalar)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                scalar = OfString(element.GetString()!);
                return true;
            case JsonValueKind.Number when NumberValue.TryRead(element, out var number):
                scalar = OfNumber(number);
                return true;
            case JsonValueKind.True or JsonValueKind.False:
                scalar = new ScalarValue(element.ValueKind, text: null, number: default);
                return true;
            default:
                scalar = default;
                return false;
        }
    }

    /// <summary>null、物件、陣列與無法表示的數值（NaN、無限大）都不是純量，讀不出來。</summary>
    public static bool TryRead(JsonNode? node, out ScalarValue scalar)
    {
        if (node is not JsonValue value)
        {
            scalar = default;
            return false;
        }

        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                scalar = OfString(ReadString(value));
                return true;
            case JsonValueKind.Number when NumberValue.TryRead(value, out var number):
                scalar = OfNumber(number);
                return true;
            case JsonValueKind.True or JsonValueKind.False:
                scalar = new ScalarValue(value.GetValueKind(), text: null, number: default);
                return true;
            default:
                scalar = default;
                return false;
        }
    }

    /// <summary>給問題訊息用。字串可能來自文件而且很長，一律經過 <see cref="IssueText.Quote"/>。</summary>
    public string ToMessageText() => Kind switch
    {
        JsonValueKind.String => IssueText.Quote(Text),
        JsonValueKind.Number => Number.ToString(),
        JsonValueKind.True => "true",
        _ => "false",
    };

    private static ScalarValue OfString(string text) => new(JsonValueKind.String, text, number: default);

    private static ScalarValue OfNumber(NumberValue number) => new(JsonValueKind.Number, text: null, number);

    // JSON 來源與 YAML 來源背後都是 string，TryGetValue 拿得到；其他 CLR 型別（例如 Guid）照它的 JSON 文字讀。
    private static string ReadString(JsonValue value) =>
        value.TryGetValue<string>(out var text) ? text : JsonElements.From(value).GetString()!;
}
