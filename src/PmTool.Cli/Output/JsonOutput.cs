using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Output;

/// <summary>--json 的輸出：縮排的 JSON，中文照原樣寫出，不轉成 \uXXXX。</summary>
internal static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Task WriteAsync(TextWriter output, JsonElement data) =>
        output.WriteLineAsync(data.ValueKind == JsonValueKind.Undefined ? "null" : JsonSerializer.Serialize(data, Options));

    public static Task WriteAsync(TextWriter output, JsonNode? data) =>
        output.WriteLineAsync(data is null ? "null" : data.ToJsonString(Options));
}
