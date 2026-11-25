namespace PmTool.Ai.Persistence;

/// <summary>主機代理的狀態（<c>ai.host_agents.state</c> 的固定文字）。</summary>
internal static class HostAgentStates
{
    public const string AwaitingPairing = "awaiting_pairing";

    public const string Active = "active";

    public const string Disabled = "disabled";
}

/// <summary>
/// <c>ai.host_agents</c> 的一列：擁有者新增的一台主機代理（常駐在主機上的 worker）。
/// 機器憑證與配對碼只存 SHA-256；引擎欄位存的是 worker 最後一次登記時<b>原樣回報</b>的內容，
/// readiness 讀的時候才依「現在的登入授權」算，所以登出、換登入之後立刻反映，不必等下一次登記。
/// </summary>
internal sealed record HostAgentRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    /// <summary>名稱轉大寫（不分文化），組織裡唯一。</summary>
    public required string NameKey { get; init; }

    public required string State { get; init; }

    public string? MachineKey { get; init; }

    public string? MachineName { get; init; }

    public string? CredentialHash { get; init; }

    /// <summary>機器憑證什麼時候過期（發出後 <see cref="AiRules.HostCredentialLifetime"/>）。過了 <c>AuthenticateAsync</c> 就不認。</summary>
    public DateTimeOffset? CredentialExpiresAt { get; init; }

    /// <summary>
    /// 換新時發出、worker 還沒用過的下一把憑證（雜湊）。worker 第一次用它認證時換成正式的、舊的作廢；
    /// worker 沒存好、還在用舊的，下一次登記再發一把新的（覆蓋這一欄）。
    /// </summary>
    public string? NextCredentialHash { get; init; }

    public DateTimeOffset? NextCredentialExpiresAt { get; init; }

    /// <summary>
    /// 最近一次有人拿這台的憑證、卻報了不同的機器編號來登記（被拒絕）：那時報的編號、電腦名稱與時間。
    /// 擁有者在管理頁看到警告；重新配對時清掉。
    /// </summary>
    public string? SuspectMachineKey { get; init; }

    public string? SuspectMachineName { get; init; }

    public DateTimeOffset? SuspectSeenAt { get; init; }

    public string? PairingCodeHash { get; init; }

    public DateTimeOffset? PairingExpiresAt { get; init; }

    /// <summary>開始配對的人：配對時要重新確認他現在還是擁有者。</summary>
    public Guid? PairingStartedBy { get; init; }

    public string? AgentVersion { get; init; }

    public string? ServerUrl { get; init; }

    /// <summary>jsonb：最後一次登記回報的兩個引擎（<c>StoredHostEngine</c>）。</summary>
    public string Engines { get; init; } = "[]";

    /// <summary>jsonb：擁有者要求登出的引擎（固定文字的陣列）；worker 回報已經登出時清掉。</summary>
    public string LogoutRequested { get; init; } = "[]";

    public DateTimeOffset? LastSeenAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required Guid UpdatedBy { get; init; }
}

/// <summary><c>ai.host_sources</c> 的一列：組織來源（一個代理的一個引擎最多一個）。</summary>
internal sealed record HostSourceRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid AgentId { get; init; }

    /// <summary><see cref="AiEnumNames"/> 的固定文字：claude_code 或 codex。</summary>
    public required string Engine { get; init; }

    public required string Name { get; init; }

    public required string NameKey { get; init; }

    public required bool Enabled { get; init; }

    /// <summary>jsonb：開放的模型代號（字串陣列）。</summary>
    public required string EnabledModels { get; init; }

    public string? DefaultModel { get; init; }

    public string? DefaultEffort { get; init; }

    public int? DailyRequestsPerPerson { get; init; }

    public int? MonthlyRequests { get; init; }

    public long? MonthlyTokens { get; init; }

    public DateTimeOffset? BillingAcknowledgedAt { get; init; }

    public Guid? BillingAcknowledgedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required Guid UpdatedBy { get; init; }
}

/// <summary>登入的狀態（<c>ai.host_logins.state</c> 的固定文字）。</summary>
internal static class HostLoginStates
{
    public const string Pending = "pending";

    public const string Claimed = "claimed";

    public const string Completed = "completed";

    public const string Failed = "failed";

    public const string Cancelled = "cancelled";
}

/// <summary>
/// <c>ai.host_logins</c> 的一列：擁有者在網頁開始的一次引擎登入。引擎目前的授權 = 那個代理、那個引擎唯一一筆 <c>completed</c>
/// （被登出或換掉的改成 <c>cancelled</c>）。<see cref="CompletedAt"/> 完成與失敗都記，是這次登入結束的時間。
/// </summary>
internal sealed record HostLoginRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid AgentId { get; init; }

    public required string Engine { get; init; }

    public required string CodeHash { get; init; }

    public required string State { get; init; }

    public required Guid StartedBy { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? ClaimedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>worker 回報的計費方式（固定文字：api、subscription、unknown）。</summary>
    public string? Billing { get; init; }

    public string? Error { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>工作的狀態（<c>ai.host_jobs.state</c> 的固定文字）。</summary>
internal static class HostJobStates
{
    /// <summary>送出時在來源的 advisory lock 裡預留了名額，使用者的訊息與工作的內容還沒備好。</summary>
    public const string Reserved = "reserved";

    public const string Queued = "queued";

    public const string Running = "running";

    public const string Finished = "finished";
}

/// <summary>
/// <c>ai.host_jobs</c> 的一列：網頁送出的一則回覆，排在某個來源的隊伍裡，等那台代理領走、跑完。
/// <see cref="Payload"/> 是工作的內容（<c>StoredHostJob</c>，不含工具授權）；回覆存完整列刪掉，用量另外存在 <c>host_usage</c>。
/// </summary>
internal sealed record HostJobRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid SourceId { get; init; }

    public required Guid AgentId { get; init; }

    public required Guid PersonId { get; init; }

    public required string ProjectKey { get; init; }

    public required Guid ConversationId { get; init; }

    public required Guid MessageId { get; init; }

    /// <summary>預留時還沒有；轉送把內容填上、狀態改成 queued 的同一步寫進來。</summary>
    public string? Payload { get; init; }

    public required string State { get; init; }

    /// <summary>資料庫給的流水號：預留的先後就是排隊的先後（時鐘相同、UUID 同一毫秒內也不保證有序）。</summary>
    public long Seq { get; init; }

    public DateTimeOffset? QueuedAt { get; init; }

    public DateTimeOffset? ClaimedAt { get; init; }

    /// <summary>領走時隨機給的租約；回報與結束要帶對。</summary>
    public int Lease { get; init; }

    public DateTimeOffset? LastReportAt { get; init; }

    /// <summary>使用者停止、來源或代理被停用或登出：下一次回報時告訴 worker 停下來，工具授權也立刻失效。</summary>
    public bool CancelRequested { get; init; }

    /// <summary>擁有者的動作讓這件工作被取消時，給使用者看的原因；使用者自己按停止是 null。</summary>
    public string? CancelMessage { get; init; }

    /// <summary>結束是 worker 自己送的（重送結束不算錯）；伺服器結束的（逾時、取消）不是。</summary>
    public bool FinishedByWorker { get; init; }

    /// <summary>這件工作用了幾次工具（伺服器端計，上限 <see cref="AiRules.MaxAgentToolCallsPerJob"/>）。</summary>
    public int ToolCalls { get; init; }

    /// <summary>這件工作成功提出了幾份（變更集加修改建議，上限 <see cref="AiRules.MaxAgentProposalsPerJob"/>）。</summary>
    public int Proposals { get; init; }

    public string? FinishStatus { get; init; }

    public string? FinishError { get; init; }

    /// <summary>領走之後 worker 失聯：引擎可能已經提出了東西，所以不重跑。</summary>
    public bool OutcomeUnknown { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public string? ToolTokenHash { get; init; }

    public DateTimeOffset? ToolTokenExpiresAt { get; init; }

    /// <summary>worker 回報過的最大流水號（含沒存的空文字）；下一則要的是它加一。</summary>
    public int EventCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary><c>ai.host_job_events</c> 的一列：worker 回報的一則進度，回覆存完就刪。流水號是 worker 編的，空文字沒存所以可能有空號。</summary>
internal sealed record HostJobEventRow
{
    public required Guid JobId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required int Seq { get; init; }

    public required string Content { get; init; }
}

/// <summary>用量的狀態（<c>ai.host_usage.state</c> 的固定文字）。配額與用量頁都只算沒退還的。</summary>
internal static class HostUsageStates
{
    public const string Reserved = "reserved";

    public const string Settled = "settled";

    public const string Released = "released";
}

/// <summary>
/// <c>ai.host_usage</c> 的一列：一件工作一列（編號就是工作編號），工作刪掉之後還在。只有則數、狀態、token，沒有對話的內容與標題。
/// </summary>
internal sealed record HostUsageRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid SourceId { get; init; }

    public required Guid PersonId { get; init; }

    public required string ProjectKey { get; init; }

    public required string State { get; init; }

    /// <summary>結束的種類（complete、stopped、failed）；還沒結束是 null。</summary>
    public string? FinishStatus { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    /// <summary>預留的時間：每天、每月的則數都依它算。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SettledAt { get; init; }
}

/// <summary>工具呼叫的狀態（<c>ai.host_tool_calls.state</c> 的固定文字）。</summary>
internal static class HostToolCallStates
{
    public const string Pending = "pending";

    public const string Done = "done";
}

/// <summary>
/// <c>ai.host_tool_calls</c> 的一列：提出類工具的一次呼叫（工作、工具、參數雜湊三者唯一），重送同樣的呼叫只建立一次。
/// </summary>
internal sealed record HostToolCallRow
{
    public required Guid JobId { get; init; }

    public required string Tool { get; init; }

    public required string ArgsHash { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string State { get; init; }

    public string? Result { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
