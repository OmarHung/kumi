namespace PmTool.Cli.Agent.Autostart;

internal enum AutostartOs
{
    MacOS,
    Windows,
    Other,
}

/// <summary>跑完一個系統指令的結果；<see cref="Output"/> 是標準輸出加標準錯誤。</summary>
internal sealed record CommandOutcome(int ExitCode, string Output);

/// <summary>
/// 本機代理的開機自動啟動碰得到的外部環境：作業系統、家目錄、kumi 自己的路徑、uid、跑系統指令（launchctl）、
/// 背景啟動與結束程序、Windows 的登錄。測試換成假的，不真的裝到使用者的電腦上。
/// 預設值是「什麼都沒有」（不支援的作業系統）；真的環境見 <see cref="Current"/>。
/// </summary>
internal sealed record AutostartEnvironment
{
    public AutostartOs Os { get; init; } = AutostartOs.Other;

    public string HomeDirectory { get; init; } = string.Empty;

    /// <summary>Windows 的 %LOCALAPPDATA%；記錄檔放這裡。</summary>
    public string? LocalAppData { get; init; }

    /// <summary>kumi 自己的完整路徑（啟動項目要寫它）。</summary>
    public string? KumiPath { get; init; }

    public int ProcessId { get; init; } = 1;

    /// <summary>macOS 的使用者編號（launchctl 的 gui/&lt;uid&gt;）；用到才查。</summary>
    public Func<CancellationToken, Task<string>> GetUid { get; init; } = static _ => Task.FromResult("501");

    public Func<string, IReadOnlyList<string>, CancellationToken, Task<CommandOutcome>> RunCommand { get; init; } =
        static (_, _, _) => Task.FromResult(new CommandOutcome(1, string.Empty));

    /// <summary>在背景啟動一個不跟著這個視窗結束的程序（Windows 沒有 launchd，靠它立刻啟動）。</summary>
    public Func<string, IReadOnlyList<string>, CancellationToken, Task> StartBackground { get; init; } =
        static (_, _, _) => Task.CompletedTask;

    /// <summary>程序的名稱（不含副檔名）；程序不存在是 null。結束程序之前用它確認真的是 kumi。</summary>
    public Func<int, string?> GetProcessName { get; init; } = static _ => null;

    /// <summary>結束整個程序樹並等它結束；回傳是否真的結束了。</summary>
    public Func<int, CancellationToken, Task<bool>> KillProcess { get; init; } = static (_, _) => Task.FromResult(true);

    public Func<string, string?> ReadRunValue { get; init; } = static _ => null;

    public Action<string, string> WriteRunValue { get; init; } = static (_, _) => { };

    /// <summary>刪掉登錄的值；原本就沒有回 false。</summary>
    public Func<string, bool> DeleteRunValue { get; init; } = static _ => false;

    /// <summary>藏起自己的主控台視窗（Windows 登入時啟動的背景程序不該跳出視窗）。</summary>
    public Action HideConsole { get; init; } = static () => { };

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>啟動後等背景程序拿到鎖最久多久。</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>停掉背景程序最久等多久，之後才強制結束。</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    public string? LogDirectory => Os switch
    {
        AutostartOs.MacOS => Path.Combine(HomeDirectory, "Library", "Logs", "Kumi"),
        AutostartOs.Windows => Path.Combine(LocalAppData ?? Path.Combine(HomeDirectory, "AppData", "Local"), "Kumi"),
        _ => null,
    };

    public string? LogPath => LogDirectory is { } directory ? Path.Combine(directory, "agent.log") : null;

    /// <summary>真的環境。只讀得到的值（不跑程序、不碰檔案）在這裡算；要花力氣的都包在委派裡，用到才執行。</summary>
    public static AutostartEnvironment Current() => new()
    {
        Os = OperatingSystem.IsMacOS() ? AutostartOs.MacOS : OperatingSystem.IsWindows() ? AutostartOs.Windows : AutostartOs.Other,
        HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        KumiPath = Environment.ProcessPath,
        ProcessId = Environment.ProcessId,
        GetUid = AutostartSystem.GetUidAsync,
        RunCommand = AutostartSystem.RunAsync,
        StartBackground = AutostartSystem.StartDetachedAsync,
        GetProcessName = AutostartSystem.ProcessName,
        KillProcess = AutostartSystem.KillAsync,
        ReadRunValue = AutostartSystem.ReadRunValue,
        WriteRunValue = AutostartSystem.WriteRunValue,
        DeleteRunValue = AutostartSystem.DeleteRunValue,
        HideConsole = AutostartSystem.HideConsole,
    };
}
