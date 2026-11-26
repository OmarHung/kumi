using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理提出的「新增項目」建議：助理（內建的工具 propose_new_items，或本機、主機代理經 MCP 的同名工具）只建立建議、不建立項目，
/// 使用者在網頁按「全部建立」才以自己的身分與權限依序建立（<see cref="ApplyAsync"/>），按「不要」就作廢。
/// 一份建議可以有好幾個項目（把一份需求拆成幾個任務），後面的可以掛在前面的底下。規格見 <c>internal/HANDOFF-ai-new-items.md</c>。
/// 只有建立它的人看得到、能套用；別人的、不存在的、專案已經看不到的一律 <see cref="AiErrorCodes.NewItemProposalNotFound"/>。
/// </summary>
public interface INewItemProposalService
{
    Task<Result<NewItemProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 以 <paramref name="actor"/> 的身分照順序建立（<c>IWorkItemService.CreateAsync</c>，權限與檢查全照工作引擎）。
    /// 第一個項目就建立失敗時什麼都沒建立、建議維持「待決定」，回傳工作引擎的失敗（代碼與訊息原樣）；
    /// 後面的失敗時已經建立的不刪，掛在失敗的項目底下的也跳過，建議標成已套用、<see cref="NewItemProposal.Error"/> 說明哪些沒建立。
    /// 開始建立之前先把建議標成「建立中」並提交，同一份建議不會被建立兩次（就算程序在中途結束）。
    /// 已經套用、作廢或建立中的：<see cref="AiErrorCodes.NewItemProposalClosed"/>。
    /// </summary>
    Task<Result<NewItemProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用、作廢或建立中的：<see cref="AiErrorCodes.NewItemProposalClosed"/>。</summary>
    Task<Result<NewItemProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>
/// 建立新增項目的建議（<see cref="INewItemProposalService"/> 之外的另一半）。內建助理與 MCP 的 propose_new_items 都呼叫它，檢查的規則只有一份。
/// </summary>
public interface INewItemProposalCreator
{
    /// <summary>
    /// 專案要看得到（不然原樣回傳工作引擎的 <c>project.not_found</c>）、使用者在專案裡要有 <c>item.create</c>（不然 <see cref="AiErrorCodes.Forbidden"/>，
    /// 訊息「你沒有建立項目的權限。」）。每個項目的檢查不過時回 <see cref="AiErrorCodes.FieldInvalid"/>，每個問題一個細項
    /// （路徑寫成 <c>items[2].type</c>），什麼都不存。檢查的規則見 <see cref="NewItemDraft"/> 各屬性的註解。
    /// </summary>
    Task<Result<NewItemProposal>> CreateAsync(Actor actor, NewItemProposalDraft draft, CancellationToken cancellationToken);
}

/// <param name="Items">1 到 <see cref="AiRules.MaxNewItemsPerProposal"/> 個，依建立的順序。</param>
public sealed record NewItemProposalDraft(string ProjectKey, IReadOnlyList<NewItemDraft> Items)
{
    /// <summary>內建助理建立的才有；MCP 是 null。</summary>
    public Guid? ConversationId { get; init; }
}

/// <summary>
/// 建議新增的一個項目。狀態一律從類型的流程的第一個狀態開始（和網頁、<c>create_item</c> 一樣），不收狀態。
/// 字串一律擋 NUL 與控制字元（說明可以有換行與 tab，標題不行）。
/// </summary>
/// <param name="Type">項目類型的 key，要是藍圖裡有的。</param>
/// <param name="Title">不空白，去掉前後空白後最長 <c>WorkRules.MaxTitleLength</c>。</param>
public sealed record NewItemDraft(string Type, string Title)
{
    /// <summary>
    /// 同一份建議裡給這個項目取的代稱（1～20 個小寫英文、數字、<c>-</c>、<c>_</c>），讓後面的項目用 <see cref="ParentRef"/> 掛在它底下。
    /// 同一份建議裡不能重複；沒有人掛在它底下時可以不給。
    /// </summary>
    public string? Ref { get; init; }

    /// <summary>說明（Markdown），最長 <c>WorkRules.MaxDescriptionLength</c>；null 是沒有。</summary>
    public string? Description { get; init; }

    /// <summary>
    /// 負責人。工具收 <c>me</c>（發問的人）、<c>none</c>、專案成員的人員編號（MCP 另外收 Email），在工具那一層換成人員編號；
    /// 這裡一定是專案成員的人員編號或 null（不是成員：細項的訊息列出成員的名稱與編號，讓模型改用正確的）。
    /// </summary>
    public Guid? AssigneeId { get; init; }

    /// <summary><c>none</c>、<c>low</c>、<c>medium</c>、<c>high</c>、<c>urgent</c>；null 是 none。</summary>
    public string? Priority { get; init; }

    /// <summary><c>yyyy-MM-dd</c>。</summary>
    public string? StartDate { get; init; }

    /// <summary><c>yyyy-MM-dd</c>。</summary>
    public string? DueDate { get; init; }

    /// <summary>不小於 0、不超過 <c>WorkRules.MaxNumberMagnitude</c>。</summary>
    public decimal? Estimate { get; init; }

    /// <summary>
    /// 掛在既有的項目底下：要在這個專案、看得到，而且藍圖允許這個類型掛在那個項目的類型底下（<c>ItemType.Parents</c>）。
    /// 和 <see cref="ParentRef"/> 只能給一個。
    /// </summary>
    public string? ParentKey { get; init; }

    /// <summary>
    /// 掛在同一份建議裡、排在前面的項目底下（它的 <see cref="Ref"/>）；藍圖要允許這個類型掛在那個項目的類型底下。
    /// 套用時那個項目沒建立成功，這個也跳過。
    /// </summary>
    public string? ParentRef { get; init; }

    /// <summary>
    /// 自訂欄位 {key: 值}：key 要是這個類型有的欄位，值的 JSON 種類要對（規則和修改建議的 <c>fields.&lt;key&gt;</c> 同一份）；
    /// 選項、必填這些細節套用時由工作引擎判斷。
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Fields { get; init; } = new Dictionary<string, JsonElement>();
}

public enum NewItemProposalStatus
{
    /// <summary>等使用者決定。</summary>
    Pending,

    /// <summary>
    /// 使用者按了「全部建立」、正在建立。超過 <see cref="AiRules.NewItemProposalApplyStale"/> 還是這樣（程序在中途結束）時，
    /// 讀出來一律是 <see cref="Applied"/>，<see cref="NewItemProposal.Error"/> 說明建立的過程中斷了，<see cref="NewItemProposal.CreatedKeys"/> 是已經建立的。
    /// </summary>
    Applying,

    Applied,

    Dismissed,
}

/// <summary>一份新增項目的建議。</summary>
/// <param name="Items">建議的項目，依建立的順序；就是檢查過的 <see cref="NewItemDraft"/>（字串去掉前後空白、空白的說明是 null）。</param>
/// <param name="CreatedKeys">和 <paramref name="Items"/> 一一對應：已經建立的是項目編號（例如 <c>WEB-13</c>），還沒建立或沒建立成功是 null。</param>
public sealed record NewItemProposal(
    Guid Id,
    string ProjectKey,
    IReadOnlyList<NewItemDraft> Items,
    IReadOnlyList<string?> CreatedKeys,
    NewItemProposalStatus Status,
    DateTimeOffset CreatedAt)
{
    /// <summary>套用（開始建立）或作廢的時間。</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>有項目沒建立成功、或建立的過程中斷：說明哪些沒建立，繁體中文。</summary>
    public string? Error { get; init; }
}
