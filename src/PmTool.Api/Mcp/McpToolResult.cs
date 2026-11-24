using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Abstractions;
using PmTool.Api.Json;

namespace PmTool.Api.Mcp;

/// <summary>工具回給代理的結果：一段 JSON 文字，加上是不是錯誤（MCP 的 <c>isError</c>）。</summary>
internal sealed record McpToolResult(bool IsError, string Json)
{
    // 和 API 同一套命名（camelCase、snake_case 列舉、UTC 時刻），代理在兩邊看到的字串一樣；沒有值的屬性不寫，省代理的字數。
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static McpToolResult Ok(object payload) => Limit(new McpToolResult(IsError: false, JsonSerializer.Serialize(payload, Options)));

    public static McpToolResult Error(string message) =>
        new(IsError: true, JsonSerializer.Serialize(new { error = message }, Options));

    /// <summary>模組回的失敗：訊息照 <see cref="Failure.Message"/>，有細項就一起帶，代理才知道是哪個欄位的問題。</summary>
    public static McpToolResult FromFailure(Failure failure) =>
        failure.Details.Count == 0
            ? new McpToolResult(IsError: true, JsonSerializer.Serialize(new { error = failure.Message, code = failure.Code }, Options))
            : new McpToolResult(
                IsError: true,
                JsonSerializer.Serialize(
                    new
                    {
                        error = failure.Message,
                        code = failure.Code,
                        issues = failure.Details.Select(detail => new { code = detail.Code, path = detail.Path, message = detail.Message }),
                    },
                    Options));

    private static McpToolResult Limit(McpToolResult result) =>
        result.Json.Length <= McpLimits.MaxResultCharacters
            ? result
            : Error($"結果太大了（{result.Json.Length} 個字元）。請縮小範圍，例如指定專案、降低 limit。");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // 中文不跳脫成 \uXXXX，代理讀起來才不會多一倍的字數。
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new SnakeCaseEnumConverterFactory());
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        return options;
    }
}
