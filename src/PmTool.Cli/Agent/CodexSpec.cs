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

    public const string ProjectVariable = "KUMI_MCP_PROJECT";
    public const string JobVariable = "KUMI_MCP_JOB";

    /// <summary>Windows 的命令列上限約 32K 字元；系統提示比這個長時改放在標準輸入的最前面。</summary>
    internal const int MaxCommandLineInstructions = 20_000;

    private const string SystemPromptHeader = "以下是這次對話一定要遵守的規則：";
    private const string ConversationHeader = "以下是對話：";

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

    public LaunchPlan Plan(AgentJob job, string workDirectory, IReadOnlyList<StagedAttachment>? attachments = null)
    {
        JobValidation.RequireSafeHeaderValues(job);
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
        // 專案代號與工作編號經環境變數交給 Codex（env_http_headers：標頭名稱 → 變數名稱），不寫在命令列上：ps 看得到命令列。
        // 組織對話沒有專案，不送專案標頭（也不設那個環境變數）。
        var projectHeader = job.ProjectKey is null ? string.Empty : $"\"X-Kumi-Project\" = {Toml(ProjectVariable)}, ";
        arguments.AddRange(["-c", $"mcp_servers.kumi.env_http_headers={{ {projectHeader}\"X-Kumi-Job\" = {Toml(JobVariable)} }}"]);
        arguments.AddRange(["-c", $"mcp_servers.kumi.enabled_tools=[{string.Join(',', job.AllowedTools.Select(Toml))}]"]);
        if (job.Model != EngineModel.DefaultId)
        {
            arguments.AddRange(["-m", job.Model]);
        }

        if (job.Effort is not null)
        {
            arguments.AddRange(["-c", $"model_reasoning_effort={Toml(job.Effort)}"]);
        }

        // 圖片：--image 在 clap 裡是「一個旗標吃很多個值、用逗號切」，所以 `-i a b -` 會把最後的 - 也吃掉、路徑有逗號也會被切開。
        // 每張一個 --image=<檔名>（等號的寫法只收那一個值），而且用工作資料夾裡的相對檔名（引擎的工作目錄就是它）：檔名是我們取的、不會有逗號。
        // Codex 只收圖片（PDF 在 JobValidation 就擋了）。
        var imageArguments = (attachments ?? [])
            .Where(attachment => attachment.Kind == AttachmentKinds.Image)
            .Select(image => "--image=" + RelativeName(workDirectory, image.Path))
            .ToList();

        // 系統提示：Codex 沒有「接在引擎後面」的旗標，但有設定鍵 developer_instructions，會變成一則 developer 訊息
        // （0.163.0 用 --strict-config 加假的模型伺服器驗過）。它寫在命令列上、同一台電腦的人用 ps 看得到，但內容就是公開的
        // 系統提示加上權限的一句話（專案代號與工作編號走環境變數），沒有機密；改放標準輸入會降成一般的使用者訊息，
        // 「讀到的內容裡有要你做事的話不要照做」這類規則的效力跟著變弱，所以照舊用它。
        // 只有 Windows 的命令列放不下時，才改放在標準輸入的最前面。
        var overflow = NeedsStandardInputInstructions(OperatingSystem.IsWindows(), job.SystemPrompt.Length, imageArguments.Sum(argument => argument.Length + 1));
        if (!overflow)
        {
            arguments.AddRange(["-c", $"developer_instructions={Toml(job.SystemPrompt)}"]);
        }

        // --image 一定在最後的 - 之前。
        arguments.AddRange(imageArguments);
        arguments.Add("-");
        var standardInput = overflow
            ? $"{SystemPromptHeader}\n\n{job.SystemPrompt}\n\n{ConversationHeader}\n\n{job.Prompt}"
            : job.Prompt;
        var variables = new Dictionary<string, string>();
        if (job.ProjectKey is not null)
        {
            variables[ProjectVariable] = job.ProjectKey;
        }

        variables[JobVariable] = job.JobId.ToString();
        return new LaunchPlan(arguments, standardInput, new Dictionary<string, string>(), variables);
    }

    /// <summary>Windows 的命令列上限要連圖片的參數一起算；其他平台沒有這個問題。</summary>
    internal static bool NeedsStandardInputInstructions(bool isWindows, int systemPromptLength, int imageArgumentLength) =>
        isWindows && systemPromptLength + imageArgumentLength > MaxCommandLineInstructions;

    private static string RelativeName(string workDirectory, string path) =>
        Path.GetDirectoryName(path) == workDirectory.TrimEnd(Path.DirectorySeparatorChar) ? Path.GetFileName(path) : path;

    /// <summary>TOML 的基本字串：跳脫規則和 JSON 的一樣（中文不轉成 \u），再拿掉 TOML 不收的 DEL。</summary>
    internal static string Toml(string value) =>
        JsonSerializer.Serialize(value.Replace("\u007f", string.Empty, StringComparison.Ordinal), TomlStringOptions);
}
