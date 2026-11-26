using System.Globalization;
using System.Text.Json.Nodes;
using PmTool.Cli.Commands;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Agent.Autostart;

/// <summary>
/// <c>kumi agent enable／disable／status</c>：登入電腦時自動在背景啟動本機代理。
/// 規格見 internal/HANDOFF-setup-wizard.md 第 4.3 節；<c>kumi setup</c> 用 <c>CliRunner</c> 呼叫 enable，所以訊息與結束代碼是合約。
/// </summary>
internal static class AutostartCommands
{
    private const int StatusLogLines = 10;
    private const int FailureLogLines = 5;

    public static async Task<int> EnableAsync(CommandCall call)
    {
        var context = call.Context;
        var environment = context.Autostart;
        var backend = AutostartBackends.For(environment) ?? throw new CliException(
            ExitCodes.Error, "這個作業系統還不支援自動啟動，請自己開著 kumi agent start（或用 systemd）。");

        var spec = await CheckPreconditionsAsync(call, environment);
        var manager = new AutostartManager(environment, backend, context.ConfigPath);
        if (manager.State() is { Running: true, Background: false } foreground)
        {
            var who = foreground.Pid is { } pid ? $"（PID {pid}）" : string.Empty;
            throw new CliException(ExitCodes.Error, $"這台電腦已經有本機代理在執行{who}，先關掉那個視窗再執行一次。");
        }

        if (!await manager.StopBackgroundAsync(call.CancellationToken))
        {
            throw new CliException(ExitCodes.Error, "舊的背景程序停不下來，沒有更動設定。請到工作管理員或活動監視器結束 kumi，再執行一次。" + (manager.SkipNote is { } note ? " " + note : string.Empty));
        }

        AutostartManager.TrimLog(spec.LogPath);
        await backend.InstallAsync(spec, call.CancellationToken);
        await backend.LaunchAsync(spec, call.CancellationToken);
        var running = await manager.WaitUntilRunningAsync(call.CancellationToken);
        return Report(call, spec, running);
    }

    public static async Task<int> DisableAsync(CommandCall call)
    {
        var environment = call.Context.Autostart;
        var backend = AutostartBackends.For(environment);
        var manager = backend is null ? null : new AutostartManager(environment, backend, call.Context.ConfigPath);
        var wasEnabled = backend?.IsInstalled() ?? false;
        var hadBackground = manager?.State() is { Running: true, Background: true };
        if (backend is not null)
        {
            // 不看鎖就先請啟動管理員收掉：剛啟動、還沒拿到鎖的背景程序，鎖看不出來，但管理員裡已經載入了。
            await backend.StopAsync(call.CancellationToken);
        }

        if (manager is not null && !await manager.StopBackgroundAsync(call.CancellationToken))
        {
            throw new CliException(ExitCodes.Error, "背景的本機代理停不下來，沒有取消自動啟動。請到工作管理員或活動監視器結束 kumi，再執行一次。" + (manager.SkipNote is { } note ? " " + note : string.Empty));
        }

        if (backend is not null)
        {
            await backend.RemoveAsync(call.CancellationToken);
        }

        var changed = wasEnabled || hadBackground;
        if (call.Json)
        {
            call.Out.WriteLine(new JsonObject { ["enabled"] = false, ["wasEnabled"] = wasEnabled }.ToJsonString());
        }
        else
        {
            call.Out.WriteLine(changed ? "已取消自動啟動，背景的本機代理已經停止。" : "沒有設定自動啟動。");
        }

        return ExitCodes.Success;
    }

    public static async Task<int> StatusAsync(CommandCall call)
    {
        var environment = call.Context.Autostart;
        var backend = AutostartBackends.For(environment);
        var logPath = environment.LogPath;
        var state = new AgentRunState(false, null, false);
        if (backend is not null)
        {
            var manager = new AutostartManager(environment, backend, call.Context.ConfigPath);
            state = manager.State();
            if (!state.Running && await backend.QueryPidAsync(call.CancellationToken) is { } launchdPid)
            {
                // 啟動管理員說有在跑，但還沒拿到鎖（剛啟動、或還在等網路）。
                state = new AgentRunState(true, launchdPid, true);
            }
        }

        var recent = logPath is null ? [] : AutostartManager.LogTail(logPath, StatusLogLines);
        var enabled = backend?.IsInstalled() ?? false;
        var usage = JobQuota.Read(call.Context.ConfigPath, () => DateTimeOffset.Now, state.Running);
        var report = new StatusReport(backend is not null, enabled, state, logPath, enabled && logPath is not null && !File.Exists(logPath), recent, usage);
        if (call.Json)
        {
            call.Out.WriteLine(StatusJson(report).ToJsonString());
        }
        else
        {
            WriteStatusText(call.Out, report);
        }

        return ExitCodes.Success;
    }

    private static async Task<AutostartSpec> CheckPreconditionsAsync(CommandCall call, AutostartEnvironment environment)
    {
        var context = call.Context;
        var kinds = AgentCommands.ResolveKinds(call.Arguments.Values("engine"));
        var (api, credentials) = AgentCommands.ConnectForAgent(call);
        if (credentials.Source == TokenSource.Environment)
        {
            throw new CliException(
                ExitCodes.Error, "背景執行讀不到環境變數裡的權杖，請先執行 kumi agent login 把權杖存進設定檔。");
        }

        await AgentCommands.EnsureScopesAsync(api, call.CancellationToken);
        if (!kinds.Any(kind => ExecutableLocator.Find(kind, context.GetEnvironmentVariable) is not null))
        {
            throw new CliException(
                ExitCodes.Error, "這台電腦的 PATH 上找不到 claude，也找不到 codex，背景的本機代理沒有東西可以提供。請先安裝並登入其中一個。");
        }

        if (environment.KumiPath is not { Length: > 0 } kumiPath || environment.LogPath is not { } logPath)
        {
            throw new CliException(ExitCodes.Error, "找不到 kumi 自己的執行檔路徑，沒辦法寫啟動項目。");
        }

        if (AutostartPathPolicy.Check(kumiPath, environment) is { } unsafePlace)
        {
            throw new CliException(ExitCodes.Error, unsafePlace);
        }

        var limits = AgentCommands.ApplyLimits(call, context.AgentOptions);

        return new AutostartSpec(
            kumiPath, credentials.Server,
            call.Arguments.Value("name") is { } name ? MachineIdentity.ResolveName(name) : null,
            [.. call.Arguments.Values("engine").Distinct()],
            call.Arguments.Has("insecure"), BackgroundEnvironment(context), logPath,
            AgentCommands.ParseLimit(call, "max-jobs-per-hour"), AgentCommands.ParseLimit(call, "max-jobs-per-day"),
            limits.AllowUntestedEngine);
    }

    /// <summary>launchd 啟動的程序不經過使用者的 shell：PATH 與找引擎要用的變數要自己帶。不帶任何權杖。</summary>
    private static IReadOnlyDictionary<string, string> BackgroundEnvironment(CliContext context)
    {
        string[] names = ["PATH", "XDG_CONFIG_HOME", ExecutableLocator.ClaudeVariable, ExecutableLocator.CodexVariable];
        return names
            .Select(name => (Name: name, Value: context.GetEnvironmentVariable(name)))
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .ToDictionary(pair => pair.Name, pair => pair.Value!);
    }

    private static int Report(CommandCall call, AutostartSpec spec, bool running)
    {
        if (call.Json)
        {
            call.Out.WriteLine(new JsonObject { ["enabled"] = true, ["running"] = running, ["logPath"] = spec.LogPath }.ToJsonString());
        }

        if (running)
        {
            if (!call.Json)
            {
                call.Out.WriteLine(
                    $"已設定：登入電腦時會自動在背景啟動本機代理，現在已經在執行。記錄檔：{spec.LogPath}。kumi agent status 看狀態，kumi agent disable 取消。");
                call.Out.WriteLine(
                    $"它會在背景用你的 AI 訂閱執行網頁送來的對話，本機上限是每小時 {Limit(spec.MaxJobsPerHour, JobQuota.DefaultPerHour)}、每天 {Limit(spec.MaxJobsPerDay, JobQuota.DefaultPerDay)}"
                    + "（--max-jobs-per-hour、--max-jobs-per-day 調整，0 是不限）。");
                if (call.Context.Autostart.Os == AutostartOs.Windows)
                {
                    call.Out.WriteLine("注意：Windows 的開機自動啟動是實驗性的，還沒有在真的 Windows 上完整驗證過；有問題請看記錄檔，或用 kumi agent disable 取消。");
                }
            }

            return ExitCodes.Success;
        }

        var tail = AutostartManager.LogTail(spec.LogPath, FailureLogLines);
        var detail = tail.Count == 0 ? "記錄檔裡還沒有東西。" : "記錄檔最後幾行：\n" + string.Join('\n', tail.Select(line => "  " + line));
        call.Error.WriteLine($"已設定登入時自動啟動，但背景程序 10 秒內沒有起來。記錄檔：{spec.LogPath}\n{detail}");
        return ExitCodes.Error;
    }

    /// <param name="LogMissing">設了自動啟動、記錄檔卻不在：多半是資料夾被清理工具刪掉，launchd 不會自己建回來。</param>
    /// <param name="Usage">這台電腦這一小時、今天領走的工作數與上限。</param>
    private sealed record StatusReport(
        bool Supported, bool Enabled, AgentRunState State, string? LogPath, bool LogMissing, IReadOnlyList<string> Recent, QuotaUsage Usage);

    private static string Limit(int? configured, int fallback)
    {
        var value = configured ?? fallback;
        return value == 0 ? "不限" : $"{value} 件";
    }

    private static JsonObject UsageJson(QuotaWindow window) => new() { ["used"] = window.Used, ["limit"] = window.Limit };

    private static string UsageText(QuotaWindow window) => window.IsUnlimited ? $"{window.Used} 件（不限）" : $"{window.Used} / {window.Limit} 件";

    private static JsonObject StatusJson(StatusReport report) => new()
    {
        ["supported"] = report.Supported,
        ["enabled"] = report.Enabled,
        ["running"] = report.State.Running,
        ["pid"] = report.State.Pid,
        ["background"] = report.State.Running ? report.State.Background : null,
        ["connection"] = report.State is { Running: true, Connection: { } connection }
            ? new JsonObject
            {
                ["state"] = connection.State,
                ["since"] = connection.Since.ToString("O", CultureInfo.InvariantCulture),
                ["error"] = connection.Error,
            }
            : null,
        ["logPath"] = report.LogPath,
        ["logMissing"] = report.LogMissing,
        ["usage"] = new JsonObject
        {
            ["hour"] = UsageJson(report.Usage.Hour),
            ["day"] = UsageJson(report.Usage.Day),
            ["limitsAreDefaults"] = report.Usage.LimitsAreDefaults,
        },
        ["recentLog"] = new JsonArray([.. report.Recent.Select(line => (JsonNode?)line)]),
    };

    private static void WriteStatusText(TextWriter output, StatusReport report)
    {
        var (state, logPath, recent) = (report.State, report.LogPath, report.Recent);
        output.WriteLine(!report.Supported ? "自動啟動：這個作業系統不支援（請自己開著 kumi agent start）"
            : report.Enabled ? "自動啟動：已設定（登入電腦時在背景啟動）" : "自動啟動：沒有設定（kumi agent enable 可以設定）");
        var pid = state.Pid is { } value ? $"，PID {value}" : string.Empty;
        output.WriteLine(!state.Running ? "執行中：沒有"
            : state.Background ? $"執行中：是（背景{pid}）" : $"執行中：是（前景，在某個終端機視窗裡{pid}）");
        if (state is { Running: true, Connection: { } connection })
        {
            WriteConnectionText(output, connection);
        }

        output.WriteLine($"領走的工作：這一小時 {UsageText(report.Usage.Hour)}，今天 {UsageText(report.Usage.Day)}"
            + (report.Usage.LimitsAreDefaults ? "（沒有在執行，上限是預設值；實際的上限看下次啟動時的設定）" : string.Empty));
        if (logPath is null)
        {
            return;
        }

        output.WriteLine($"記錄檔：{logPath}");
        if (report.LogMissing)
        {
            output.WriteLine("  記錄檔不見了（可能整個資料夾被清理工具刪掉），背景程序的輸出沒有留下來。執行 kumi agent enable 重新設定，記錄檔會重建。");
        }

        if (recent.Count > 0)
        {
            output.WriteLine("最近的記錄：");
            foreach (var line in recent)
            {
                output.WriteLine("  " + line);
            }
        }
    }

    private static void WriteConnectionText(TextWriter output, AgentConnection connection)
    {
        var since = connection.Since.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        switch (connection.State)
        {
            case AgentConnectionState.Connected:
                output.WriteLine($"連線：已連上伺服器（{since} 起）");
                break;
            case AgentConnectionState.Retrying:
                var reason = connection.Error is { Length: > 0 } error ? $"：{error}" : string.Empty;
                output.WriteLine($"連線：連不上或伺服器有問題，{since} 起一直在重試{reason}");
                output.WriteLine("  網路、VPN、proxy、防火牆或伺服器暫時的問題排除之後會自己接上，不用重新啟動。");
                break;
            default:
                output.WriteLine($"連線：正在連線（{since} 開始；連不上時要等一兩分鐘才會逾時）");
                break;
        }
    }
}
