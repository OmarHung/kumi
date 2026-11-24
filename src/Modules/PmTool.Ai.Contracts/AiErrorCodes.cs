namespace PmTool.Ai;

/// <summary>AI 模組回的失敗代碼。代碼是對外合約，不能改名。規格見 <c>internal/HANDOFF-ai-assistant.md</c>。</summary>
public static class AiErrorCodes
{
    /// <summary>AI 設定（供應商的連線、預設的模型）只有組織管理員能讀、能改（403）。</summary>
    public const string Forbidden = "ai.forbidden";

    /// <summary>設定或連線的內容不對（422）。細項的代碼是下面五個，路徑是欄位名稱（camelCase）。</summary>
    public const string SettingsInvalid = "ai.settings_invalid";

    /// <summary>
    /// 連線剛被另一個管理員改過（409）：這邊連供應商的那幾秒裡，同一個連線的內容變了，不拿過期的清單去蓋。
    /// 沒有細項；重新整理再試。
    /// </summary>
    public const string SettingsConflict = "ai.settings_conflict";

    /// <summary>細項：這個欄位一定要填。</summary>
    public const string FieldRequired = "ai.field_required";

    /// <summary>細項：格式不對、太長、有控制字元、重複。</summary>
    public const string FieldInvalid = "ai.field_invalid";

    /// <summary>細項：基底網址指到不允許的位址（私有網段、本機）；部署設了 <c>Ai:AllowPrivateEndpoints</c> 才能連。</summary>
    public const string EndpointNotAllowed = "ai.endpoint_not_allowed";

    /// <summary>細項：預設的連線、模型或思考深度不在可用的清單裡。</summary>
    public const string ModelNotAvailable = "ai.model_not_available";

    /// <summary>細項：一個組織最多 <see cref="AiRules.MaxConnectionsPerOrganization"/> 個連線。</summary>
    public const string TooManyConnections = "ai.too_many_connections";

    /// <summary>連不上供應商、金鑰不對、讀不到模型清單（422），訊息說明原因。</summary>
    public const string ProviderFailed = "ai.provider_failed";

    /// <summary>找不到這個連線（404）。</summary>
    public const string ConnectionNotFound = "ai.connection_not_found";

    /// <summary>組織沒有開啟 AI，或沒有任何可以用的模型（422）。</summary>
    public const string Unavailable = "ai.unavailable";

    /// <summary>選的連線、模型或思考深度現在不能用（422）：被管理員關掉、移除，或這個模型沒有這個等級。</summary>
    public const string SelectionInvalid = "ai.selection_invalid";

    /// <summary>沒有讀這個專案設定的權限（blueprint.read），不能用 AI 助理（403）。</summary>
    public const string AssistantForbidden = "ai.assistant_forbidden";

    /// <summary>找不到這則對話（404）。別人的對話也是這個代碼，不透露它存不存在。</summary>
    public const string ConversationNotFound = "ai.conversation_not_found";

    /// <summary>這則對話正在回覆，等它結束或按「停止」再送（409）。</summary>
    public const string ConversationBusy = "ai.conversation_busy";

    /// <summary>同時在回覆的對話太多（409），一個人最多 <see cref="AiRules.MaxConcurrentRunsPerPerson"/> 則。</summary>
    public const string TooManyRuns = "ai.too_many_runs";

    /// <summary>對話太長了（409），請開新的對話。上限是 <see cref="AiRules.MaxMessagesPerConversation"/> 則訊息。</summary>
    public const string ConversationTooLong = "ai.conversation_too_long";

    /// <summary>送出的訊息不對（422）：空的、太長、有控制字元。</summary>
    public const string MessageInvalid = "ai.message_invalid";

    /// <summary>
    /// 這則對話之前的訊息讀不出來（409），沒辦法接著聊：資料壞了，或存的格式後來改過。
    /// 什麼都不會存、也不會留下「正在回覆」的狀態；請開一則新的對話。
    /// </summary>
    public const string ConversationUnreadable = "ai.conversation_unreadable";

    /// <summary>組織沒有允許成員接自己的 AI 帳號（403）。</summary>
    public const string PersonalDisabled = "ai.personal_disabled";

    /// <summary>「用 OpenRouter 登入」沒有完成（422）：登入的連結過期或不是這個人的、換不到金鑰，訊息說明原因。</summary>
    public const string LoginFailed = "ai.login_failed";

    /// <summary>本機代理送來的內容不對（422）：電腦名稱、引擎、模型、進度的格式或長度，訊息說明哪裡不對。</summary>
    public const string AgentInvalid = "ai.agent_invalid";

    /// <summary>本機代理回報的工作不存在、不是這個人領的，或已經結束（404）。本機代理收到時停下引擎、不再回報。</summary>
    public const string AgentJobNotFound = "ai.agent_job_not_found";

    /// <summary>送出訊息時選的項目不在這個專案、看不到或不存在（404）。什麼都不存。</summary>
    public const string ItemNotFound = "ai.item_not_found";

    /// <summary>找不到這份修改建議（404）。別人的也是這個代碼。</summary>
    public const string ItemProposalNotFound = "ai.item_proposal_not_found";

    /// <summary>這份修改建議已經套用或作廢了（409）。</summary>
    public const string ItemProposalClosed = "ai.item_proposal_closed";

    /// <summary>主機代理的管理與登入只有組織擁有者能做（403）；管理員也不行。每次都向身分模組重新確認，不看登入當時的角色。</summary>
    public const string HostOwnerOnly = "ai.host_owner_only";

    /// <summary>找不到這台主機代理（404）。別的組織的也是這個代碼。</summary>
    public const string HostAgentNotFound = "ai.host_agent_not_found";

    /// <summary>主機代理還沒配對或已停用，不能開始登入（422）。</summary>
    public const string HostAgentUnavailable = "ai.host_agent_unavailable";

    /// <summary>找不到這個組織來源（404）。</summary>
    public const string HostSourceNotFound = "ai.host_source_not_found";

    /// <summary>找不到這次登入（404）。</summary>
    public const string HostLoginNotFound = "ai.host_login_not_found";

    /// <summary>配對碼不對、過期、用過了，或開始配對的人已經不是擁有者（422）。不說是哪一項。</summary>
    public const string HostPairingInvalid = "ai.host_pairing_invalid";

    /// <summary>
    /// 主機上的登入沒有被接受（422）：登入碼不對、過期、用過（重放）、開始登入的人已經不是擁有者、CLI 沒登入，或登入的是訂閱帳號。
    /// 訊息說明原因；worker 收到時執行 CLI 的登出。
    /// </summary>
    public const string HostLoginRejected = "ai.host_login_rejected";

    /// <summary>引擎登入的是訂閱帳號（或判斷不出計費方式），條款不允許給全組織共用，來源不能開（422）。</summary>
    public const string HostBillingNotShareable = "ai.host_billing_not_shareable";

    /// <summary>主機上的來源現在不能用（422）：關掉了、代理離線或停用、引擎需要擁有者重新授權。訊息說明原因；不會改用別的付費來源。</summary>
    public const string HostSourceUnavailable = "ai.host_source_unavailable";

    /// <summary>排隊滿了（409）：你在這個來源已經有一則在等（每人 <see cref="AiRules.HostMaxQueuedPerPerson"/> 則），或整個來源的隊伍已滿。</summary>
    public const string HostQueueFull = "ai.host_queue_full";

    /// <summary>用到擁有者設的上限了（409）：你今天的則數、這個來源這個月的則數或 token。訊息說是哪一個。</summary>
    public const string HostQuotaExceeded = "ai.host_quota_exceeded";

    /// <summary>worker 的回報被拒絕（409）：租約不對，或工作已經被伺服器結束（逾時、結果不明、取消）。worker 要停下引擎、不再回報。</summary>
    public const string HostJobLeaseLost = "ai.host_job_lease_lost";

    /// <summary>worker 送來的內容不對（422）：網址、引擎、模型、流水號的格式或範圍。訊息說明哪裡不對。</summary>
    public const string HostAgentInvalid = "ai.host_agent_invalid";
}
