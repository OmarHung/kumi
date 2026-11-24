using PmTool.Cli.Agent;
using PmTool.Cli.Agent.Autostart;

namespace PmTool.Cli.Tests.Agent.Autostart;

/// <summary>
/// 假的作業系統：家目錄是暫存資料夾，launchctl、登錄、背景啟動與結束程序都只記在記憶體裡。
/// 「背景程序」就是這個測試程序裡的一把真的 <see cref="AgentLock"/>，所以鎖的行為和真的一樣；不碰真的 launchd 與登錄。
/// </summary>
internal sealed class FakeAutostart : IDisposable
{
    public const int BackgroundPid = 4242;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"kumi-autostart-{Guid.NewGuid():N}");
    private readonly Dictionary<int, string> _processes = [];
    private AgentLock? _background;
    private string? _configPath;

    public FakeAutostart(AutostartOs os = AutostartOs.MacOS)
    {
        Os = os;
        Directory.CreateDirectory(Home);
    }

    public AutostartOs Os { get; }

    public string Home => Path.Combine(_directory, "home");

    public string PlistPath => Path.Combine(Home, "Library", "LaunchAgents", "kumi.local-agent.plist");

    public string LogPath => Os == AutostartOs.MacOS
        ? Path.Combine(Home, "Library", "Logs", "Kumi", "agent.log")
        : Path.Combine(_directory, "local", "Kumi", "agent.log");

    public string KumiPath { get; set; } = "/opt/kumi bin/kumi";

    /// <summary>執行過的系統指令與背景啟動，依序。</summary>
    public List<string> Calls { get; } = [];

    public Dictionary<string, string> Registry { get; } = [];

    /// <summary>false：啟動之後背景程序一直沒有拿到鎖（模擬起不來）。</summary>
    public bool LaunchSucceeds { get; set; } = true;

    /// <summary>true：launchctl bootout 停不掉它，只有結束程序才有用。</summary>
    public bool Stubborn { get; set; }

    public int HideConsoleCalls { get; private set; }

    public bool BackgroundRunning => _background is not null;

    public AutostartEnvironment Build(string configPath)
    {
        _configPath = configPath;
        return new AutostartEnvironment
        {
            Os = Os,
            HomeDirectory = Home,
            LocalAppData = Path.Combine(_directory, "local"),
            KumiPath = KumiPath,
            ProcessId = 7001,
            GetUid = _ => Task.FromResult("501"),
            RunCommand = RunAsync,
            StartBackground = StartBackgroundAsync,
            GetProcessName = pid => _processes.GetValueOrDefault(pid),
            KillProcess = (pid, _) =>
            {
                Calls.Add($"kill {pid}");
                StopBackground();
                return Task.FromResult(true);
            },
            ReadRunValue = name => Registry.GetValueOrDefault(name),
            WriteRunValue = (name, value) => Registry[name] = value,
            DeleteRunValue = name => Registry.Remove(name),
            HideConsole = () => HideConsoleCalls++,
            Delay = (_, _) => Task.CompletedTask,
            StartTimeout = TimeSpan.FromMilliseconds(50),
            StopTimeout = TimeSpan.FromMilliseconds(50),
            PollInterval = TimeSpan.FromMilliseconds(10),
        };
    }

    /// <summary>假裝有個程序（前景或背景）已經在跑，並拿著鎖。</summary>
    public AgentLock Occupy(bool background, int pid)
    {
        _processes[pid] = "kumi";
        return AgentLock.TryAcquire(_configPath!, pid, background).Lock!;
    }

    public void Dispose()
    {
        StopBackground();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 暫時資料夾刪不掉不影響測試。
        }
    }

    private Task<CommandOutcome> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        Calls.Add($"{file} {string.Join(' ', arguments)}");
        if (file != "launchctl")
        {
            return Task.FromResult(new CommandOutcome(1, string.Empty));
        }

        return Task.FromResult(arguments[0] switch
        {
            "bootout" => Bootout(),
            "bootstrap" => LaunchOutcome(),
            "print" => _background is null
                ? new CommandOutcome(113, "Could not find service")
                : new CommandOutcome(0, $"kumi.local-agent = {{\n\tstate = running\n\tpid = {BackgroundPid}\n}}"),
            _ => new CommandOutcome(1, string.Empty),
        });
    }

    private CommandOutcome LaunchOutcome()
    {
        // launchctl bootstrap 本身成功；程序有沒有真的起來是另一回事（LaunchSucceeds）。
        Launch();
        return new CommandOutcome(0, string.Empty);
    }

    private CommandOutcome Bootout()
    {
        if (_background is null)
        {
            return new CommandOutcome(3, "No such process");
        }

        if (!Stubborn)
        {
            StopBackground();
        }

        return new CommandOutcome(0, string.Empty);
    }

    private Task StartBackgroundAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        Calls.Add($"start {file} {string.Join(' ', arguments)}");
        Launch();
        return Task.CompletedTask;
    }

    private bool Launch()
    {
        if (!LaunchSucceeds)
        {
            return false;
        }

        _processes[BackgroundPid] = "kumi";
        _background = AgentLock.TryAcquire(_configPath!, BackgroundPid, background: true).Lock;
        return _background is not null;
    }

    public void StopBackground()
    {
        _background?.Dispose();
        _background = null;
        _processes.Remove(BackgroundPid);
    }
}
