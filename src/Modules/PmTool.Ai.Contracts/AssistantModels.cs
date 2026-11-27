using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 專案的 AI 助理：幫忙調整專案的設定（藍圖）。它讀設定、模板與統計數字，算預覽，提出變更集等人核准；
/// 不讀項目的內文、不能套用、不能改項目。權限等於發起的人（要有 blueprint.read，提出還要 blueprint.propose）。
/// 對話只有本人看得到。專案看不到一律回 NotFound（工作引擎的代碼）。規格見 <c>internal/HANDOFF-ai-assistant.md</c> 第 4、5 節。
/// <para>
/// 每個方法的 <c>projectKey</c> 是 null 時是「不選專案」的組織對話（<c>internal/HANDOFF-org-assistant.md</c>）：組織的有效成員（含訪客）
/// 在組織能用 AI 時都能開；工具只有模板、看得到的專案清單與提出新專案（<see cref="IProjectProposalService"/>）。
/// 組織對話與專案對話是分開的兩組：用專案代號讀不到組織對話，反之亦然（一律 <see cref="AiErrorCodes.ConversationNotFound"/>）。
/// 組織對話不收選的項目（<see cref="AiErrorCodes.OrganizationChatUnsupported"/>）；附件照收，用組織對話自己上傳的。
/// </para>
/// </summary>
public interface IAssistantService
{
    /// <summary>自己在這個專案的對話，新的（最後更新）在前，最多 <see cref="AiRules.MaxConversationsListed"/> 則。</summary>
    Task<Result<IReadOnlyList<AssistantConversationSummary>>> ListAsync(
        Actor actor, string? projectKey, CancellationToken cancellationToken);

    /// <summary>
    /// 自己所有的對話（各專案與不選專案的，浮動面板的「對話」選單），新的在前，最多 <see cref="AiRules.MaxConversationsListed"/> 則。
    /// 每個專案照 <see cref="ListAsync"/> 同一套規則：看不到專案、沒有讀設定的權限的不列，看過全部項目而現在只剩自己的整則刪掉。
    /// </summary>
    Task<Result<IReadOnlyList<AssistantConversationListing>>> ListAllAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>開一則空的對話。組織沒開 AI 時回 <see cref="AiErrorCodes.Unavailable"/>。</summary>
    Task<Result<AssistantConversationSummary>> CreateAsync(Actor actor, string? projectKey, CancellationToken cancellationToken);

    /// <summary>一則對話的全部訊息，舊的在前。</summary>
    Task<Result<AssistantConversation>> GetAsync(
        Actor actor, string? projectKey, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>刪掉一則對話與它的訊息（真的刪，不能復原）。正在回覆時回 <see cref="AiErrorCodes.ConversationBusy"/>。</summary>
    Task<Result<Unit>> DeleteAsync(Actor actor, string? projectKey, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// 送出一則訊息並開始回覆。先檢查權限、對話、訊息、選擇、同時在跑的回覆，任何一項不過就回失敗，什麼都不存；
    /// 通過的話使用者的訊息已經存下、對話標成正在回覆，回傳的 <see cref="AssistantRun"/> 要讀到結束。
    /// </summary>
    Task<Result<AssistantRun>> StartAsync(
        Actor actor, string? projectKey, Guid conversationId, AssistantMessageInput input, CancellationToken cancellationToken);
}

/// <param name="Text">
/// 使用者打的字。前後的空白去掉；最長 <see cref="AiRules.MaxMessageLength"/>；不能有 NUL 與控制字元（換行與 Tab 可以）。
/// 有附件時可以是空的（對話的標題改用第一個附件的檔名）。
/// </param>
/// <param name="Selection">這一則用哪個連線、模型、思考深度。要是 <see cref="IAiOptionsService"/> 現在列得出來的。</param>
public sealed record AssistantMessageInput(string Text, AiSelection Selection)
{
    /// <summary>
    /// 這則訊息針對的項目（網頁上選的，例如 <c>WEB-12</c>）；null 是沒有選。要是這個專案、自己看得到的項目，
    /// 不然 <see cref="AiErrorCodes.ItemNotFound"/>（什麼都不存）。助理在提示裡知道使用者在看哪一個，自己用工具讀它。
    /// </summary>
    public string? ItemKey { get; init; }

    /// <summary>
    /// 這則訊息的附件（<see cref="IAssistantAttachmentService.UploadAsync"/> 上傳的待送附件），依這個順序顯示與送出。
    /// 要是自己在這個專案待送的、不重複、最多 <see cref="AssistantAttachmentRules.MaxPerMessage"/> 個、合計不超過
    /// <see cref="AssistantAttachmentRules.MaxBytesPerMessage"/>；選的來源讀不到的類型回 <see cref="AiErrorCodes.AttachmentUnsupported"/>。
    /// 任何一項不過就什麼都不存；存下使用者的訊息時在同一個交易裡綁上（之後不能再用）。
    /// </summary>
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
}

/// <summary>
/// 一則正在進行的回覆。<see cref="Events"/> 依序送出文字、工具的進度，最後一定是 <see cref="AssistantRunCompleted"/>；
/// 讀的時候給的 CancellationToken 被取消（使用者按「停止」或關掉頁面）時，存下已經有的部分（狀態 <see cref="AssistantMessageStatus.Stopped"/>）。
/// 出錯時不丟例外，最後一則的狀態是 <see cref="AssistantMessageStatus.Failed"/>、帶原因。只能讀一次。
/// </summary>
/// <param name="UserMessage">剛存下的使用者訊息。</param>
/// <param name="AssistantMessageId">這則回覆存下來時的編號，<see cref="AssistantRunCompleted.Message"/> 也是這個編號。</param>
public sealed record AssistantRun(AssistantMessage UserMessage, Guid AssistantMessageId, IAsyncEnumerable<AssistantRunEvent> Events);

/// <summary>回覆進行中的事件。</summary>
public abstract record AssistantRunEvent;

/// <summary>模型寫出來的一段文字，接在前一段後面。</summary>
public sealed record AssistantTextDelta(string Delta) : AssistantRunEvent;

/// <summary>工具開始（<see cref="AssistantToolStatus.Running"/>）或結束了。同一次呼叫用同一個 <see cref="AssistantToolPart.CallId"/>。</summary>
public sealed record AssistantToolUpdate(AssistantToolPart Tool) : AssistantRunEvent;

/// <summary>回覆結束了（完成、中斷或失敗），這是存下來的樣子。</summary>
public sealed record AssistantRunCompleted(AssistantMessage Message) : AssistantRunEvent;

/// <summary>
/// 主機上的來源（<see cref="AiConnectionOption.Hosted"/>）：這則還在排隊。<paramref name="Position"/> 是前面還有幾件（排隊的加執行中的）再加一，
/// 1 是下一個。一開始送一次，之後位置變了才再送。用 API 金鑰的模型與本機代理不會有這個事件。
/// </summary>
public sealed record AssistantRunQueued(int Position) : AssistantRunEvent;

/// <summary>主機上的來源：輪到了，worker 開始執行（之後才有文字與工具的事件）。只送一次。</summary>
public sealed record AssistantRunStarted : AssistantRunEvent;

public enum AssistantRole
{
    User,

    Assistant,
}

public enum AssistantMessageStatus
{
    Complete,

    /// <summary>使用者按了「停止」、關掉頁面，或超過 <see cref="AiRules.ReplyTimeout"/>。</summary>
    Stopped,

    Failed,
}

public enum AssistantToolStatus
{
    Running,

    Done,

    /// <summary>工具回了預期中的失敗（例如變更集驗證不過），模型會看到原因、通常會自己修正再試。</summary>
    Failed,
}

/// <summary>訊息的一段。給人看的形狀：工具只說做了什麼與結果，不帶送給模型的參數與內容。</summary>
public abstract record AssistantPart;

public sealed record AssistantTextPart(string Text) : AssistantPart;

/// <summary>助理用了一個工具。</summary>
/// <param name="CallId">這則回覆裡的編號。</param>
/// <param name="Name">
/// 工具的名稱：get_blueprint、list_templates、get_template、describe_usage、preview_changeset、propose_changeset、get_changeset、
/// get_item、search_items、propose_item_changes、propose_new_items、get_project、list_people、propose_member_changes、propose_item_deletions、propose_bulk_item_changes、withdraw_proposal、my_work；組織對話是 list_templates、get_template、list_projects、propose_project、withdraw_proposal、my_work。網頁照名稱顯示說法，不認得的畫成一般的「用了工具」。
/// </param>
/// <param name="ChangeSetNumber">propose_changeset 成功時，提出的變更集編號（網頁畫成「變更集 #12」的卡片）。</param>
/// <param name="Error">失敗時給人看的原因，繁體中文。</param>
public sealed record AssistantToolPart(
    string CallId, string Name, AssistantToolStatus Status, long? ChangeSetNumber, string? Error) : AssistantPart
{
    /// <summary>propose_item_changes 成功時，建立的修改建議（<see cref="IItemProposalService"/>；網頁畫成有「套用」「不要」的卡片）。</summary>
    public Guid? ItemProposalId { get; init; }

    /// <summary>propose_new_items 成功時，建立的新增項目建議（<see cref="INewItemProposalService"/>；網頁畫成有「全部建立」「不要」的卡片）。</summary>
    public Guid? NewItemProposalId { get; init; }

    /// <summary>propose_member_changes 成功時，建立的成員建議（<see cref="IMemberProposalService"/>；網頁畫成有「套用」「不要」的卡片）。</summary>
    public Guid? MemberProposalId { get; init; }

    /// <summary>propose_item_deletions 成功時，建立的刪除建議（<see cref="IItemDeletionProposalService"/>；網頁畫成有「刪除」「不要」的卡片）。</summary>
    public Guid? ItemDeletionProposalId { get; init; }

    /// <summary>propose_bulk_item_changes 成功時，建立的批次修改建議（<see cref="IItemBatchProposalService"/>；網頁畫成有「全部套用」「不要」的卡片）。</summary>
    public Guid? ItemBatchProposalId { get; init; }

    /// <summary>propose_project 成功時，建立的新專案建議（<see cref="IProjectProposalService"/>；網頁畫成有「建立專案」「不要」的卡片）。</summary>
    public Guid? ProjectProposalId { get; init; }
}

/// <summary>一則訊息。</summary>
/// <param name="Parts">依序的段落。使用者的訊息只有一段文字。</param>
public sealed record AssistantMessage(Guid Id, AssistantRole Role, IReadOnlyList<AssistantPart> Parts, DateTimeOffset CreatedAt)
{
    /// <summary>助理的回覆才有。</summary>
    public AssistantMessageStatus? Status { get; init; }

    /// <summary>失敗或中斷的原因，繁體中文；完成時是 null。</summary>
    public string? Error { get; init; }

    /// <summary>助理的回覆用的連線、模型、思考深度。</summary>
    public AiSelection? Selection { get; init; }

    /// <summary>當時的連線名稱（連線之後改名或刪掉，這裡還是當時的名稱）。</summary>
    public string? ConnectionName { get; init; }

    /// <summary>助理的回覆用了多少 token（含所有工具呼叫的來回）；供應商沒回報時是 null。</summary>
    public AssistantUsage? Usage { get; init; }

    /// <summary>使用者的訊息才有：送出時選的項目（<see cref="AssistantMessageInput.ItemKey"/>），網頁在訊息上標「關於 WEB-12」。</summary>
    public string? ItemKey { get; init; }

    /// <summary>使用者的訊息才有：送出時帶的附件，依送出的順序；沒有是空的。</summary>
    public IReadOnlyList<AssistantAttachment> Attachments { get; init; } = [];
}

public sealed record AssistantUsage(long InputTokens, long OutputTokens);

/// <summary>對話清單的一列。</summary>
/// <param name="Title">第一則訊息的前 <see cref="AiRules.MaxTitleLength"/> 個字；還沒送過訊息是 null（網頁寫「新的對話」）。</param>
/// <param name="IsRunning">正在回覆。</param>
public sealed record AssistantConversationSummary(
    Guid Id, string? Title, int MessageCount, bool IsRunning, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AssistantConversation(AssistantConversationSummary Conversation, IReadOnlyList<AssistantMessage> Messages);
