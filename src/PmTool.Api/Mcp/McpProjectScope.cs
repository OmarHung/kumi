using System.Text.RegularExpressions;
using PmTool.Identity;

namespace PmTool.Api.Mcp;

/// <summary>
/// 本機代理（權杖有 <c>agent:run</c>）一次只替一則對話做事。對話屬於一個專案時，每個請求都要用 <c>X-Kumi-Project</c> 標頭說明是哪個，
/// 所有工具只准碰那個專案；不選專案的組織對話不帶專案標頭，範圍是「只限組織」（見 <see cref="McpScope"/>）。
/// 範圍由伺服器用 <c>X-Kumi-Job</c> 的工作找到對話決定，標頭只用來核對。標頭是傳輸層的設定（本機代理啟動引擎時寫進 MCP 設定），模型改不了，
/// 所以項目內容裡夾帶的提示注入也無法讓它讀或改別的專案。一般的權杖（OAuth、個人存取權杖）不受影響。
/// </summary>
internal static partial class McpProjectScope
{
    public const string Header = "X-Kumi-Project";

    /// <summary>本機代理這一件工作的編號（GUID）：伺服器用它計工具的額度（AG-07）。和專案標頭一樣由 kumi 寫進引擎的 MCP 設定。</summary>
    public const string JobHeader = "X-Kumi-Job";

    public const string MissingJobMessage = "本機代理的請求沒有 X-Kumi-Job 標頭：你的 kumi 太舊了，請執行 kumi update 更新到最新版再試。";

    public const string UnknownJobMessage = "找不到這件工作：它可能已經結束、不是你的，或專案和 X-Kumi-Project 不同。";

    public const string MissingMessage =
        "本機代理要說明這是哪一則對話：專案的對話要有 X-Kumi-Project 標頭（專案代號），不選專案的組織對話要有 X-Kumi-Job 標頭（工作編號）。";

    /// <summary>工作屬於不選專案的組織對話，請求卻帶了專案：不讓代理（或被注入的提示）把組織對話指到某個專案。</summary>
    public const string OrganizationJobWithProjectMessage =
        "這件工作屬於不選專案的組織對話，請求不能帶 X-Kumi-Project：請更新 kumi（kumi update）後再試。";

    /// <summary>有帶專案標頭（不管內容對不對）。</summary>
    public static bool HasHeader(HttpRequest request) => request.Headers.ContainsKey(Header);

    /// <summary>有帶工作標頭（不管內容對不對）。</summary>
    public static bool HasJobHeader(HttpRequest request) => request.Headers.ContainsKey(JobHeader);

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

    /// <summary>標頭裡的工作編號；沒帶、帶了好幾個或不是 GUID 是 null。</summary>
    public static Guid? ReadJob(HttpRequest request) =>
        request.Headers.TryGetValue(JobHeader, out var values) && values.Count == 1 && Guid.TryParse(values[0]?.Trim(), out var id) && id != Guid.Empty
            ? id
            : null;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
