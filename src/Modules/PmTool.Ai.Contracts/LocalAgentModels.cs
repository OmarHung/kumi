using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 本機代理這一頭（<c>kumi agent start</c>，用權杖、範圍 <c>agent:run</c>）：登記這台電腦有哪些引擎、領網頁送來的回覆、回報進度與結果。
/// 本機代理只往外連（長輪詢），伺服器不連進使用者的電腦。規格見 <c>internal/HANDOFF-local-agent.md</c>。
/// 組織沒有允許成員使用自己的 AI 帳號（<see cref="AiSettings.AllowPersonalConnections"/>）時一律
/// <see cref="AiErrorCodes.PersonalDisabled"/>：本機代理用的也是成員自己的訂閱。
/// </summary>
public interface ILocalAgentService
{
    /// <summary>
    /// 登記（或更新）這台電腦與它的引擎、記下「現在在線」，然後等最多 <see cref="AiRules.LocalAgentPollWait"/>，
    /// 有這個人、這台電腦的引擎的工作就領走（同一件工作只會被領一次）回傳；等不到回 null。
    /// <paramref name="capacity"/> 是這台電腦現在還能同時多跑幾件（0 時只登記、不領）。
    /// 引擎清單是空的時只更新在線的時間。內容不對（名稱太長、控制字元、重複的引擎、模型太多）：<see cref="AiErrorCodes.AgentInvalid"/>。
    /// </summary>
    Task<Result<LocalAgentJob?>> CheckInAsync(Actor actor, LocalAgentCheckIn checkIn, int capacity, CancellationToken cancellationToken);

    /// <summary>
    /// 回報一批進度（依序）。工作不是這個人領的、已經結束：<see cref="AiErrorCodes.AgentJobNotFound"/>。
    /// 回傳的 <see cref="LocalAgentReportAck.Cancelled"/> 是 true 時，使用者按了「停止」或回覆逾時：本機代理要結束引擎、送 <see cref="FinishAsync"/>。
    /// </summary>
    Task<Result<LocalAgentReportAck>> ReportAsync(
        Actor actor, Guid jobId, IReadOnlyList<LocalAgentEvent> events, CancellationToken cancellationToken);

    /// <summary>工作結束了（完成、中斷或失敗）。重送同一件的結束不算錯（回成功、什麼都不改）。</summary>
    Task<Result<Unit>> FinishAsync(Actor actor, Guid jobId, LocalAgentFinish finish, CancellationToken cancellationToken);

    /// <summary>
    /// 本機代理下載這件工作的一個附件（<see cref="LocalAgentJob.Attachments"/> 列出的），交給引擎。
    /// 工作要是這個人領走、還沒結束的，附件要在這件工作的清單裡；不然一律 <see cref="AiErrorCodes.AttachmentNotFound"/>。呼叫端負責 Dispose。
    /// </summary>
    Task<Result<AssistantAttachmentContent>> OpenJobAttachmentAsync(Actor actor, Guid jobId, Guid attachmentId, CancellationToken cancellationToken);
}

/// <summary>網頁這一頭：「我的帳號」列出自己登記過的電腦、移除不用的。只能用 Cookie。</summary>
public interface ILocalAgentDirectory
{
    /// <summary>自己登記過的電腦，最近在線的在前；組織不允許時照樣列得出來。</summary>
    Task<IReadOnlyList<LocalAgentMachine>> ListAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 移除一台電腦（它的引擎從能選的清單消失）。那台電腦上的 <c>kumi agent</c> 還開著的話，下一次登記會再出現。
    /// 別人的、不存在的：<see cref="AiErrorCodes.ConnectionNotFound"/>。
    /// </summary>
    Task<Result<Unit>> RemoveAsync(Actor actor, Guid machineId, CancellationToken cancellationToken);
}

/// <param name="MachineKey">本機代理第一次執行時產生、存在設定資料夾裡的隨機編號（8～64 個英數字與 <c>-</c>），用來認出同一台電腦。</param>
/// <param name="MachineName">給人看的名稱，預設是電腦的主機名稱；最長 <see cref="AiRules.MaxMachineNameLength"/>。</param>
/// <param name="AgentVersion"><c>kumi</c> 的版本，例如 <c>0.9.0</c>。</param>
/// <param name="Engines">這台電腦上裝好、登入好的引擎；最多兩個（Claude Code、Codex 各一）。</param>
public sealed record LocalAgentCheckIn(
    string MachineKey, string MachineName, string AgentVersion, IReadOnlyList<LocalEngineInfo> Engines);

/// <param name="Kind">只能是 <see cref="AiProviderKind.ClaudeCode"/> 或 <see cref="AiProviderKind.Codex"/>。</param>
/// <param name="Version">引擎自己回報的版本（<c>claude --version</c> 的第一段），最長 40 個字。</param>
/// <param name="Models">能選的模型；第一個是預設。代號 <c>default</c> 表示「不指定，用引擎自己的設定」。最多 <see cref="AiRules.MaxLocalEngineModels"/> 個。</param>
public sealed record LocalEngineInfo(AiProviderKind Kind, string Version, IReadOnlyList<AiModelOption> Models);

/// <summary>
/// 一件要在本機跑的回覆。本機代理照 <see cref="Engine"/> 啟動引擎：系統提示接在引擎自己的後面、<see cref="Prompt"/> 從標準輸入送進去、
/// 只連 <see cref="McpUrl"/> 這一個 MCP 伺服器（用本機代理自己的權杖）、只准用 <see cref="AllowedTools"/> 列的工具（MCP 的工具名稱，不含前綴）。
/// </summary>
/// <param name="JobId">回報與結束時帶的編號。</param>
/// <param name="Engine"><see cref="AiProviderKind.ClaudeCode"/> 或 <see cref="AiProviderKind.Codex"/>。</param>
/// <param name="Model">要用的模型代號；<c>default</c> 是不指定。</param>
/// <param name="Effort">思考深度；null 是不指定。</param>
/// <param name="ProjectKey">對話所屬的專案，工具都要帶它；不選專案的組織對話是 null（kumi 0.12.0 起，這時不送 <c>X-Kumi-Project</c>）。</param>
/// <param name="SystemPrompt">伺服器組好的系統提示（和內建助理同一份規則，加上 MCP 工具的說法）。</param>
/// <param name="Prompt">整段對話（舊的在前）加上這一則訊息，已經排成一段文字；不靠引擎自己記的工作階段。</param>
/// <param name="AllowedTools">MCP 工具的名稱，例如 <c>get_blueprint</c>。</param>
/// <param name="McpUrl">這個部署的 MCP 網址（<c>{issuer}/mcp</c>）。</param>
/// <param name="MaxTurns">引擎最多來回幾次（工具呼叫的上限加一）。</param>
public sealed record LocalAgentJob(
    Guid JobId,
    AiProviderKind Engine,
    string Model,
    AiEffort? Effort,
    string? ProjectKey,
    string SystemPrompt,
    string Prompt,
    IReadOnlyList<string> AllowedTools,
    string McpUrl,
    int MaxTurns)
{
    /// <summary>
    /// 要交給引擎的圖片與 PDF（依序；internal/HANDOFF-ai-attachments.md 第 8 節）。<see cref="Prompt"/> 裡在原本的位置寫
    /// 「〔附件 N：檔名（附在這則訊息的最後）〕」，N 是這份清單的順序（1 起算）。本機代理領到之後用
    /// <see cref="ILocalAgentService.OpenJobAttachmentAsync"/> 下載、核對大小與 SHA-256，Claude Code 用串流 JSON 的圖片與文件區塊、
    /// Codex 用 <c>-i</c>（只有圖片）交給引擎。文字檔不在這裡，照舊接在 <see cref="Prompt"/> 裡。沒有附件是空的（舊版的 kumi 不認得這個欄位）。
    /// </summary>
    public IReadOnlyList<LocalAgentJobAttachment> Attachments { get; init; } = [];
}

/// <param name="Id">附件的編號，下載時用。</param>
/// <param name="Kind">只會是 <see cref="AssistantAttachmentKind.Image"/> 或 <see cref="AssistantAttachmentKind.Pdf"/>（Codex 只有圖片）。</param>
/// <param name="Sha256">內容的 SHA-256（小寫十六進位），下載後核對。</param>
public sealed record LocalAgentJobAttachment(
    Guid Id, string FileName, string ContentType, AssistantAttachmentKind Kind, long SizeBytes, string Sha256);

/// <summary>
/// 本機代理（成員電腦上的 Claude Code、Codex）與主機代理能用的 MCP 工具（工具名稱，不含前綴），只有一份：
/// 建立工作時當 <see cref="LocalAgentJob.AllowedTools"/> 送出去，伺服器的 <c>/mcp</c> 對帶 <c>agent:run</c> 的權杖也用它當上限，
/// 引擎被誘導去叫別的工具時伺服器端一樣擋得住，不靠成員電腦上的設定。
/// </summary>
public static class LocalAgentTools
{
    public static IReadOnlyList<string> Allowed { get; } =
    [
        "get_blueprint",
        "list_templates",
        "get_template",
        "describe_usage",
        "preview_changeset",
        "propose_changeset",
        "get_changeset",
        "get_item",
        "search_items",
        "propose_item_changes",
    ];

    public static bool IsAllowed(string toolName) => Allowed.Contains(toolName);

    /// <summary>
    /// 不選專案的組織對話的工作能用的工具。MCP 對這種工作（權杖帶 <c>agent:run</c>、工作的對話沒有專案；或主機授權的專案是 null）
    /// 用它當上限，而且一律不碰任何專案的資料：沒有專案不等於「全部專案都能碰」。
    /// </summary>
    public static IReadOnlyList<string> AllowedForOrganization { get; } =
    [
        "list_templates",
        "get_template",
        "list_projects",
        "propose_project",
    ];

    public static bool IsAllowedForOrganization(string toolName) => AllowedForOrganization.Contains(toolName);
}

/// <summary>本機代理回報的一則進度。JSON 用 <c>type</c> 分：<c>text</c>、<c>tool</c>。</summary>
public abstract record LocalAgentEvent;

/// <summary>模型寫出來的一段文字，接在前一段後面。最長 <see cref="AiRules.MaxLocalAgentTextDelta"/> 個字。</summary>
public sealed record LocalAgentText(string Delta) : LocalAgentEvent;

/// <summary>
/// 工具開始或結束。<see cref="AssistantToolPart.Name"/> 是 MCP 的工具名稱（不含前綴）；不在 <see cref="LocalAgentJob.AllowedTools"/> 裡的名稱照樣記，
/// 網頁畫成一般的「用了工具」。<see cref="AssistantToolPart.ChangeSetNumber"/> 是 propose_changeset 成功時回的編號。
/// </summary>
public sealed record LocalAgentTool(AssistantToolPart Tool) : LocalAgentEvent;

/// <param name="Cancelled">使用者按了「停止」、回覆逾時，或工作已經被伺服器結束：停下引擎、送結束。</param>
public sealed record LocalAgentReportAck(bool Cancelled);

/// <param name="Status">完成、中斷（使用者停止或本機代理被關掉）、失敗。</param>
/// <param name="Error">中斷或失敗的原因，繁體中文，給人看；最長 <see cref="AiRules.MaxErrorLength"/>。不能帶權杖或本機的路徑。</param>
/// <param name="Usage">引擎回報的 token 用量；沒有就是 null。</param>
public sealed record LocalAgentFinish(AssistantMessageStatus Status, string? Error, AssistantUsage? Usage);

/// <summary>「我的帳號」列的一台電腦。</summary>
/// <param name="Id">移除時用的編號。</param>
/// <param name="Engines">這台電腦上次登記的引擎與版本。</param>
/// <param name="Online">最近 <see cref="AiRules.LocalAgentOnlineWindow"/> 內登記過。</param>
public sealed record LocalAgentMachine(
    Guid Id,
    string Name,
    string AgentVersion,
    IReadOnlyList<LocalAgentEngineSummary> Engines,
    bool Online,
    DateTimeOffset LastSeenAt);

public sealed record LocalAgentEngineSummary(AiProviderKind Kind, string Version);
