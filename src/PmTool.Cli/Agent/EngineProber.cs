namespace PmTool.Cli.Agent;

/// <summary>
/// 只跑不送提示的小指令（<c>--version</c>、<c>auth status</c>、<c>login status</c>、<c>features list</c>、<c>--help</c>）的共用部分，
/// 以及解析它們輸出的小工具。本機代理的探測（<see cref="EngineDiscovery"/>）與主機 worker 的探測共用，
/// 「安全用的旗標有哪些」只有這一份。
/// </summary>
internal sealed class EngineProber(AgentOptions options)
{
    public static readonly string[] ClaudeRequiredFlags =
    [
        "--tools", "--allowedTools", "--setting-sources", "--strict-mcp-config", "--disable-slash-commands",
        "--append-system-prompt-file", "--no-session-persistence", "--permission-mode", "--mcp-config",
    ];

    public static readonly string[] CodexRequiredFlags = ["--strict-config", "--ignore-user-config", "--ignore-rules", "--ephemeral"];

    private readonly ProcessRunner _runner = new(options);

    /// <summary>探測也在每次新建、只有自己能進的空資料夾裡跑，不用共用的暫存資料夾（別人可能在裡面放東西）。起不來或逾時回 null。</summary>
    public async Task<ProbeResult?> ProbeAsync(string executable, string[] arguments, CancellationToken cancellation)
    {
        var directory = Directory.CreateTempSubdirectory("kumi-probe-");
        try
        {
            return await _runner.ProbeAsync(
                new ProcessRequest(executable, arguments, directory.FullName, string.Empty, new Dictionary<string, string>(), ProcessEngine.RemovedVariables),
                cancellation);
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 刪不掉就算了。
            }
        }
    }

    /// <summary>help 裡找得到這個旗標。Claude Code 把 <c>--append-system-prompt</c> 與 <c>-file</c> 合寫成 <c>--append-system-prompt[-file]</c>。</summary>
    public static bool HasFlag(string help, string flag) =>
        help.Contains(flag, StringComparison.Ordinal)
        || (flag.EndsWith("-file", StringComparison.Ordinal) && help.Contains(flag[..^"-file".Length] + "[-file]", StringComparison.Ordinal));

    /// <summary><c>codex features list</c> 每行第一欄是功能名稱（後面是階段與開關）。</summary>
    public static HashSet<string> ParseFeatureNames(string output) =>
        [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty)
            .Where(token => token.Length > 0)];

    /// <summary>claude --version 是「2.1.330 (Claude Code)」，第一段是版本。</summary>
    public static string FirstToken(string output) => Limit(output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());

    /// <summary>codex --version 是「codex-cli 0.163.0」，最後一段是版本。</summary>
    public static string LastToken(string output) => Limit(output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault());

    private static string Limit(string? version) =>
        string.IsNullOrEmpty(version) ? "unknown" : version.Length <= 40 ? version : version[..40];
}
