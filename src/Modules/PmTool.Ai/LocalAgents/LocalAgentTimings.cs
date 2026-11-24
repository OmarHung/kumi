namespace PmTool.Ai.LocalAgents;

/// <summary>轉送回覆的時間。預設是 <see cref="AiRules"/> 的值；測試換成短的，不必真的等 20 秒、120 秒。</summary>
/// <param name="ClaimTimeout">送出之後多久沒被領走就失敗。</param>
/// <param name="IdleTimeout">領走之後多久沒有任何回報就失敗。</param>
/// <param name="PollInterval">讀進度的間隔。</param>
/// <param name="StopGrace">按「停止」之後最多等本機代理結束多久。</param>
internal sealed record LocalAgentTimings(TimeSpan ClaimTimeout, TimeSpan IdleTimeout, TimeSpan PollInterval, TimeSpan StopGrace)
{
    public static LocalAgentTimings Default { get; } = new(
        AiRules.LocalAgentClaimTimeout, AiRules.LocalAgentIdleTimeout, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10));
}
