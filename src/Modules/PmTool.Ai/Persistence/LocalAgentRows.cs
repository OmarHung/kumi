namespace PmTool.Ai.Persistence;

/// <summary><c>ai.local_agents</c> 的一列：一個人的一台電腦（<c>kumi agent start</c> 登記的）。</summary>
internal sealed record LocalAgentRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>本機代理自己產生、存在它的設定資料夾裡的隨機編號，用來認出同一台電腦。</summary>
    public required string MachineKey { get; init; }

    public required string Name { get; init; }

    public required string AgentVersion { get; init; }

    public required DateTimeOffset LastSeenAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// <c>ai.local_engines</c> 的一列：一台電腦上的一個引擎。<see cref="Id"/> 就是能選清單裡的連線編號。
/// 同一個模組裡，電腦刪掉時引擎一起刪（外鍵）。
/// </summary>
internal sealed record LocalEngineRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid AgentId { get; init; }

    /// <summary><see cref="AiEnumNames"/> 的固定文字：claude_code 或 codex。</summary>
    public required string Kind { get; init; }

    public required string Version { get; init; }

    /// <summary>模型的清單（jsonb）：<c>[{"id","displayName","efforts":["low",…]}]</c>，第一個是預設。</summary>
    public required string Models { get; init; }
}

/// <summary>工作的狀態（<c>ai.local_agent_jobs.state</c> 的固定文字）。</summary>
internal static class LocalJobStates
{
    public const string Waiting = "waiting";

    public const string Claimed = "claimed";

    public const string Finished = "finished";
}

/// <summary>
/// <c>ai.local_agent_jobs</c> 的一列：網頁送出的一則回覆，等這個人的那台電腦領走、跑完。
/// <see cref="Payload"/> 是 <c>LocalAgentJob</c>（jsonb），領走時原樣回給本機代理。
/// </summary>
internal sealed record LocalJobRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>只有這個引擎所在的那台電腦領得到。</summary>
    public required Guid EngineId { get; init; }

    public required Guid ConversationId { get; init; }

    public required Guid MessageId { get; init; }

    public required string Payload { get; init; }

    public required string State { get; init; }

    /// <summary>
    /// 這件工作要交給引擎下載的圖片與 PDF 的附件編號（<c>LocalAgentJob.Attachments</c>）。內容領走就清掉，下載時靠這一欄核對：
    /// 不在清單裡的附件、不是領走這件的人、已結束的工作都下載不到。
    /// </summary>
    public Guid[] AttachmentIds { get; init; } = [];

    /// <summary>使用者按了「停止」、回覆逾時，或伺服器放棄等這件工作：下一次回報時告訴本機代理停下來。</summary>
    public bool CancelRequested { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ClaimedAt { get; init; }

    public DateTimeOffset? LastReportAt { get; init; }

    /// <summary>已經存下幾則進度；下一則的流水號是它加一。回報時鎖住這一列，流水號才不會重複。</summary>
    public int EventCount { get; init; }

    /// <summary>同 <see cref="HostJobRow.ToolCalls"/>：MCP 的請求帶 <c>X-Kumi-Job</c> 時計在這一件。</summary>
    public int ToolCalls { get; init; }

    /// <summary>同 <see cref="HostJobRow.Proposals"/>。</summary>
    public int Proposals { get; init; }

    /// <summary>結束的種類（<c>complete</c>、<c>stopped</c>、<c>failed</c>）；還沒結束是 null。</summary>
    public string? FinishStatus { get; init; }

    public string? FinishError { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }
}

/// <summary><c>ai.local_agent_events</c> 的一列：本機代理回報的一則進度，回覆存完就刪。</summary>
internal sealed record LocalEventRow
{
    public required Guid JobId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required int Seq { get; init; }

    /// <summary>jsonb：<c>{"type":"text","delta"}</c> 或 <c>{"type":"tool",…}</c>。</summary>
    public required string Content { get; init; }
}
