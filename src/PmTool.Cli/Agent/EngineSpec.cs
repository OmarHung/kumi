using System.Text.RegularExpressions;

namespace PmTool.Cli.Agent;

/// <summary>引擎要怎麼啟動：命令列、從標準輸入送的提示、要先寫進暫存資料夾的檔案（檔名 → 內容）。</summary>
internal sealed record LaunchPlan(IReadOnlyList<string> Arguments, string StandardInput, IReadOnlyDictionary<string, string> Files);

/// <summary>一種引擎的啟動方式與事件轉換。命令列怎麼組，是這個指令最要緊的安全邊界，兩個實作各自照規格寫。</summary>
internal interface IEngineSpec
{
    string Kind { get; }

    LaunchPlan Plan(AgentJob job, string workDirectory, IReadOnlyList<StagedAttachment>? attachments = null);

    IEngineTranslator CreateTranslator();
}

/// <summary>
/// 伺服器送來的工作要變成命令列與設定檔的一部分（模型、工具名稱、網址），先檢查：
/// 不能因為伺服器（或在路上被改過的回應）送來 <c>--something</c> 就多出一個旗標，
/// 也不能讓 <c>mcpUrl</c> 夾帶 <c>${VAR}</c>：Claude Code 會展開設定檔裡的變數，等於把使用者的環境變數送去任意主機。
/// </summary>
internal static partial class JobValidation
{
    /// <summary>引擎最多能用的 Kumi MCP 工具。伺服器送來的 allowedTools 只能是它的子集：多出來的不跑。</summary>
    public static readonly IReadOnlyList<string> KnownTools =
    [
        "get_blueprint", "list_templates", "get_template", "describe_usage", "preview_changeset", "propose_changeset", "get_changeset",
        "get_item", "search_items", "propose_item_changes",
    ];

    private const string McpPath = "/mcp";
    private const int MaxTurnsLimit = 1000;

    /// <summary>整段對話累積的附件個數上限（一則訊息最多 5 個，但歷史裡的也會重送）。</summary>
    private const int MaxAttachments = 50;

    private const long MaxImageBytes = 5L * 1024 * 1024;
    private const long MaxPdfBytes = 10L * 1024 * 1024;
    private const long MaxAttachmentTotalBytes = 15L * 1024 * 1024;
    private static readonly HashSet<string> Efforts = ["none", "low", "medium", "high", "xhigh", "max"];

    /// <summary>沒問題回 null，有問題回要給使用者看的原因。<paramref name="server"/> 是登入的伺服器網址。</summary>
    public static string? Check(AgentJob job, string server)
    {
        if (job.Model != EngineModel.DefaultId && !ModelPattern().IsMatch(job.Model))
        {
            return "模型代號不正確";
        }

        if (job.Effort is not null && !Efforts.Contains(job.Effort))
        {
            return "思考深度不正確";
        }

        // 專案代號會原樣進 MCP 的標頭與引擎的設定：只認伺服器專案代號的格式，換行、引號都進不來。
        if (!ProjectKeyPattern().IsMatch(job.ProjectKey))
        {
            return "專案代號不正確";
        }

        if (job.AllowedTools.Count == 0 || job.AllowedTools.Any(tool => !KnownTools.Contains(tool)))
        {
            return "允許的工具不正確";
        }

        if (NormalizeMcpUrl(job.McpUrl, server) is null)
        {
            return "MCP 網址不正確";
        }

        if (job.MaxTurns is < 1 or > MaxTurnsLimit)
        {
            return "來回上限不正確";
        }

        return CheckAttachments(job);
    }

    /// <summary>
    /// 附件的檢查。上限和伺服器的 <c>AssistantAttachmentRules</c> 同一套數字（CLI 不引用模組，所以抄一份）：
    /// 伺服器已經擋過，這裡是不信任回應的第二道，也讓被換掉的回應不能叫這台電腦下載一個超大的檔案。
    /// </summary>
    private static string? CheckAttachments(AgentJob job)
    {
        if (job.Attachments.Count > MaxAttachments)
        {
            return "附件太多";
        }

        long total = 0;
        foreach (var attachment in job.Attachments)
        {
            if (AttachmentProblem(attachment, job.Engine) is { } problem)
            {
                return problem;
            }

            total += attachment.SizeBytes;
        }

        return total > MaxAttachmentTotalBytes ? "附件的總大小太大" : null;
    }

    private static string? AttachmentProblem(AgentAttachment attachment, string engine)
    {
        var isImage = attachment.Kind == AttachmentKinds.Image;
        var isPdf = attachment.Kind == AttachmentKinds.Pdf;
        if (attachment.Id == Guid.Empty || !(isImage || isPdf)
            || AgentAttachment.ExtensionOf(attachment.ContentType) is not { } extension
            || (extension == "pdf") != isPdf)
        {
            return "附件的種類不正確";
        }

        if (isPdf && engine == EngineKinds.Codex)
        {
            return "Codex 讀不到 PDF";
        }

        if (attachment.SizeBytes < 1 || attachment.SizeBytes > (isPdf ? MaxPdfBytes : MaxImageBytes))
        {
            return "附件的大小不正確";
        }

        return Sha256Pattern().IsMatch(attachment.Sha256) ? null : "附件的雜湊不正確";
    }

    /// <summary>
    /// 和登入的伺服器同源（scheme、主機、連接埠）、路徑剛好是（伺服器的基底路徑加）/mcp、沒有 query 與 fragment、
    /// 沒有帳密與會被展開或跳脫的字元；回正規化後的網址，不合格回 null。
    /// </summary>
    public static string? NormalizeMcpUrl(string mcpUrl, string server)
    {
        if (mcpUrl.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character is '$' or '{' or '}' or '"' or '\\' or '%')
            || !Uri.TryCreate(mcpUrl, UriKind.Absolute, out var uri)
            || !Uri.TryCreate(server, UriKind.Absolute, out var origin))
        {
            return null;
        }

        var expectedPath = origin.AbsolutePath.TrimEnd('/') + McpPath;
        var sameOrigin = uri.Scheme == origin.Scheme
            && string.Equals(uri.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == origin.Port;
        var clean = uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
        return sameOrigin && clean && uri.AbsolutePath == expectedPath ? uri.AbsoluteUri : null;
    }

    [GeneratedRegex(@"\A[0-9a-f]{64}\z")]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex(@"\A[A-Z][A-Z0-9]{0,19}\z")]
    private static partial Regex ProjectKeyPattern();

    // 不能以 - 開頭：那會被引擎當成旗標。\z 不是 $：$ 會放過結尾的換行。
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/\[\]\-]{0,99}\z")]
    private static partial Regex ModelPattern();
}
