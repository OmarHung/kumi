namespace PmTool.Cli.Agent;

/// <summary>
/// 本機代理的時間與數量。預設值是規格寫的數字；拆出來是為了測試不必真的等 5 分鐘。
/// </summary>
internal sealed record AgentOptions
{
    /// <summary>同時最多跑幾件（也是登記時的 capacity 上限）。</summary>
    public int MaxConcurrentJobs { get; init; } = 2;

    /// <summary>進度每隔多久回報一次。</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>累積幾則就不等時間、馬上回報。</summary>
    public int FlushBatch { get; init; } = 50;

    /// <summary>伺服器一個請求最多收幾則（<c>AiRules.MaxLocalAgentEventsPerReport</c>）。</summary>
    public int MaxEventsPerReport { get; init; } = 200;

    /// <summary>一則文字最長幾個字（<c>AiRules.MaxLocalAgentTextDelta</c>），超過的切成好幾則。</summary>
    public int MaxTextDelta { get; init; } = 8000;

    /// <summary>沒有新進度時多久送一次空的回報：伺服器 120 秒沒收到回報就當作失敗，使用者按「停止」也是靠這個才知道。</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>本機的逾時：<c>ReplyTimeout</c>（5 分鐘）加 30 秒，伺服器那邊先逾時時，這裡才有機會收到取消。</summary>
    public TimeSpan EngineTimeout { get; init; } = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30);

    /// <summary>取消時送出中斷訊號之後等多久，還沒結束就強制結束整個程序樹。</summary>
    public TimeSpan InterruptGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>引擎已經回報結果之後，等它自己結束多久。</summary>
    public TimeSpan ExitGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>引擎被結束之後，等標準輸出讀完多久（孫程序可能還抓著管線）。</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>查版本、登入狀態這類不送提示的指令最多等多久。</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>登記後（長輪詢）很快就回來時，兩次之間至少隔多久，避免伺服器出狀況時狂打。</summary>
    public TimeSpan MinPollGap { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>同時在跑的件數已滿時，多久登記一次（讓伺服器知道還在線；在線的判定是 60 秒內）。</summary>
    public TimeSpan BusyCheckInInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>探 Claude Code 的別名實際指到哪個模型，每個最多等多久。</summary>
    public TimeSpan ModelProbeTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>送出結束與最後一批進度的時限；Ctrl-C 之後不能讓使用者等很久。</summary>
    public TimeSpan FinishTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>每件工作的暫存資料夾放在哪裡；null 是系統的暫存資料夾。測試指到自己的資料夾，才看得出有沒有刪乾淨。</summary>
    public string? WorkRoot { get; init; }

    /// <summary>刪暫存資料夾失敗時，等多久再試一次。</summary>
    public TimeSpan DeleteRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>下載附件時，一次讀等多久都沒有資料就當作斷線。</summary>
    public TimeSpan DownloadIdleTimeout { get; init; } = AttachmentStaging.DefaultIdleTimeout;

    /// <summary>等待用；測試換成假的，才不必真的等。</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
}
