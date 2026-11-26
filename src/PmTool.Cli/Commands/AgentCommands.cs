using System.Text.Json;
using PmTool.Cli.Agent;
using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Api;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Commands;

/// <summary>
/// <c>kumi agent start</c>：讓網頁的 AI 助理用這台電腦上自己登入的 Claude Code／Codex。給人開著的長駐指令，
/// 只往外連（長輪詢），不開連接埠；AI 代理不要自己執行它。規格見 internal/HANDOFF-local-agent.md 第 5 節。
/// <c>enable／disable／status</c> 管開機自動啟動（internal/HANDOFF-setup-wizard.md 第 4.3 節），實作在 <see cref="AutostartCommands"/>。
/// </summary>
internal static class AgentCommands
{
    private static readonly string[] RequiredScopes = ["agent:run", "blueprint:read", "blueprint:write"];

    private static readonly OptionSpec NameOption = new("name", "名稱", "這台電腦在網頁上的名稱（預設是主機名稱）");

    private static readonly OptionSpec EngineOption =
        new("engine", "引擎", "只提供這個引擎：claude-code 或 codex；可以重複（預設兩個都試）", Repeatable: true);

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("agent", "start", "開著它，網頁的 AI 助理就能用這台電腦上的 Claude Code／Codex",
            "kumi agent start [--name 名稱] [--engine claude-code|codex]… [--json]",
            [
                NameOption,
                EngineOption,
                new OptionSpec("service", "", "給開機自動啟動用：連不上伺服器時一直重試，不結束（401、403、沒有引擎才結束）"),
            ],
            0, 0, StartAsync),
        new("agent", "enable", "設定登入電腦時自動在背景啟動本機代理（macOS、Windows），並立刻啟動",
            "kumi agent enable [--server 網址] [--name 名稱] [--engine claude-code|codex]… [--json]",
            [NameOption, EngineOption],
            0, 0, AutostartCommands.EnableAsync),
        new("agent", "disable", "取消開機自動啟動，並停掉背景的本機代理",
            "kumi agent disable [--json]", [], 0, 0, AutostartCommands.DisableAsync),
        new("agent", "status", "看開機自動啟動有沒有設定、本機代理有沒有在執行、記錄檔最後幾行",
            "kumi agent status [--json]", [], 0, 0, AutostartCommands.StatusAsync),
    ];

    /// <summary>本機代理用的憑證與連線：先找 agent 專用的權杖，沒有才用命令列的那一把。</summary>
    internal static (ApiClient Api, Credentials Credentials) ConnectForAgent(CommandCall call)
    {
        var credentials = Credentials.ResolveForAgent(call.Context, call.Arguments.Value("server"));
        Credentials.EnsureTransportAllowed(call.Context, call.Arguments, credentials.Server);
        return (new ApiClient(call.Context.Http, credentials.Server, credentials.Token), credentials);
    }

    private static async Task<int> StartAsync(CommandCall call)
    {
        var service = call.Arguments.Has("service");
        var kinds = ResolveKinds(call.Arguments.Values("engine"));
        using var logs = service ? ServiceLogs.Open(call, () => DateTimeOffset.Now) : null;
        try
        {
            return await RunAsync(call, kinds, service, logs);
        }
        catch (CliException exception) when (logs is { LogsErrors: true })
        {
            // 主控台藏起來了，只有記錄檔看得到失敗的原因。
            logs.Error.WriteLine(exception.Message);
            throw;
        }
    }

    private const int ServiceLockAttempts = 5;
    private static readonly TimeSpan ServiceLockInterval = TimeSpan.FromSeconds(1);

    private static async Task<int> RunAsync(CommandCall call, IReadOnlyList<string> kinds, bool service, ServiceLogs? logs)
    {
        var context = call.Context;
        using var held = await AcquireLockAsync(call, service);
        var connection = new ConnectionTracker(held.Report, () => DateTimeOffset.Now);
        connection.Connecting();
        var (api, credentials) = ConnectForAgent(call);
        var console = new AgentConsole(logs?.Out ?? call.Out, logs?.Error ?? call.Error, call.Json);
        var options = service ? context.AgentOptions with { MaxBackoff = ServiceRetry.MaxBackoff } : context.AgentOptions;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(call.CancellationToken);
        using var registration = context.RegisterInterrupt(() => stop.Cancel());
        try
        {
            if (service)
            {
                await ServiceRetry.RunAsync(
                    async () => { await EnsureScopesAsync(api, stop.Token); return true; }, options, console.Notice, connection, stop.Token);
            }
            else
            {
                await EnsureScopesAsync(api, stop.Token);
            }

            connection.Succeeded();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }

        var discovery = await new EngineDiscovery(context.GetEnvironmentVariable, credentials.Token, api.Server, options)
            .DiscoverAsync(kinds, call.CancellationToken);
        foreach (var note in discovery.Notes)
        {
            console.Notice(note);
        }

        if (discovery.Engines.Count == 0)
        {
            throw new CliException(ExitCodes.Error, "這台電腦上沒有能用的 Claude Code 或 Codex，本機代理沒有東西可以提供。照上面的說明安裝、登入之後再執行一次。");
        }

        var machineName = MachineIdentity.ResolveName(call.Arguments.Value("name"));
        var loop = new AgentLoop(
            api, discovery.Engines, MachineIdentity.LoadOrCreateKey(context.ConfigPath), machineName, HelpText.VersionNumber,
            options, console, () => DateTimeOffset.Now, connection);

        console.Ready(api.Server, machineName, discovery.Engines, service);
        await loop.RunAsync(stop.Token);
        return ExitCodes.Success;
    }

    /// <summary>這台電腦同時只能有一個本機代理（前景或背景）；拿不到就說誰拿著。</summary>
    private static async Task<AgentLock> AcquireLockAsync(CommandCall call, bool background)
    {
        // 背景啟動時 status、enable 可能剛好在看鎖：多試幾次（每秒一次、最多 5 秒）再放棄。前景啟動拿不到就直接告訴使用者。
        var autostart = call.Context.Autostart;
        var (held, holder) = await AgentLock.TryAcquireWithRetryAsync(
            call.Context.ConfigPath, autostart.ProcessId, background, background ? ServiceLockAttempts : 1, ServiceLockInterval,
            autostart.Delay, call.CancellationToken);
        if (held is not null)
        {
            return held;
        }

        var who = holder?.Pid is { } pid ? $"（PID {pid}）" : string.Empty;
        throw new CliException(
            ExitCodes.Error,
            holder is { Background: true }
                ? $"本機代理已經在執行{who}。背景的那一個用 kumi agent disable 停掉。"
                : $"本機代理已經在執行{who}。請先關掉那個視窗（或那個程序）。");
    }

    internal static IReadOnlyList<string> ResolveKinds(IReadOnlyList<string> requested)
    {
        if (requested.Count == 0)
        {
            return [EngineKinds.ClaudeCode, EngineKinds.Codex];
        }

        return [.. requested.Select(option => EngineKinds.FromOption(option)
            ?? throw CliException.Usage($"不認得的引擎 {option}。可以用 claude-code 或 codex。")).Distinct()];
    }

    /// <summary>
    /// 權杖要有 agent:run 與 MCP 用的兩個範圍。伺服器告訴我們範圍時先擋下來說清楚；沒有告訴時，就等登記時的 403。
    /// </summary>
    internal static async Task EnsureScopesAsync(ApiClient api, CancellationToken cancellation)
    {
        var session = await api.GetAsync("session", null, cancellation);
        if (session.Get("scopes") is not { ValueKind: JsonValueKind.Array } scopes)
        {
            return;
        }

        var names = scopes.EnumerateArray().Select(scope => scope.GetString()).ToHashSet();
        var missing = RequiredScopes.Where(scope => !names.Contains(scope)).ToArray();
        if (missing.Length > 0)
        {
            throw new CliException(
                ExitCodes.Error,
                $"這把權杖缺少範圍：{string.Join("、", missing)}。請執行 kumi agent login，用瀏覽器登入並建立本機代理用的權杖。");
        }
    }
}
