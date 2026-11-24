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

    /// <summary>工作引擎的專案編號（模組之間不建外鍵）。</summary>
    public required Guid ProjectId { get; init; }

    public required Guid PersonId { get; init; }

    public string? Title { get; init; }

    public required int MessageCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// 正在回覆的租約到什麼時候；null 或已經過了就是沒在回覆。用條件式的 UPDATE 拿，好幾個 API 程序之間也只有一個在回覆。
    /// </summary>
    public DateTimeOffset? RunningUntil { get; init; }
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
