using PmTool.Cli.Commands;

namespace PmTool.Cli.Setup;

/// <param name="Action">null 是不設定這個引擎（使用者說不要，或已經有別的設定又不換）。</param>
internal sealed record McpPlan(DetectedEngine Engine, McpAction? Action, string SkipReason);

internal sealed record SetupPlan(IReadOnlyList<McpPlan> Mcp, bool Agent, bool Autostart)
{
    public int StepCount => 1 + Mcp.Count(item => item.Action is not null) + (Agent ? 1 : 0);
}

/// <summary>照問好的計畫一步一步做：Claude Code、Codex 的 MCP 與本機代理。每一步失敗都只記下來、繼續下一步。</summary>
internal sealed class SetupExecutor(CommandCall call, string server, TextWriter output, int totalSteps)
{
    private const string LocalAgentName = "本機代理";
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
