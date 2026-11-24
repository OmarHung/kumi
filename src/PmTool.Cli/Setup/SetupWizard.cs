using PmTool.Cli.Agent;
using PmTool.Cli.Commands;
using PmTool.Cli.Login;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Setup;

/// <summary>
/// <c>kumi setup</c>：登入、Claude Code／Codex 的 MCP、本機代理與開機自動啟動一次設定好（internal/HANDOFF-setup-wizard.md 第 4.2 節）。
/// 順序：偵測、把問題全部問完、才動手；每一步失敗都記下來繼續，最後總結，有失敗結束代碼是 1。
/// </summary>
internal sealed class SetupWizard(CommandCall call)
{
    private const string MissingNote = "沒有安裝";
    private const string DeclinedNote = "略過";

    private CliContext Context => call.Context;

    private TextWriter Out => call.Out;

    public async Task<int> RunAsync()
    {
        Require();
        var server = await ResolveServerAsync();
        await Out.WriteLineAsync($"Kumi 設定精靈　{server}");
        await Out.WriteLineAsync();

        var engines = await DetectAsync();
        var prompt = new SetupPrompt(Context, call.CancellationToken);
        var plan = await AskAsync(prompt, server, engines);

        var executor = new SetupExecutor(call, server, Out, plan.StepCount);
        var results = await ExecuteAsync(executor, server, plan, engines);
        await SetupSummary.WriteAsync(Out, results);

        var exit = results.Any(result => result.Status == SetupStatus.Failed) ? ExitCodes.Error : ExitCodes.Success;
        return await OfferStartAsync(prompt, server, plan, results) is var startExit && startExit != ExitCodes.Success ? startExit : exit;
    }

    private void Require()
    {
        if (call.Json)
        {
            throw CliException.Usage("kumi setup 是一問一答的精靈，不支援 --json。");
        }

        if (!Context.IsInteractive)
        {
            throw CliException.Usage("kumi setup 要在終端機裡執行（它會問問題、開瀏覽器）。沒有終端機時，請用 kumi auth login --with-token 登入。");
        }
    }

    private async Task<string> ResolveServerAsync()
    {
        var server = await LoginCommand.ResolveServerAsync(call);
        Credentials.EnsureTransportAllowed(Context, call.Arguments, server);
        return server;
    }

    private async Task<IReadOnlyList<(string Kind, DetectedEngine? Engine)>> DetectAsync()
    {
        await Out.WriteLineAsync("這台電腦上：");
        var detector = new EngineDetector(Context);
        List<(string, DetectedEngine?)> found = [];
        foreach (var kind in new[] { EngineKinds.ClaudeCode, EngineKinds.Codex })
        {
            var engine = await detector.DetectAsync(kind, call.CancellationToken);
            found.Add((kind, engine));
            await Out.WriteLineAsync(engine is null
                ? $"  – 找不到 {EngineDetector.DisplayName(kind)}（安裝：{EngineDetector.InstallUrl(kind)}）"
                : $"  ✓ {engine.DisplayName} {engine.Version}");
        }

        if (found.All(item => item.Item2 is null))
        {
            await Out.WriteLineAsync("沒有找到 Claude Code 或 Codex，這次只設定命令列工具。裝好之後再執行一次 kumi setup 就能接上。");
        }

        await Out.WriteLineAsync();
        return found;
    }

    private async Task<SetupPlan> AskAsync(
        SetupPrompt prompt, string server, IReadOnlyList<(string Kind, DetectedEngine? Engine)> engines)
    {
        var present = engines.Where(item => item.Engine is not null).Select(item => item.Engine!).ToArray();
        await Out.WriteLineAsync("要設定哪些？（直接按 Enter 是「是」）");
        var mcp = new McpConfigurator(Context, Out);
        List<McpPlan> plans = [];
        foreach (var engine in present)
        {
            plans.Add(await AskEngineAsync(prompt, mcp, engine, server));
        }

        var agent = present.Length > 0
            && await prompt.AskAsync("  讓網頁的 AI 助理用這台電腦的 Claude Code／Codex（本機代理）？");
        var autostart = agent && Context.Login.AutostartSupported
            && await prompt.AskAsync("    登入電腦時自動在背景啟動本機代理？");
        await Out.WriteLineAsync();
        return new SetupPlan(plans, agent, autostart);
    }

    private async Task<McpPlan> AskEngineAsync(SetupPrompt prompt, McpConfigurator mcp, DetectedEngine engine, string server)
    {
        if (!await prompt.AskAsync($"  讓 {engine.DisplayName} 使用 Kumi？"))
        {
            return new McpPlan(engine, null, DeclinedNote);
        }

        var url = mcp.McpUrl(server);
        if (!McpConfigurator.IsSafeUrl(url))
        {
            await Out.WriteLineAsync("    伺服器網址含有不支援的字元，沒辦法替它加上 MCP。");
            return new McpPlan(engine, null, "伺服器網址含有不支援的字元，請手動加上 MCP");
        }

        var existing = await mcp.InspectAsync(engine, call.CancellationToken);
        if (!existing.Exists)
        {
            return new McpPlan(engine, McpAction.Add, string.Empty);
        }

        if (McpConfigurator.SameUrl(existing.Url, url))
        {
            return new McpPlan(engine, McpAction.KeepAndLogin, string.Empty);
        }

        var replace = await prompt.AskAsync(
            $"    {engine.DisplayName} 已經有一個叫 kumi 的 MCP 伺服器（指到 {existing.Url ?? "不明的位置"}），要換成這個伺服器嗎？", defaultYes: false);
        return replace ? new McpPlan(engine, McpAction.Replace, string.Empty) : new McpPlan(engine, null, "保留原來的 kumi 設定");
    }

    private async Task<List<SetupResult>> ExecuteAsync(
        SetupExecutor executor, string server, SetupPlan plan, IReadOnlyList<(string Kind, DetectedEngine? Engine)> engines)
    {
        await executor.BeginStepAsync("登入 Kumi");
        var login = await new SetupLogin(call, server, Out).EnsureAsync(plan.Agent);
        List<SetupResult> results =
        [
            login.CliReady
                ? new SetupResult("命令列工具", SetupStatus.Done, "已登入")
                : new SetupResult("命令列工具", SetupStatus.Failed, $"沒有登入：{login.Failure}。之後執行 kumi auth login"),
        ];

        foreach (var (kind, engine) in engines)
        {
            var name = EngineDetector.DisplayName(kind);
            var mcpPlan = plan.Mcp.FirstOrDefault(item => item.Engine.Kind == kind);
            results.Add(engine is null
                ? new SetupResult(name, SetupStatus.Skipped, MissingNote)
                : mcpPlan!.Action is null
                    ? new SetupResult(name, SetupStatus.Skipped, mcpPlan.SkipReason)
                    : await executor.RunMcpAsync(mcpPlan));
        }

        results.Add(plan.Agent
            ? await executor.RunAgentAsync(plan, login)
            : new SetupResult("本機代理", SetupStatus.Skipped, engines.Any(item => item.Engine is not null) ? DeclinedNote : "需要先安裝 Claude Code 或 Codex"));
        return results;
    }

    /// <summary>沒有設開機自動啟動、權杖也好了：問要不要現在在這個視窗啟動（會一直開著，直到使用者關掉）。</summary>
    private async Task<int> OfferStartAsync(SetupPrompt prompt, string server, SetupPlan plan, IReadOnlyList<SetupResult> results)
    {
        var agentReady = results.Any(result => result is { Name: "本機代理", Status: SetupStatus.Done });
        if (!plan.Agent || plan.Autostart || !agentReady)
        {
            return ExitCodes.Success;
        }

        await Out.WriteLineAsync();
        return await prompt.AskAsync("現在在這個視窗啟動本機代理嗎？（視窗要一直開著；之後也可以執行 kumi agent start）")
            ? await SubCommand.RunAsync(call, ["agent", "start"], server)
            : ExitCodes.Success;
    }
}
