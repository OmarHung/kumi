using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace PmTool.Api.Mcp;

/// <summary>
/// 工具參數的指紋：物件的鍵排序之後寫成不縮排的 JSON，取 SHA-256（小寫十六進位）。
/// 同一件工作裡同一個提出類的工具、參數的內容一樣就是同一次提案（<c>IHostToolGrants.BeginWriteAsync</c>），
/// 所以鍵的順序、空白不同的重送要算成同一個。
/// </summary>
internal static class McpArgumentHash
{
    public static string Of(IDictionary<string, JsonElement>? arguments)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in (arguments ?? new Dictionary<string, JsonElement>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(name);
                Write(writer, value);
            }

            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
