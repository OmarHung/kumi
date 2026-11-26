using System.Text.Json.Nodes;
using PmTool.Cli.Commands;

namespace PmTool.Cli.Agent.Autostart;

internal enum AgentRestartOutcome
{
    /// <summary>沒有在跑的本機代理（update 時不替使用者開一個）。</summary>
    NotRunning,

    /// <summary>在跑的是終端機視窗裡的 <c>kumi agent start</c>：只有使用者自己能重開。</summary>
    Foreground,

    /// <summary>這個作業系統不能自動重新啟動（Windows 還沒驗證、Linux 沒有開機自動啟動）。</summary>
    Unsupported,

    Restarted,

    Failed,
}

/// <param name="Pid">重新啟動成功時是新的程序，其他時候是原本在跑的（不知道是 null）。</param>
/// <param name="Detail">失敗時系統指令回的訊息（launchctl 的原文）；只是等不到新的程序時是 null。</param>
internal sealed record AgentRestartResult(AgentRestartOutcome Outcome, int? Pid = null, string? Detail = null)
{
    /// <summary><c>kumi update --json</c> 的 <c>agentRestart</c>，是對外合約。</summary>
    public string JsonValue => Outcome switch
    {
        AgentRestartOutcome.NotRunning => "not_running",
        AgentRestartOutcome.Foreground => "foreground",
        AgentRestartOutcome.Unsupported => "unsupported",
        AgentRestartOutcome.Restarted => "restarted",
        _ => "failed",
    };
}

/// <summary>
/// 換了執行檔之後讓背景的本機代理用新版重新開一次。已經在跑的程序記憶體裡是舊版，登記到伺服器的版本也是舊的，
/// 不重開的話伺服器會一直說要更新（<c>kumi update</c> 只換檔案）。是不是在跑一律看 <see cref="AgentLock"/>。
/// </summary>
internal static class AgentRestart
{
    private const int FailureLogLines = 5;

    /// <param name="onlyIfRunning">
    /// true（update 用）：沒在跑就不動；false（<c>kumi agent restart</c>）：設了開機自動啟動、程序卻不在時也照啟動項目開一個。
    /// </param>
    public static async Task<AgentRestartResult> RunAsync(CliContext context, bool onlyIfRunning, CancellationToken cancellation)
    {
        var environment = context.Autostart;
        var backend = AutostartBackends.For(environment);
        var holder = AgentLock.Peek(context.ConfigPath);
        if (holder is { Background: false })
        {
            return new AgentRestartResult(AgentRestartOutcome.Foreground, holder.Pid);
        }

        if (holder is null && (onlyIfRunning || backend?.IsInstalled() != true))
        {
            return new AgentRestartResult(AgentRestartOutcome.NotRunning);
        }

        if (backend is not { SupportsRestart: true })
        {
            return new AgentRestartResult(AgentRestartOutcome.Unsupported, holder?.Pid);
        }

        var outcome = await backend.RestartAsync(cancellation);
        if (outcome.ExitCode != 0)
        {
            return new AgentRestartResult(AgentRestartOutcome.Failed, holder?.Pid, outcome.Output.Trim());
        }

        var manager = new AutostartManager(environment, backend, context.ConfigPath);
        return await manager.WaitForReplacementAsync(holder, cancellation) is { Pid: var pid }
            ? new AgentRestartResult(AgentRestartOutcome.Restarted, pid)
            : new AgentRestartResult(AgentRestartOutcome.Failed, holder?.Pid);
    }

    /// <summary>kumi agent restart。</summary>
    public static async Task<int> CommandAsync(CommandCall call)
    {
        var result = await RunAsync(call.Context, onlyIfRunning: false, call.CancellationToken);
        switch (result.Outcome)
        {
            case AgentRestartOutcome.Restarted:
                if (call.Json)
                {
                    call.Out.WriteLine(new JsonObject { ["restarted"] = true, ["pid"] = result.Pid }.ToJsonString());
                }
                else
                {
                    call.Out.WriteLine($"背景的本機代理已經重新啟動（PID {result.Pid}）。kumi agent status 看它連上伺服器了沒。");
                }

                return ExitCodes.Success;
            case AgentRestartOutcome.Foreground:
                throw new CliException(ExitCodes.Error,
                    $"這台電腦的本機代理是在終端機視窗裡執行的{PidText(result.Pid)}，不是開機自動啟動的：到那個視窗按 Ctrl-C，再執行一次 kumi agent start。");
            case AgentRestartOutcome.NotRunning:
                throw new CliException(ExitCodes.Error, "沒有設定開機自動啟動，也沒有在背景執行的本機代理。要在背景執行請用 kumi agent enable。");
            case AgentRestartOutcome.Unsupported:
                throw new CliException(ExitCodes.Error, UnsupportedText(call.Context.Autostart));
            default:
                throw new CliException(ExitCodes.Error, FailureText(call.Context.Autostart, result));
        }
    }

    /// <summary>update 換好檔案之後印的話：重開了印在標準輸出，其他要使用者處理的印在標準錯誤；沒在跑什麼都不印。</summary>
    public static void Report(CommandCall call, AgentRestartResult result)
    {
        if (call.Json)
        {
            return;
        }

        var environment = call.Context.Autostart;
        switch (result.Outcome)
        {
            case AgentRestartOutcome.Restarted:
                call.Out.WriteLine("背景的本機代理已經用新版重新啟動。");
                break;
            case AgentRestartOutcome.Foreground:
                call.Error.WriteLine(
                    $"注意：本機代理還在用舊版執行{PidText(result.Pid)}，在某個終端機視窗裡：到那個視窗按 Ctrl-C，再執行一次 kumi agent start，才會換成新版。");
                break;
            case AgentRestartOutcome.Unsupported:
                call.Error.WriteLine($"注意：背景的本機代理還在用舊版執行{PidText(result.Pid)}。{UnsupportedText(environment)}");
                break;
            case AgentRestartOutcome.Failed:
                var detail = result.Detail is { Length: > 0 } text ? $"（{text}）" : string.Empty;
                call.Error.WriteLine($"警告：kumi 已經更新，但背景的本機代理沒能重新啟動{detail}。請執行 kumi agent status 看狀態，或 kumi agent restart 再試一次。");
                break;
        }
    }

    private static string PidText(int? pid) => pid is { } value ? $"（PID {value}）" : string.Empty;

    private static string UnsupportedText(AutostartEnvironment environment) => environment.Os == AutostartOs.Windows
        ? "Windows 還不能自動重新啟動背景的本機代理：登出再登入 Windows（或重開機），它就會用新版啟動。"
        : "這個作業系統沒有開機自動啟動，請自己結束那個 kumi 再重新執行 kumi agent start。";

    private static string FailureText(AutostartEnvironment environment, AgentRestartResult result)
    {
        if (result.Detail is { Length: > 0 } detail)
        {
            return $"launchctl 沒辦法重新啟動本機代理：{detail}。請執行 kumi agent enable 重新設定。";
        }

        var seconds = (int)(environment.StopTimeout + environment.StartTimeout).TotalSeconds;
        var tail = environment.LogPath is { } logPath ? AutostartManager.LogTail(logPath, FailureLogLines) : [];
        var log = tail.Count == 0 ? "記錄檔裡還沒有東西。" : "記錄檔最後幾行：\n" + string.Join('\n', tail.Select(line => "  " + line));
        return $"已經請系統重新啟動本機代理，但新的程序 {seconds} 秒內沒有起來。{log}";
    }
}
