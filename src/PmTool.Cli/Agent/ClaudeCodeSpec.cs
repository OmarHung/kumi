using System.Text.Json;

namespace PmTool.Cli.Agent;

/// <summary>
/// <c>claude -p</c> 的無頭模式。關掉所有內建工具、只連我們給的 MCP、只載入工作目錄（空的暫存資料夾）的專案設定，
/// 使用者自己的 hooks、技能、記憶都不載入；系統提示接在引擎自己的後面。2026-11-23 在 Claude Code 2.1.330 上驗過（見交接第 5 節）。
/// </summary>
internal sealed class ClaudeCodeSpec : IEngineSpec
{
    // 預設的編碼器（非 ASCII 一律跳脫成 \u）：串流輸入是「一行一則」，U+2028、U+2029、U+0085 這幾個換行字元
    // 不能原樣留在行裡（有些讀行的實作會把它們當換行切開），中文轉成 \u 只是標準輸入大一點。
    private static readonly JsonSerializerOptions LineOptions = new();

    public const string TokenVariable = "KUMI_MCP_TOKEN";

    /// <summary>固定的模型清單：Claude Code 沒有列模型的指令，別名（opus、sonnet、haiku）由它自己對到最新的版本。</summary>
    public static readonly IReadOnlyList<EngineModel> Models =
    [
        new(EngineModel.DefaultId, "預設", ["low", "medium", "high", "xhigh", "max"]),
        new("opus", "Opus", ["low", "medium", "high", "xhigh", "max"]),
        new("sonnet", "Sonnet", ["low", "medium", "high", "xhigh", "max"]),
        new("haiku", "Haiku", ["low", "medium", "high"]),
    ];

    public string Kind => EngineKinds.ClaudeCode;

    public IEngineTranslator CreateTranslator() => new ClaudeCodeTranslator();

    public LaunchPlan Plan(AgentJob job, string workDirectory, IReadOnlyList<StagedAttachment>? attachments = null)
    {
        var arguments = new List<string>
        {
            "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--strict-mcp-config", "--mcp-config", Path.Combine(workDirectory, "mcp.json"),
            "--tools", string.Empty,
            "--allowedTools", string.Join(',', job.AllowedTools.Select(tool => ClaudeCodeTranslator.McpPrefix + tool)),
            "--permission-mode", "dontAsk",
            "--no-session-persistence",
            "--setting-sources", "project",
            "--disable-slash-commands",
            "--append-system-prompt-file", Path.Combine(workDirectory, "system.md"),
            "--max-turns", job.MaxTurns.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var hasAttachments = attachments is { Count: > 0 };
        if (hasAttachments)
        {
            // 純文字的標準輸入放不了圖片與 PDF；改成一行串流 JSON 的使用者訊息（2026-11-26 在 Claude Code 2.1.332 上驗過）。
            arguments.AddRange(["--input-format", "stream-json"]);
        }

        if (job.Model != EngineModel.DefaultId)
        {
            arguments.AddRange(["--model", job.Model]);
        }

        if (job.Effort is not null)
        {
            arguments.AddRange(["--effort", job.Effort]);
        }

        return new LaunchPlan(arguments, hasAttachments ? StreamInput(job.Prompt, attachments!) : job.Prompt, new Dictionary<string, string>
        {
            ["mcp.json"] = McpConfig(job.McpUrl, job.ProjectKey, job.JobId),
            ["system.md"] = job.SystemPrompt,
        });
    }

    /// <summary>串流輸入的一行使用者訊息：先是原本的提示，之後依序是每個附件的 base64 區塊（圖片用 image、PDF 用 document）。</summary>
    internal static string StreamInput(string prompt, IReadOnlyList<StagedAttachment> attachments)
    {
        var content = new List<object> { new { type = "text", text = prompt } };
        foreach (var attachment in attachments)
        {
            var source = new
            {
                type = "base64",
                media_type = attachment.ContentType,
                data = Convert.ToBase64String(File.ReadAllBytes(attachment.Path)),
            };
            content.Add(new { type = attachment.Kind == AttachmentKinds.Pdf ? "document" : "image", source });
        }

        return JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content } }, LineOptions) + "\n";
    }

    /// <summary>
    /// 專案標頭讓伺服器把這把權杖限在對話的專案（代理的權杖一定要帶）；工作標頭讓伺服器計這件工作的工具額度。都是字面值（專案代號已經驗過格式、工作編號是 GUID）。權杖不寫進檔案：Claude Code 讀檔時把 ${KUMI_MCP_TOKEN} 換成環境變數的值。</summary>
    private static string McpConfig(string url, string projectKey, Guid jobId) => JsonSerializer.Serialize(new
    {
        mcpServers = new
        {
            kumi = new
            {
                type = "http",
                url,
                headers = new Dictionary<string, string> { ["Authorization"] = "Bearer ${" + TokenVariable + "}", ["X-Kumi-Project"] = projectKey, ["X-Kumi-Job"] = jobId.ToString() },
            },
        },
    });
}
