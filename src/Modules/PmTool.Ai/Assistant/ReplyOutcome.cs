namespace PmTool.Ai.Assistant;

/// <summary>一則回覆怎麼結束的：狀態與（中斷、失敗時）給人看的原因。</summary>
internal sealed record ReplyOutcome(AssistantMessageStatus Status, string? Error)
{
    /// <summary>本機或主機的引擎自己回報的失敗（不是沒人領、逾時、失聯這類伺服器這邊判定的）。</summary>
    public bool ReportedByEngine { get; init; }
}
