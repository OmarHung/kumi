using System.Text.Json;

namespace PmTool.Cli.Agent;

/// <summary>找得到、能用的引擎，與沒辦法提供的引擎的原因（怎麼裝、怎麼登入）。</summary>
internal sealed record DiscoveryResult(IReadOnlyList<IAgentEngine> Engines, IReadOnlyList<string> Notes);

/// <summary>
/// 只跑不送提示的指令（<c>--version</c>、<c>auth status</c>、<c>login status</c>、<c>features list</c>、<c>exec --help</c>），
/// 確認引擎裝了、登入了、而且關得掉不該有的工具。不能為了「試試看」送提示：那會用掉使用者的訂閱額度。
/// </summary>
internal sealed class EngineDiscovery(Func<string, string?> environment, string mcpToken, string server, AgentOptions options)
{
    private static readonly string[] ClaudeRequiredFlags = EngineProber.ClaudeRequiredFlags;

    private static readonly string[] CodexRequiredFlags = EngineProber.CodexRequiredFlags;

    private readonly EngineProber _prober = new(options);

    public async Task<DiscoveryResult> DiscoverAsync(IReadOnlyCollection<string> kinds, CancellationToken cancellation)
    {
        var engines = new List<IAgentEngine>();
        var notes = new List<string>();
        foreach (var kind in kinds)
        {
            var (engine, note) = kind == EngineKinds.Codex
                ? await TryCodexAsync(cancellation)
                : await TryClaudeAsync(cancellation);
            if (engine is not null)
            {
                engines.Add(engine);
            }

            if (note is not null)
            {
                notes.Add(note);
            }
        }

        return new DiscoveryResult(engines, notes);
    }

    private async Task<(IAgentEngine?, string?)> TryClaudeAsync(CancellationToken cancellation)
    {
        const string name = "Claude Code";
        if (ExecutableLocator.Find(EngineKinds.ClaudeCode, environment) is not { } executable)
        {
            return (null, $"找不到 {name}（claude）：請先安裝（https://code.claude.com/docs/en/overview），再重新執行。");
        }

        if (BatchScriptNote(name, executable) is { } batch)
        {
            return (null, batch);
        }

        if (await ProbeAsync(executable, ["--version"], cancellation) is not { ExitCode: 0 } version)
        {
            return (null, $"{name} 跑不起來（claude --version 失敗）：請確認安裝完整，或重新安裝。");
        }

        if (await ProbeAsync(executable, ["--help"], cancellation) is not { } claudeHelp
            || ClaudeRequiredFlags.Where(flag => !HasFlag(claudeHelp.Output, flag)).ToArray() is { Length: > 0 })
        {
            return (null, $"這個版本的 {name} 太舊或不完整，沒有這些安全用的選項，為了安全不提供 {name}。請更新 {name}。");
        }

        if (!await IsClaudeLoggedInAsync(executable, cancellation))
        {
            return (null, $"{name} 沒有登入：請在終端機執行 claude，依畫面登入。");
        }

        // 別名實際指到哪個模型：只放在顯示名稱裡；id 仍是別名，Claude Code 更新後別名自然指到新的。
        var resolved = await ClaudeModels.ResolveAsync(executable, options, cancellation);
        var detail = resolved.TryGetValue(EngineModel.DefaultId, out var defaultModel) ? $"預設 {ClaudeModels.DisplayName(defaultModel)}" : null;
        var engine = new ProcessEngine(
            new ClaudeCodeSpec(), executable, FirstToken(version.StandardOutput), ClaudeModels.WithResolvedNames(resolved), mcpToken, server, options, detail);
        return (engine, null);
    }

    private async Task<bool> IsClaudeLoggedInAsync(string executable, CancellationToken cancellation)
    {
        if (await ProbeAsync(executable, ["auth", "status"], cancellation) is not { ExitCode: 0 } status)
        {
            return false;
        }

        // 結束代碼 0 時輸出是 JSON；明確寫著沒登入就算沒登入。
        try
        {
            using var document = JsonDocument.Parse(status.StandardOutput);
            return !document.RootElement.TryGetProperty("loggedIn", out var flag) || flag.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private async Task<(IAgentEngine?, string?)> TryCodexAsync(CancellationToken cancellation)
    {
        const string name = "Codex";
        if (ExecutableLocator.Find(EngineKinds.Codex, environment) is not { } executable)
        {
            return (null, $"找不到 {name}（codex）：請先安裝（https://github.com/openai/codex），再重新執行。");
        }

        if (BatchScriptNote(name, executable) is { } batch)
        {
            return (null, batch);
        }

        if (await ProbeAsync(executable, ["--version"], cancellation) is not { ExitCode: 0 } version)
        {
            return (null, $"{name} 跑不起來（codex --version 失敗）：請確認安裝完整，或重新安裝。");
        }

        if (await ProbeAsync(executable, ["login", "status"], cancellation) is not { ExitCode: 0 })
        {
            return (null, $"{name} 沒有登入：請在終端機執行 codex login。");
        }

        if (await ProbeAsync(executable, ["exec", "--help"], cancellation) is not { } help
            || CodexRequiredFlags.Any(flag => !help.Output.Contains(flag, StringComparison.Ordinal)))
        {
            return (null, $"這個版本的 {name} 太舊，沒有 {string.Join("、", CodexRequiredFlags)} 這些安全用的選項，為了安全不提供 {name}。請更新 {name}。");
        }

        var features = await ProbeAsync(executable, ["features", "list"], cancellation);
        var listed = features is { ExitCode: 0 } ? ParseFeatureNames(features.Output) : [];
        // 關不掉執行指令的工具，模型就能讀使用者電腦上的任何檔案：寧可不提供。
        if (!listed.Contains("shell_tool"))
        {
            return (null, $"這個版本的 {name} 沒辦法關掉執行指令的工具（codex features list 裡沒有 shell_tool），為了安全不提供 {name}。請更新 {name}。");
        }

        var disabled = CodexSpec.DisableCandidates.Where(listed.Contains).ToArray();
        var engine = new ProcessEngine(new CodexSpec(disabled), executable, LastToken(version.StandardOutput), CodexModels.Read(environment), mcpToken, server, options);
        return (engine, null);
    }

    private static string? BatchScriptNote(string name, string executable) => ExecutableLocator.IsBatchScript(executable)
        ? $"找到的 {name}（{Path.GetFileName(executable)}）是 .cmd／.bat：Windows 的批次檔對命令列的跳脫規則不同，系統提示裡的特殊字元會變成命令注入，所以不用。請改用 .exe 的安裝方式（例如官方的安裝程式），或用 KUMI_AGENT_{(name == "Codex" ? "CODEX" : "CLAUDE")}_PATH 指向 .exe。"
        : null;

    private static bool HasFlag(string help, string flag) => EngineProber.HasFlag(help, flag);

    private Task<ProbeResult?> ProbeAsync(string executable, string[] arguments, CancellationToken cancellation) =>
        _prober.ProbeAsync(executable, arguments, cancellation);

    private static HashSet<string> ParseFeatureNames(string output) => EngineProber.ParseFeatureNames(output);

    private static string FirstToken(string output) => EngineProber.FirstToken(output);

    private static string LastToken(string output) => EngineProber.LastToken(output);
}
