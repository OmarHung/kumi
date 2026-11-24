namespace PmTool.Cli.Agent.Autostart;

/// <summary>
/// 要寫進啟動項目的內容。刻意沒有權杖：背景程序從設定檔讀自己的權杖，啟動項目（plist、登錄）任何人讀得到。
/// </summary>
internal sealed record AutostartSpec(
    string KumiPath,
    string Server,
    string? Name,
    IReadOnlyList<string> Engines,
    bool Insecure,
    IReadOnlyDictionary<string, string> Environment,
    string LogPath)
{
    /// <summary>kumi 後面接的參數（不含 kumi 自己）。</summary>
    public IReadOnlyList<string> Arguments
    {
        get
        {
            var arguments = new List<string> { "agent", "start", "--service", "--server", Server };
            if (Name is { Length: > 0 })
            {
                arguments.AddRange(["--name", Name]);
            }

            foreach (var engine in Engines)
            {
                arguments.AddRange(["--engine", engine]);
            }

            if (Insecure)
            {
                arguments.Add("--insecure");
            }

            return arguments;
        }
    }
}

/// <summary>開機自動啟動在某個作業系統上的作法。</summary>
internal interface IAutostartBackend
{
    bool IsInstalled();

    /// <summary>寫入啟動項目（不啟動）。</summary>
    Task InstallAsync(AutostartSpec spec, CancellationToken cancellation);

    /// <summary>照啟動項目立刻在背景啟動。</summary>
    Task LaunchAsync(AutostartSpec spec, CancellationToken cancellation);

    /// <summary>請背景程序停下來（不保證已經停了，呼叫端會看鎖）。</summary>
    Task StopAsync(CancellationToken cancellation);

    /// <summary>移除啟動項目；原本就沒有回 false。</summary>
    Task<bool> RemoveAsync(CancellationToken cancellation);

    /// <summary>系統自己的啟動管理員看到的背景程序 PID（沒有或查不到是 null）。</summary>
    Task<int?> QueryPidAsync(CancellationToken cancellation);
}

internal static class AutostartBackends
{
    public static IAutostartBackend? For(AutostartEnvironment environment) => environment.Os switch
    {
        AutostartOs.MacOS => new LaunchAgentBackend(environment),
        AutostartOs.Windows => new WindowsRunBackend(environment),
        _ => null,
    };
}
