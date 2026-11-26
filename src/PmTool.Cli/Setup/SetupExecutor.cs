using PmTool.Cli.Commands;
using PmTool.Cli.Skills;

namespace PmTool.Cli.Setup;

/// <param name="Action">null 是不設定這個引擎（使用者說不要，或已經有別的設定又不換）。</param>
internal sealed record McpPlan(DetectedEngine Engine, McpAction? Action, string SkipReason);

internal sealed record SetupPlan(IReadOnlyList<McpPlan> Mcp, bool Agent, bool Autostart, bool Skills = false)
{
    public int StepCount => 1 + Mcp.Count(item => item.Action is not null) + (Skills ? 1 : 0) + (Agent ? 1 : 0);
}

/// <summary>照問好的計畫一步一步做：Claude Code、Codex 的 MCP 與本機代理。每一步失敗都只記下來、繼續下一步。</summary>
internal sealed class SetupExecutor(CommandCall call, string server, TextWriter output, int totalSteps)
{
    private const string LocalAgentName = "本機代理";
    public const string SkillsName = "AI 代理的 skill";
    private int _step = 1;

    public async Task BeginStepAsync(string title) => await output.WriteLineAsync($"[{_step++}/{totalSteps}] {title}");

    public async Task<SetupResult> RunMcpAsync(McpPlan plan)
    {
        var engine = plan.Engine;
        await BeginStepAsync(engine.DisplayName);
        var mcp = new McpConfigurator(call.Context, output);
        var cancellation = call.CancellationToken;
        if (await mcp.ApplyAsync(engine, plan.Action!.Value, mcp.McpUrl(server), cancellation) is { } failure)
        {
            await output.WriteLineAsync($"  ✗ {failure}");
            return new SetupResult(engine.DisplayName, SetupStatus.Failed, $"沒有加上 MCP：{failure}");
        }

        if (!await mcp.NeedsLoginAsync(engine, cancellation))
        {
            await output.WriteLineAsync($"  {engine.DisplayName} 已經授權過 Kumi，不用再登入。");
        }
        else if (await mcp.LoginAsync(engine, call.Arguments.Has("no-browser"), cancellation) is { } warning)
        {
            await output.WriteLineAsync($"  ✗ {warning}");
            return new SetupResult(engine.DisplayName, SetupStatus.Failed, $"已加上 MCP，但{warning}");
        }

        await output.WriteLineAsync($"  ✓ {engine.DisplayName} 可以使用 Kumi 了");
        return new SetupResult(engine.DisplayName, SetupStatus.Done, "已連上 Kumi");
    }

    /// <summary>
    /// 把 skill 裝進偵測到的引擎（使用者層）。舊名稱的資料夾在這裡才問（只有真的有的時候）；
    /// 同名但不是 kumi 裝的資料夾不覆蓋，總結裡提示用 kumi skill install --force。
    /// </summary>
    public async Task<SetupResult> RunSkillsAsync(IReadOnlyList<DetectedEngine> engines, SetupPrompt prompt)
    {
        await BeginStepAsync(SkillsName);
        var locations = engines.Select(engine => SkillLocations.Global(call.Context, engine.Kind)).ToList();
        var outcomes = await SkillService.Current().InstallAsync(
            locations, force: false, LegacyPolicy.Ask,
            directory => prompt.AskAsync($"  舊名稱的 skill {directory} 還在，同一份 skill 出現兩次會讓代理混淆。要移除嗎？"));
        await SkillReport.WriteTextAsync(output, call.Error, outcomes);
        if (outcomes.Any(outcome => outcome.Action == SkillAction.Failed))
        {
            return new SetupResult(SkillsName, SetupStatus.Failed, "沒有裝成功，之後執行 kumi skill install");
        }

        return outcomes.Any(outcome => outcome.Action is SkillAction.SkippedForeign or SkillAction.LegacyKept)
            ? new SetupResult(SkillsName, SetupStatus.Done, "已安裝；有的資料夾不是 kumi 裝的或是舊名稱，沒有動（kumi skill status 看狀況）")
            : new SetupResult(SkillsName, SetupStatus.Done, "已安裝（kumi skill status 看狀態，kumi update 會一起更新）");
    }

    /// <summary>要開機自動啟動就呼叫 <c>kumi agent enable</c>（它自己印訊息）；不要的話只確認權杖在，啟動留到總結之後問。</summary>
    public async Task<SetupResult> RunAgentAsync(SetupPlan plan, SetupLoginState login)
    {
        await BeginStepAsync(LocalAgentName);
        if (!login.AgentReady)
        {
            var reason = login.AgentSkipped
                ? new SetupResult(LocalAgentName, SetupStatus.Skipped, "組織沒有開放成員使用自己的 AI 帳號")
                : new SetupResult(LocalAgentName, SetupStatus.Failed, "沒有本機代理的權杖。登入之後執行 kumi agent login，再執行 kumi agent start");
            await output.WriteLineAsync($"  {(reason.Status == SetupStatus.Skipped ? "–" : "✗")} {reason.Detail}");
            return reason;
        }

        if (!plan.Autostart)
        {
            await output.WriteLineAsync("  ✓ 本機代理的權杖已經存好");
            return new SetupResult(LocalAgentName, SetupStatus.Done, "權杖已存好，要用的時候執行 kumi agent start");
        }

        var exit = await SubCommand.RunAsync(call, ["agent", "enable"], server);
        return exit == ExitCodes.Success
            ? new SetupResult(LocalAgentName, SetupStatus.Done, "登入電腦時自動啟動（kumi agent status 看狀態）")
            : new SetupResult(LocalAgentName, SetupStatus.Failed, $"kumi agent enable 沒有成功（結束代碼 {exit}）。可以先用 kumi agent start 在視窗裡開著");
    }
}
