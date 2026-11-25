using PmTool.Abstractions;

namespace PmTool.Ai.Assistant;

/// <summary>助理回的失敗。代碼是對外合約（<see cref="AiErrorCodes"/>），訊息給人看。</summary>
internal static class AssistantFailures
{
    public static Failure Forbidden() =>
        new(FailureKind.Forbidden, AiErrorCodes.AssistantForbidden, "你沒有讀這個專案設定的權限，不能用 AI 助理。");

    public static Failure ConversationNotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.ConversationNotFound, "找不到這則對話。");

    public static Failure Busy() =>
        new(FailureKind.Conflict, AiErrorCodes.ConversationBusy, "這則對話正在回覆，請等它結束，或按「停止」再送。");

    public static Failure TooManyRuns() =>
        new(FailureKind.Conflict, AiErrorCodes.TooManyRuns,
            $"你同時最多只能有 {AiRules.MaxConcurrentRunsPerPerson} 則對話在回覆，請等其中一則結束再送。");

    public static Failure TooLong() =>
        new(FailureKind.Conflict, AiErrorCodes.ConversationTooLong,
            $"這則對話已經很長了（最多 {AiRules.MaxMessagesPerConversation} 則訊息），請開一則新的對話。");

    public static Failure Unreadable() =>
        new(FailureKind.Conflict, AiErrorCodes.ConversationUnreadable, "這則對話之前的內容讀不出來，沒辦法接著聊，請開一則新的對話。");

    public static Failure UsageLimit(string message) => new(FailureKind.Conflict, AiErrorCodes.UsageLimitReached, message);

    public static Failure MessageInvalid(string message) => new(FailureKind.Invalid, AiErrorCodes.MessageInvalid, message);

    public static Failure Unavailable() =>
        new(FailureKind.Invalid, AiErrorCodes.Unavailable, "組織還沒有開啟 AI 助理，請組織管理員到「AI 設定」開啟，或允許成員使用自己的 AI 帳號。");

    public static Failure McpUrlMissing() =>
        new(FailureKind.Invalid, AiErrorCodes.SelectionInvalid, "這個部署還沒設定對外的網址，本機的引擎連不到 Kumi，請聯絡管理員（Security:PublicHost）。");
}
