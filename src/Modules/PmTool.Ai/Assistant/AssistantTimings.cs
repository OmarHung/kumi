namespace PmTool.Ai.Assistant;

/// <summary>回覆的時間限制。預設是 <see cref="AiRules"/> 的值；測試換成短的，不必真的等五分鐘。</summary>
internal sealed record AssistantTimings(TimeSpan ReplyTimeout, TimeSpan RunLease)
{
    public static AssistantTimings Default { get; } = new(AiRules.ReplyTimeout, AiRules.RunLease);
}
