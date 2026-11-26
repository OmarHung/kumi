namespace PmTool.Ai.Persistence;

/// <summary>
/// <c>ai.settings</c> 的一列：一個組織的 AI 開關與預設。沒設定過的組織沒有這一列。
/// 思考深度存成固定的文字（<see cref="AiEnumNames"/>），null 是不指定。
/// </summary>
internal sealed record AiSettingsRow
{
    public required Guid OrganizationId { get; init; }

    public required bool Enabled { get; init; }

    public Guid? DefaultConnectionId { get; init; }

    public string? DefaultModel { get; init; }

    public string? DefaultEffort { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required Guid UpdatedBy { get; init; }

    /// <summary>成員能不能接自己的 AI 帳號；預設不行（專案的設定會送到成員自己的供應商帳號，由管理員決定）。</summary>
    public bool AllowPersonalConnections { get; init; }

    /// <summary>組織連線每人每天最多幾則回覆（組織的時區）；null 是不限。新的組織與既有的都從 <see cref="AiRules.DefaultDailyRequestsPerPerson"/> 開始。</summary>
    public int? DailyRequestsPerPerson { get; init; } = AiRules.DefaultDailyRequestsPerPerson;

    /// <summary>組織連線全組織每月最多幾則回覆；null 是不限。</summary>
    public int? MonthlyRequests { get; init; }

    /// <summary>組織連線全組織每月最多多少 token（輸入加輸出）；null 是不限。</summary>
    public long? MonthlyTokens { get; init; }
}

/// <summary>
/// <c>ai.connections</c> 的一列：組織接上的一個供應商。金鑰用 Data Protection 加密後存，讀出來只說有沒有與最後幾個字。
/// </summary>
internal sealed record AiConnectionRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    /// <summary>連線的擁有者；null 是組織的連線，有值是那個人自己的（只有他能用）。</summary>
    public Guid? OwnerPersonId { get; init; }

    public required string Name { get; init; }

    /// <summary>名稱轉大寫（不分文化），在同一個擁有者（組織或某個人）之下唯一：名稱不分大小寫不能重複。</summary>
    public required string NameKey { get; init; }

    /// <summary>供應商（<see cref="AiEnumNames"/> 的固定文字）。</summary>
    public required string Kind { get; init; }

    /// <summary>只有相容端點有，結尾沒有斜線。</summary>
    public string? BaseUrl { get; init; }

    /// <summary>加密過的金鑰（<c>IDataProtector.Protect</c> 的結果）；沒有金鑰是 null。</summary>
    public string? ProtectedApiKey { get; init; }

    public string? ApiKeyHint { get; init; }

    /// <summary>模型的清單（jsonb）：<c>[{"id","displayName","efforts":["low",…],"enabled","custom"}]</c>，依代號排序。</summary>
    public required string Models { get; init; }

    public DateTimeOffset? ModelsCheckedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required Guid UpdatedBy { get; init; }
}

/// <summary><c>ai.conversations</c> 的一列：某個人在某個專案和助理的一則對話。只有本人讀得到。</summary>
internal sealed record AiConversationRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    /// <summary>工作引擎的專案編號（模組之間不建外鍵）；不選專案的組織對話是 null。</summary>
    public Guid? ProjectId { get; init; }

    public required Guid PersonId { get; init; }

    public string? Title { get; init; }

    public required int MessageCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// 正在回覆的租約到什麼時候；null 或已經過了就是沒在回覆。用條件式的 UPDATE 拿，好幾個 API 程序之間也只有一個在回覆。
    /// </summary>
    public DateTimeOffset? RunningUntil { get; init; }

    /// <summary>
    /// 有沒有在「看得到專案全部項目」（<c>item.read</c>）的時候回覆過。之後這個人只剩自己的項目（或看不到專案）時，
    /// 這則對話整個刪掉：歷史裡可能有當時工具讀到、他現在看不到的內容。
    /// </summary>
    public bool SawAllItems { get; init; }
}

/// <summary><c>ai.messages</c> 的一列：對話裡的一則訊息。</summary>
internal sealed record AiMessageRow
{
    public required Guid Id { get; init; }

    public required Guid ConversationId { get; init; }

    public required Guid OrganizationId { get; init; }

    /// <summary>對話裡的順序，從 1 開始。</summary>
    public required int Sequence { get; init; }

    /// <summary>user 或 assistant。</summary>
    public required string Role { get; init; }

    /// <summary>給人看的段落（jsonb），形狀見 <c>internal/HANDOFF-ai-assistant.md</c> 第 5.3 節。</summary>
    public required string Parts { get; init; }

    /// <summary>
    /// 之後再送給模型用的內容（jsonb）：文字、工具的呼叫與結果，和供應商無關的形狀（第 5.4 節）。
    /// 不存模型的思考：思考的區塊綁著產生它的模型與對話，換供應商或模型時送不回去。
    /// </summary>
    public required string Transcript { get; init; }

    /// <summary>助理的回覆才有：complete、stopped、failed。</summary>
    public string? Status { get; init; }

    public string? Error { get; init; }

    public Guid? ConnectionId { get; init; }

    /// <summary>當時的連線名稱。</summary>
    public string? ConnectionName { get; init; }

    public string? Model { get; init; }

    public string? Effort { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>使用者的訊息才有：送出時選的項目編號。</summary>
    public string? ItemKey { get; init; }
}

/// <summary>
/// <c>ai.connection_usage</c> 的一列：用組織連線的一則回覆（編號就是助理那則訊息的編號）。形狀照 <c>host_usage</c>：
/// 送出時預留（<c>reserved</c>），回覆結束時記下 token（<c>settled</c>），送出沒有完成就退還（<c>released</c>）。
/// 對話刪掉之後還在，配額才不會因為刪對話而歸零。個人的連線、本機代理與主機來源不記在這裡。
/// </summary>
internal sealed record AiConnectionUsageRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid ConnectionId { get; init; }

    public required Guid PersonId { get; init; }

    public string? ProjectKey { get; init; }

    /// <summary>reserved、settled、released（同 <c>HostUsageStates</c>）。</summary>
    public required string State { get; init; }

    /// <summary>complete、stopped、failed；還沒結束是 null。</summary>
    public string? FinishStatus { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    /// <summary>預留的時間：每天、每月的則數都依它算。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SettledAt { get; init; }
}
