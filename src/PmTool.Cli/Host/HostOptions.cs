namespace PmTool.Cli.Host;

/// <summary>
/// <c>kumi host run</c> 的間隔。預設值是交接第 8.1 節的數字；拆出來是為了測試不必真的等 30 秒。
/// 等待本身用 <see cref="Agent.AgentOptions.Delay"/>（測試換成假的）。
/// </summary>
internal sealed record HostOptions
{
    /// <summary>還沒配對時，多久看一次狀態檔。</summary>
    public TimeSpan PairingPoll { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>機器憑證失效（401）之後多久再試一次。</summary>
    public TimeSpan InvalidCredentialRetry { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>憑證失效的等待期間，多久看一次狀態檔有沒有換成新的憑證。</summary>
    public TimeSpan StateRecheck { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>重新探測兩個引擎的間隔。</summary>
    public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>網路錯誤的退避：1 秒起、倍增、最多 30 秒。</summary>
    public TimeSpan MinBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 代理被停用時多久再登記一次。伺服器對停用的代理也是長輪詢（最多 25 秒才回），兩者加起來要小於在線的判定（60 秒），
    /// 擁有者在網頁上才看得到它「在線、已停用」。
    /// </summary>
    public TimeSpan DisabledCheckIn { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>伺服器轉址（例如要求 https）時多久再試一次：設定不改不會好，不必退避得很密。</summary>
    public TimeSpan RedirectRetry { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>同時最多跑幾件（<c>AiRules.HostWorkerMaxConcurrent</c>）。</summary>
    public int MaxConcurrentJobs { get; init; } = 2;
}
