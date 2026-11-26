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
        new(FailureKind.Invalid, AiErrorCodes.Unavailable, "組織還沒有開放可以用的 AI。請組織管理員到「AI 助理設定」開放組織的 API 金鑰，或允許成員使用自己的 AI 帳號；也可以請組織擁有者設定主機 AI 代理。");

    public static Failure OrganizationChatUnsupported() =>
        new(FailureKind.Invalid, AiErrorCodes.OrganizationChatUnsupported, "不選專案的對話不能選項目或附上附件，請到專案的 AI 助理。");

    /// <summary>
    /// 不選專案的對話要用本機或主機的引擎、而那邊的 kumi 太舊：舊版一定要有專案代號，也不認得新的工具，送過去整件工作會失敗。
    /// 本機的請使用者更新自己的電腦，主機的請組織擁有者更新 ai-worker。
    /// </summary>
    public static Failure AgentUpdateRequired(Attachments.AgentSource source)
    {
        var version = string.IsNullOrWhiteSpace(source.Version) ? "不明" : source.Version;
        var minimum = AiRules.MinAgentVersionForOrganizationChat;
        var message = source.Hosted
            ? $"「{source.Name}」的 ai-worker（kumi {version}）要 {minimum} 以上才能用在不選專案的對話，請組織擁有者更新主機的 ai-worker；在那之前請選一個專案，或換成 API 的模型。"
            : $"「{source.Name}」那台電腦上的 kumi 是 {version}，要 {minimum} 以上才能用在不選專案的對話，請執行 kumi update；在那之前請選一個專案，或換成 API 的模型。";
        return new(FailureKind.Conflict, AiErrorCodes.AgentUpdateRequired, message);
    }

    public static Failure McpUrlMissing() =>
        new(FailureKind.Invalid, AiErrorCodes.SelectionInvalid, "這個部署還沒設定對外的網址，本機的引擎連不到 Kumi，請聯絡管理員（Security:PublicHost）。");
}
