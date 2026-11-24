using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace PmTool.Ai.Assistant;

/// <summary>助理存進資料庫與回給模型的 JSON 一律用同一套寫法。</summary>
internal static class AssistantJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // 存在 jsonb 的欄位讀出來時屬性順序被資料庫重排過（短的鍵在前），type 不一定是第一個。
        AllowOutOfOrderMetadataProperties = true,

        // 中文照原樣寫，不要跳脫成 \uXXXX：存的內容與模型讀到的工具結果都比較短、也讀得懂。
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new NulFreeStringConverter(),
        },
    };

    private const string NulEscape = "\\u0000";

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>
    /// 模型給的工具參數是原樣的 JSON，字串裡可能有 NUL：資料庫的 jsonb 不收。大部分情況沒有，所以先看一眼原文，
    /// 有才重新走一遍把字串裡的 NUL 拿掉。
    /// </summary>
    public static JsonElement WithoutNul(JsonElement element)
    {
        if (!element.GetRawText().Contains(NulEscape, StringComparison.OrdinalIgnoreCase))
        {
            return element;
        }

        var node = JsonNode.Parse(element.GetRawText());
        return JsonSerializer.SerializeToElement(Clean(node), Options);
    }

    private static JsonNode? Clean(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var cleaned = new JsonObject();
                foreach (var (name, value) in obj)
                {
                    cleaned[name.Replace("\0", string.Empty, StringComparison.Ordinal)] = Clean(value);
                }

                return cleaned;
            case JsonArray array:
                return new JsonArray([.. array.Select(Clean)]);
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(text.Replace("\0", string.Empty, StringComparison.Ordinal));
            default:
                return node?.DeepClone();
        }
    }

    // 寫出去的字串一律拿掉 NUL（資料庫的 jsonb 不收）。
    private sealed class NulFreeStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Contains('\0', StringComparison.Ordinal) ? value.Replace("\0", string.Empty, StringComparison.Ordinal) : value);
    }
}
