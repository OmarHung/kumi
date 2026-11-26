namespace PmTool.Cli.Tests;

internal sealed record CliResult(int ExitCode, string Out, string Error);

/// <summary>
/// 在記憶體裡跑一次指令：假的伺服器、暫時的設定檔資料夾、環境變數、標準輸入。
/// 每個測試自己建一個，結束時刪掉資料夾。
/// </summary>
internal sealed class CliHarness : IDisposable
{
    public const string ServerUrl = "https://pm.example.com";
    public const string Token = "pmt_example_token_value";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pm-cli-tests-{Guid.NewGuid():N}");

    public CliHarness()
    {
        Directory.CreateDirectory(_directory);
        ConfigPath = Path.Combine(_directory, "kumi", "config.json");
        // 家目錄也指到測試自己的資料夾：skill 的位置（~/.claude、~/.codex）不能落到真正的家目錄。
        Environment["HOME"] = Path.Combine(_directory, "home");
        Update = new PmTool.Cli.Commands.UpdateEnvironment(
            Path.Combine(_directory, "install", "kumi"), true, PmTool.Cli.Commands.UpdateOs.MacOS,
            System.Runtime.InteropServices.Architecture.Arm64, System.Runtime.InteropServices.Architecture.Arm64,
            (_, _) => Task.FromResult<string?>($"kumi {PmTool.Cli.Commands.HelpText.VersionNumber}"));
    }

    public FakeServer Server { get; } = new();

    public string ConfigPath { get; }

    public Dictionary<string, string?> Environment { get; } = new();

    public string StandardInput { get; set; } = string.Empty;

    /// <summary>設了就當作標準輸入的原始串流（測試用它確認讀完碼之後，剩下的位元組還在）。</summary>
    public Stream? RawInput { get; set; }

    public bool IsInteractive { get; set; }

    public string? Secret { get; set; }

    /// <summary>長駐指令登記的「Ctrl-C 時要做的事」；測試呼叫它就等於使用者按了 Ctrl-C。</summary>
    public Action? Interrupt { get; private set; }

    /// <summary>本機代理的時間與數量；預設是正式的數字。</summary>
    internal PmTool.Cli.Agent.AgentOptions AgentOptions { get; set; } = new();

    /// <summary>主機 worker 的間隔；預設是正式的數字。</summary>
    internal PmTool.Cli.Host.HostOptions HostOptions { get; set; } = new();

    /// <summary>kumi update 的外部環境；預設是 macOS（Apple 晶片）上的單一執行檔，目標放在這個測試自己的資料夾，試跑回報目前的版本。</summary>
    internal PmTool.Cli.Commands.UpdateEnvironment Update { get; set; } = null!;

    /// <summary>開機自動啟動的外部環境；預設是什麼都沒有的假環境，絕不會碰到真的 launchd、登錄或家目錄。</summary>
    internal PmTool.Cli.Agent.Autostart.AutostartEnvironment Autostart { get; set; } = new();

    /// <summary>用環境變數登入（和 CI 一樣）。</summary>
    public CliHarness LoggedInByEnvironment()
    {
        Environment["KUMI_SERVER"] = ServerUrl;
        Environment["KUMI_TOKEN"] = Token;
        return this;
    }

    /// <summary>指令的「目前資料夾」：每個測試自己一個，下載預設存在這裡。</summary>
    public string WorkingDirectory => Directory.CreateDirectory(Path.Combine(_directory, "cwd")).FullName;

    public string PathOf(string name) => Path.Combine(_directory, name);

    public async Task<CliResult> RunAsync(params string[] args)
    {
        using var http = new HttpClient(Server, disposeHandler: false);
        var output = new StringWriter();
        var error = new StringWriter();
        var context = new CliContext
        {
            Http = http,
            Out = output,
            Error = error,
            In = new StringReader(StandardInput),
            RawInput = RawInput,
            ConfigPath = ConfigPath,
            WorkingDirectory = WorkingDirectory,
            GetEnvironmentVariable = name => Environment.GetValueOrDefault(name),
            IsInteractive = IsInteractive,
            ReadSecret = () => Secret,
            RegisterInterrupt = callback =>
            {
                Interrupt = callback;
                return new NoOp();
            },
            AgentOptions = AgentOptions,
            HostOptions = HostOptions,
            Update = Update,
            Autostart = Autostart,
        };
        var exitCode = await CliRunner.RunAsync(args, context, CancellationToken.None);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed class NoOp : IDisposable
    {
        public void Dispose()
        {
        }
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 暫時資料夾刪不掉不影響測試的結果。
        }
    }
}
