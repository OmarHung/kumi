namespace PmTool.Ai.LocalAgents;

/// <summary>本機代理的欄位長度與時間。合約（<see cref="AiRules"/>）沒有寫的那幾個，只有模組自己用。</summary>
internal static class LocalAgentLimits
{
    public const int MinMachineKeyLength = 8;

    public const int MaxMachineKeyLength = 64;

    public const int MaxVersionLength = 40;

    public const int MaxToolNameLength = 100;

    public const int MaxCallIdLength = 100;

    /// <summary>
    /// 領工作時，沒被同一個程序的訊號叫醒也每隔多久問一次資料庫（工作可能是別的 API 程序收進來的）。
    /// 每次只是一個不開交易的 EXISTS，有才開交易領。
    /// </summary>
    public static readonly TimeSpan ClaimPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>領工作與逾時的判斷多給的寬限：兩邊的時鐘與等待之間的空檔。</summary>
    public static readonly TimeSpan JobGrace = TimeSpan.FromSeconds(10);

    /// <summary>一則回覆裡不同的工具呼叫最多幾個（模型自己的上限是 <see cref="AiRules.MaxToolCallsPerReply"/>，本機代理是外部送來的，給兩倍）。</summary>
    public const int MaxToolCallsPerReply = AiRules.MaxToolCallsPerReply * 2;

    /// <summary>一件工作最多交給引擎幾個圖片與 PDF（整段對話合計；超過的換成說明）。kumi 收到更多的會拒絕這件工作。</summary>
    public const int MaxJobAttachments = 50;

    /// <summary>一件工作最多存幾則進度。</summary>
    public const int MaxEventsPerJob = 5000;

    /// <summary>送出之後多久沒有結束的工作就當作被丟下了，登記時順手清掉。</summary>
    public static readonly TimeSpan JobRetention = TimeSpan.FromHours(1);
}
