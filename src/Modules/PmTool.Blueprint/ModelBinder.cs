using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint;

/// <summary>把通過結構檢查的文件轉成型別化的模型。列舉在文件裡是 snake_case（例如 single_select）。</summary>
internal static class ModelBinder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new IntegerValuedInt64Converter(),
        },
    };

    public static T Bind<T>(JsonNode node) where T : class =>
        node.Deserialize<T>(Options)
            ?? throw new InvalidOperationException("通過結構檢查的文件不應該是 null。");

    /// <summary>
    /// 結構檢查把「沒有小數部分、落在 Int64 範圍內的數字」都算成 integer，所以 12.0、1.2e1 也會走到這裡，
    /// 而內建的轉換器只讀得進 12 這種寫法。判斷交給 <see cref="NumberValue"/>，和結構檢查用同一套定義，
    /// 才不會出現「檢查通過、綁定卻丟例外」的值。
    /// </summary>
    private sealed class IntegerValuedInt64Converter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                var element = JsonElement.ParseValue(ref reader);
                if (NumberValue.TryRead(element, out var exact) && exact.IsInteger)
                {
                    return exact.Integer;
                }

                // YAML 的浮點寫法在樹裡是 double，寫成 JSON 文字時用的是最短的表示法。-2^63 會寫成
                // -9.223372036854776E+18，照字面比 Int64 的下限還小，要用 double 讀回來才是原本那個值。
                if (element.TryGetDouble(out var real) && NumberValue.TryFromDouble(real, out var rounded) && rounded.IsInteger)
                {
                    return rounded.Integer;
                }
            }

            throw new JsonException("這個值不是落在 Int64 範圍內的整數。");
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }
}
