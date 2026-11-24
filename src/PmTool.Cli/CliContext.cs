namespace PmTool.Cli;

/// <summary>
/// 指令執行時碰得到的一切：網路、輸出入、環境變數、設定檔的位置。
/// 全部由外面交進來，測試才能用假的伺服器與記憶體裡的輸出跑完整個指令。
/// </summary>
public sealed class CliContext
{
    /// <summary>送請求用。請求一律用絕對網址，不看它的 BaseAddress。</summary>
    public required HttpClient Http { get; init; }

    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    public required TextReader In { get; init; }

    /// <summary>
    /// 標準輸入的原始位元組串流。從管線讀「碼」時一個位元組一個位元組讀到換行，後面的內容（例如 codex 要的金鑰）才會留給子程序；
    /// <see cref="In"/> 會一次讀進緩衝。沒有時退回 <see cref="In"/>。
    /// </summary>
    public Stream? RawInput { get; init; }

    /// <summary>設定檔的完整路徑。</summary>
    public required string ConfigPath { get; init; }

    /// <summary>讀環境變數；沒設定回 null。</summary>
    public Func<string, string?> GetEnvironmentVariable { get; init; } = _ => null;

    /// <summary>相對路徑與預設的下載位置以它為準；測試用它隔開真正的目前資料夾。</summary>
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();

    /// <summary>標準輸入與輸出都接在終端機上：只有這時候才可以問問題。</summary>
    public bool IsInteractive { get; init; }

    /// <summary>互動時讀一行不回顯的文字（貼權杖用）；沒有終端機時是 null。</summary>
    public Func<string?>? ReadSecret { get; init; }

    /// <summary>
    /// 登記「使用者按了 Ctrl-C 或程序被要求結束」時要做的事，回傳的物件 Dispose 時取消登記。
    /// 只有長駐的指令（kumi agent start）用得到：其他指令照舊，Ctrl-C 直接結束程序。
    /// </summary>
    public Func<Action, IDisposable> RegisterInterrupt { get; init; } = static _ => NoRegistration.Instance;

    /// <summary>本機代理的時間與數量；測試換成短的。</summary>
    internal Agent.AgentOptions AgentOptions { get; init; } = new();

    /// <summary>主機 worker（kumi host run）的間隔；測試換成短的。</summary>
    internal Host.HostOptions HostOptions { get; init; } = new();

    /// <summary>kumi update 碰得到的外部環境（執行檔路徑、作業系統、試跑）；測試換成假的，不真的換掉測試程式自己。</summary>
    internal Commands.UpdateEnvironment Update { get; init; } = Commands.UpdateEnvironment.Current();

    /// <summary>瀏覽器登入（kumi auth login、kumi agent login、kumi setup）碰得到的外部環境；測試換成假的，不真的開瀏覽器。</summary>
    internal PmTool.Cli.Login.LoginEnvironment Login { get; init; } = PmTool.Cli.Login.LoginEnvironment.Current();

    /// <summary>本機代理的開機自動啟動（kumi agent enable／disable／status）碰得到的外部環境；測試換成假的。</summary>
    internal Agent.Autostart.AutostartEnvironment Autostart { get; init; } = Agent.Autostart.AutostartEnvironment.Current();

    /// <summary>輸出換成會濾掉控制字元的版本；其他不變。</summary>
    internal CliContext WithSafeOutput() => new()
    {
        Http = Http,
        Out = new Output.SafeTextWriter(Out),
        Error = new Output.SafeTextWriter(Error),
        In = In,
        RawInput = RawInput,
        ConfigPath = ConfigPath,
        WorkingDirectory = WorkingDirectory,
        GetEnvironmentVariable = GetEnvironmentVariable,
        IsInteractive = IsInteractive,
        ReadSecret = ReadSecret,
        RegisterInterrupt = RegisterInterrupt,
        AgentOptions = AgentOptions,
        HostOptions = HostOptions,
        Update = Update,
        Login = Login,
        Autostart = Autostart,
    };

    private sealed class NoRegistration : IDisposable
    {
        public static readonly NoRegistration Instance = new();

        public void Dispose()
        {
        }
    }
}
