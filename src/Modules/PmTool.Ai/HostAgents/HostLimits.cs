namespace PmTool.Ai.HostAgents;

/// <summary>主機代理的欄位長度與時間。合約（<see cref="AiRules"/>）沒有寫的那幾個，只有模組自己用。</summary>
internal static class HostLimits
{
    public const int MinMachineKeyLength = 8;

    public const int MaxMachineKeyLength = 64;

    public const int MaxVersionLength = 40;

    public const int MaxToolNameLength = 100;

    public const int MaxProjectKeyLength = 40;

    /// <summary>一件工作最多存幾則進度。</summary>
    public const int MaxEventsPerJob = 5000;

    /// <summary>worker 回報的一件工作的 token（輸入、輸出各自）最多多少；一則回覆不可能接近這個數字。</summary>
    public const long MaxReportedTokens = 1_000_000_000;

    /// <summary>伺服器自己的結束判斷（逾時、放棄）比轉送那一頭多給的寬限，兩邊的時鐘與等待之間的空檔。</summary>
    public static readonly TimeSpan JobGrace = TimeSpan.FromSeconds(10);

    /// <summary>整則回覆的上限除了排隊期限與執行期限之外多給的一分鐘（領走前後的來回）。</summary>
    public static readonly TimeSpan RunSlack = TimeSpan.FromMinutes(1);

    /// <summary>工具授權比執行的期限多給的時間。</summary>
    public static readonly TimeSpan ToolTokenSlack = TimeSpan.FromMinutes(1);

    /// <summary>預留之後多久還沒變成排隊的工作（程序在中間當掉）就退還。</summary>
    public static readonly TimeSpan ReservedExpiry = TimeSpan.FromMinutes(1);

    /// <summary>結束的工作最多留多久（轉送沒來得及刪的）。</summary>
    public static readonly TimeSpan JobRetention = TimeSpan.FromHours(1);

    /// <summary>提出類工具「進行中」的紀錄留多久，之後同樣的呼叫可以接手（原本的請求當掉了）。</summary>
    public static readonly TimeSpan PendingWriteExpiry = TimeSpan.FromMinutes(2);

    public const int MaxClaimScans = 50;
}

/// <summary>轉送與長輪詢的時間。預設是 <see cref="AiRules"/> 的值；測試換成短的，不必真的等 3 分鐘、60 秒。</summary>
/// <param name="QueueTimeout">排隊的期限。</param>
/// <param name="LeaseTimeout">領走之後多久沒有任何回報就當作結果不明。</param>
/// <param name="PollInterval">轉送讀進度的間隔。</param>
/// <param name="PositionInterval">排隊時重算位置的間隔。</param>
/// <param name="StopGrace">按「停止」之後最多等 worker 結束多久。</param>
/// <param name="PollWait">worker 登記時最多等多久工作。</param>
/// <param name="ClaimPollInterval">長輪詢沒被訊號叫醒時問資料庫的間隔。</param>
internal sealed record HostTimings(
    TimeSpan QueueTimeout,
    TimeSpan LeaseTimeout,
    TimeSpan PollInterval,
    TimeSpan PositionInterval,
    TimeSpan StopGrace,
    TimeSpan PollWait,
    TimeSpan ClaimPollInterval)
{
    public static HostTimings Default { get; } = new(
        AiRules.HostQueueTimeout,
        AiRules.HostLeaseTimeout,
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(10),
        AiRules.HostPollWait,
        TimeSpan.FromSeconds(2));
}
