using System.Text.RegularExpressions;
using PmTool.Identity;

namespace PmTool.Api.Mcp;

/// <summary>
/// 本機代理（權杖有 <c>agent:run</c>）一次只替一則對話做事，那則對話屬於一個專案：每個請求都要用 <c>X-Kumi-Project</c> 標頭說明是哪個，
/// 所有工具只准碰那個專案。標頭是傳輸層的設定（本機代理啟動引擎時寫進 MCP 設定），模型改不了，
/// 所以項目內容裡夾帶的提示注入也無法讓它讀或改別的專案。一般的權杖（OAuth、個人存取權杖）不受影響。
/// </summary>
internal static partial class McpProjectScope
{
    public const string Header = "X-Kumi-Project";

    public const string MissingMessage = "本機代理要帶專案：請求要有 X-Kumi-Project 標頭（這則對話的專案代號）。";

    /// <summary>這把權杖是不是本機代理的（要受專案限制）。</summary>
    public static bool Applies((Guid TokenId, IReadOnlyList<AccessTokenScope> Scopes)? token) =>
        token?.Scopes.Contains(AccessTokenScope.AgentRun) == true;

    /// <summary>標頭裡的專案代號（轉大寫）；沒帶、帶了好幾個或格式不對是 null。</summary>
    public static string? Read(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(Header, out var values) || values.Count != 1 || values[0] is not { } text)
        {
            return null;
        }

        var key = text.Trim();
        return KeyPattern().IsMatch(key) ? key.ToUpperInvariant() : null;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
