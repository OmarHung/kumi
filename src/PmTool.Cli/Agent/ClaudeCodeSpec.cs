using System.Text.Json;

namespace PmTool.Cli.Agent;

/// <summary>
/// <c>claude -p</c> 的無頭模式。關掉所有內建工具、只連我們給的 MCP、只載入工作目錄（空的暫存資料夾）的專案設定，
/// 使用者自己的 hooks、技能、記憶都不載入；系統提示接在引擎自己的後面。2026-11-23 在 Claude Code 2.1.330 上驗過（見交接第 5 節）。
/// </summary>
internal sealed class ClaudeCodeSpec : IEngineSpec
{
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

    public LaunchPlan Plan(AgentJob job, string workDirectory)
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
        if (job.Model != EngineModel.DefaultId)
        {
            arguments.AddRange(["--model", job.Model]);
        }

        if (job.Effort is not null)
        {
            arguments.AddRange(["--effort", job.Effort]);
        }

        return new LaunchPlan(arguments, job.Prompt, new Dictionary<string, string>
        {
            ["mcp.json"] = McpConfig(job.McpUrl, job.ProjectKey),
            ["system.md"] = job.SystemPrompt,
        });
    }

    /// <summary>專案標頭讓伺服器把這把權杖限在對話的專案（代理的權杖一定要帶）；是字面值，已經驗過格式。權杖不寫進檔案：Claude Code 讀檔時把 ${KUMI_MCP_TOKEN} 換成環境變數的值。</summary>
    private static string McpConfig(string url, string projectKey) => JsonSerializer.Serialize(new
    {
        mcpServers = new
        {
            kumi = new
            {
                type = "http",
                url,
                headers = new Dictionary<string, string> { ["Authorization"] = "Bearer ${" + TokenVariable + "}", ["X-Kumi-Project"] = projectKey },
            },
        },
    });
}
