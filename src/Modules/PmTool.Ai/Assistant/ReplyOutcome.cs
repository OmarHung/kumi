namespace PmTool.Ai.Assistant;

/// <summary>一則回覆怎麼結束的：狀態與（中斷、失敗時）給人看的原因。</summary>
internal sealed record ReplyOutcome(AssistantMessageStatus Status, string? Error);
