using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PmTool.Cli.Agent;

/// <summary>
/// <c>codex exec --json</c>。關掉執行指令的工具（<c>shell_tool</c>，沒有這個就不提供 Codex）與其他會碰到電腦的功能，
/// 模型只剩 Kumi 的 MCP；<c>--strict-config</c> 讓打錯的設定鍵直接失敗，不會默默少一道防護。研究在 codex-cli 0.163.0 上驗過（見交接第 5.1 節）。
/// </summary>
internal sealed class CodexSpec(IReadOnlyList<string> disabledFeatures) : IEngineSpec
{
    /// <summary>要關的功能；每個版本的名稱不同，不認得的 <c>--disable</c> 會失敗，所以只帶 <c>codex features list</c> 列得出來的。</summary>
    public static readonly IReadOnlyList<string> DisableCandidates =
    [
        "shell_tool", "view_image", "apps", "plugins", "hooks", "memories", "image_generation", "goals", "sleep_tool", "computer_use",
    ];

    /// <summary>Windows 的命令列上限約 32K 字元；系統提示比這個長時改放在標準輸入的最前面。</summary>
    private const int MaxCommandLineInstructions = 20_000;

    private static readonly string[] FixedConfig =
    [
        "agents.enabled=false",
        "web_search=\"disabled\"",
        "tools.experimental_request_user_input.enabled=false",
        "project_doc_max_bytes=0",
        "skills.include_instructions=false",
        "skills.bundled.enabled=false",
        "mcp_servers.kumi.bearer_token_env_var=\"" + ClaudeCodeSpec.TokenVariable + "\"",
        "mcp_servers.kumi.required=true",
        "mcp_servers.kumi.default_tools_approval_mode=\"auto\"",
    ];

    private static readonly JsonSerializerOptions TomlStringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string Kind => EngineKinds.Codex;

    public IEngineTranslator CreateTranslator() => new CodexTranslator();

    public LaunchPlan Plan(AgentJob job, string workDirectory)
    {
        var arguments = new List<string>
        {
            "exec", "--json", "--strict-config", "--skip-git-repo-check", "--ignore-user-config", "--ignore-rules", "--ephemeral",
            "-s", "read-only", "-C", workDirectory,
        };
        foreach (var feature in disabledFeatures)
        {
            arguments.AddRange(["--disable", feature]);
        }

        foreach (var setting in FixedConfig)
        {
            arguments.AddRange(["-c", setting]);
        }

        arguments.AddRange(["-c", $"mcp_servers.kumi.url={Toml(job.McpUrl)}"]);
        arguments.AddRange(["-c", $"mcp_servers.kumi.http_headers={{ \"X-Kumi-Project\" = {Toml(job.ProjectKey)}, \"X-Kumi-Job\" = {Toml(job.JobId.ToString())} }}"]);
        arguments.AddRange(["-c", $"mcp_servers.kumi.enabled_tools=[{string.Join(',', job.AllowedTools.Select(Toml))}]"]);
        if (job.Model != EngineModel.DefaultId)
        {
            arguments.AddRange(["-m", job.Model]);
        }

        if (job.Effort is not null)
        {
            arguments.AddRange(["-c", $"model_reasoning_effort={Toml(job.Effort)}"]);
        }

        // 系統提示：Codex 沒有「接在引擎後面」的旗標，但有設定鍵 developer_instructions，會變成一則 developer 訊息
        // （在 0.163.0 用 --strict-config 加假的模型伺服器驗過：鍵被認得、內容在請求的 developer 訊息裡）。
        // 所以優先用它；只有 Windows 的命令列放不下時，才改放在標準輸入的最前面（「以下是規則」），效力稍弱但一樣能用。
        var overflow = OperatingSystem.IsWindows() && job.SystemPrompt.Length > MaxCommandLineInstructions;
        if (!overflow)
        {
            arguments.AddRange(["-c", $"developer_instructions={Toml(job.SystemPrompt)}"]);
        }

        arguments.Add("-");
        var standardInput = overflow
            ? $"以下是這次對話一定要遵守的規則：\n\n{job.SystemPrompt}\n\n以下是對話：\n\n{job.Prompt}"
            : job.Prompt;
        return new LaunchPlan(arguments, standardInput, new Dictionary<string, string>());
    }

    /// <summary>TOML 的基本字串：跳脫規則和 JSON 的一樣（中文不轉成 \u），再拿掉 TOML 不收的 DEL。</summary>
    internal static string Toml(string value) =>
        JsonSerializer.Serialize(value.Replace("\u007f", string.Empty, StringComparison.Ordinal), TomlStringOptions);
}
