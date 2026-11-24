using System.Buffers;
using System.Text;
using System.Text.Json;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Items;

/// <summary>自訂欄位的值和 jsonb 文字之間的轉換。值在寫進來之前已經檢查與正規化過。</summary>
internal static class FieldValues
{
    /// <summary>新項目還沒有任何自訂欄位的值。</summary>
    public static readonly IReadOnlyDictionary<string, JsonElement> None = new Dictionary<string, JsonElement>();

    public static IReadOnlyDictionary<string, JsonElement> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    public static string Serialize(IReadOnlyDictionary<string, JsonElement> fields)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// 兩個檢查過的值是不是同一個值；null 表示沒有值。多選看選了哪些、不管順序；數字看大小、不管寫法。
    /// 形狀不同（例如藍圖改過欄位的型別）一律當成不同。
    /// </summary>
    public static bool SameValue(FieldType type, JsonElement? current, JsonElement? changed) => (current, changed) switch
    {
        (null, null) => true,
        ({ } a, { } b) when a.ValueKind != b.ValueKind => false,
        ({ } a, { } b) when type == FieldType.MultiSelect && a.ValueKind == JsonValueKind.Array => Options(a).SetEquals(Options(b)),
        ({ } a, { } b) when a.ValueKind == JsonValueKind.Number => a.GetDecimal() == b.GetDecimal(),
        ({ } a, { } b) => JsonElement.DeepEquals(a, b),
        _ => false,
    };

    private static HashSet<string?> Options(JsonElement options) => [.. options.EnumerateArray().Select(o => o.GetString())];

    /// <summary>把修改套到目前的值上：有值的換掉，null 的拿掉，沒提到的留著。</summary>
    public static IReadOnlyDictionary<string, JsonElement> Merge(
        IReadOnlyDictionary<string, JsonElement> current, IReadOnlyDictionary<string, JsonElement?> changes)
    {
        var kept = current.Where(pair => !changes.ContainsKey(pair.Key));
        var added = changes.Where(pair => pair.Value is not null).Select(pair => KeyValuePair.Create(pair.Key, pair.Value!.Value));
        return kept.Concat(added).ToDictionary(StringComparer.Ordinal);
    }
}
