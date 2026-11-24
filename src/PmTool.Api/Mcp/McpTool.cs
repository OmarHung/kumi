using System.Text.Json;
using PmTool.Identity;

namespace PmTool.Api.Mcp;

/// <summary>
/// 一個 MCP 工具。參數的 Schema 自己寫（只用 type、properties、required、additionalProperties:false、items、enum、description，
/// 見 CLAUDE.md），不從 C# 型別產生：產生器會寫出 <c>$defs</c>、<c>format</c> 這些關鍵字，也不容易寫出給代理看的說明。
/// </summary>
internal sealed record McpTool(
    string Name,
    string Description,
    string InputSchema,
    AccessTokenScope Scope,
    bool ReadOnly,
    Func<McpToolContext, McpArguments, CancellationToken, Task<McpToolResult>> Run)
{
    /// <summary>Schema 裡宣告的參數名稱；收到沒宣告的參數就擋下，拼錯名稱時代理才會知道，而不是默默被忽略。</summary>
    public IReadOnlySet<string> ParameterNames { get; } = ParameterNamesOf(InputSchema);

    public JsonElement ParsedSchema() => JsonSerializer.Deserialize<JsonElement>(InputSchema);

    private static HashSet<string> ParameterNamesOf(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.TryGetProperty("properties", out var properties)
            ? [.. properties.EnumerateObject().Select(property => property.Name)]
            : [];
    }
}
