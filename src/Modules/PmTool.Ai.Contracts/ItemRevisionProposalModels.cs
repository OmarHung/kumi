using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理提出的「刪除項目」建議（內建的 propose_item_deletions，或本機、主機代理經 MCP 的同名工具）：助理只建立建議，
/// 使用者在網頁按「刪除」才以自己的身分照工作引擎的規則刪除（<c>IBulkItemService.DeleteAsync</c>，軟刪除、能復原），按「不要」就作廢。
/// 只有建立它的人看得到、能套用；別人的、不存在的、專案已經看不到的一律 <see cref="AiErrorCodes.ItemDeletionProposalNotFound"/>。
/// 規格見 <c>internal/HANDOFF-ai-revise.md</c>。
/// </summary>
public interface IItemDeletionProposalService
{
    Task<Result<ItemDeletionProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 一次批次刪除（上下層一起在這份裡時先刪下層）。一筆都沒刪成（工作引擎全部拒絕）時維持待決定、回傳第一筆的失敗；
    /// 有刪成的就標成已套用，沒刪成的寫進 <see cref="ItemDeletionProposal.Error"/>。刪除本來就是冪等的（已經刪除的算成功），
    /// 所以只在建議的 advisory lock 裡排隊、不需要「套用中」的狀態。逾時回 <see cref="AiErrorCodes.ApplyTimedOut"/> 並維持待決定，再按一次是安全的。已經套用或作廢的：<see cref="AiErrorCodes.ItemDeletionProposalClosed"/>。
    /// </summary>
    Task<Result<ItemDeletionProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用或作廢的：<see cref="AiErrorCodes.ItemDeletionProposalClosed"/>。</summary>
    Task<Result<ItemDeletionProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>建立刪除建議。內建助理與 MCP 的 propose_item_deletions 都呼叫它，檢查的規則只有一份。</summary>
public interface IItemDeletionProposalCreator
{
    /// <summary>
    /// 專案要看得到（不然原樣回傳 <c>project.not_found</c>）；使用者沒有 <c>item.delete</c> 時回 <see cref="AiErrorCodes.Forbidden"/>（「你沒有刪除項目的權限。」）。
    /// 每一筆的問題回 <see cref="AiErrorCodes.FieldInvalid"/>，路徑 <c>itemKeys[2]</c>，一次回全部，什麼都不存：
    /// 1 到 <see cref="AiRules.MaxItemsPerDeletionProposal"/> 個、不重複、是這個專案看得到的項目（看不到的和不存在的一樣）；
    /// 還有沒刪除、又不在這份裡的下層時是錯誤，訊息列出那些下層的編號（最多 10 個），讓模型決定要一起刪還是不刪。
    /// 存起來的會記下每個項目當時的標題（給卡片顯示）。
    /// </summary>
    Task<Result<ItemDeletionProposal>> CreateAsync(Actor actor, ItemDeletionProposalDraft draft, CancellationToken cancellationToken);
}

/// <param name="ItemKeys">要刪除的項目編號，依建議的順序。</param>
public sealed record ItemDeletionProposalDraft(string ProjectKey, IReadOnlyList<string> ItemKeys)
{
    /// <summary>內建助理建立的才有；MCP 是 null（對話由本機代理的工作找到，見 <see cref="IAssistantProposalLedger"/>）。</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>為什麼刪（一行，最長 200 字），給卡片顯示；可以不給。擋控制字元。</summary>
    public string? Reason { get; init; }
}

/// <param name="Title">建立建議時的標題。</param>
public sealed record ItemDeletionTarget(string ItemKey, string Title);

public sealed record ItemDeletionProposal(
    Guid Id, string ProjectKey, IReadOnlyList<ItemDeletionTarget> Items, ItemProposalStatus Status, DateTimeOffset CreatedAt)
{
    public string? Reason { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>套用之後真的刪除的編號（含原本就已經刪除的）。</summary>
    public IReadOnlyList<string> DeletedKeys { get; init; } = [];

    /// <summary>有幾筆沒刪成：說明哪些、為什麼，繁體中文。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// AI 助理提出的「一次改好幾個項目」建議（內建的 propose_bulk_item_changes，或 MCP 的同名工具）：每個項目各自一組欄位修改（值可以不同，
/// 例如六個任務各自的到期日）與一則留言，一張卡片、按一次「全部套用」。每一筆的規則和 <see cref="IItemProposalService"/> 的單一項目建議完全相同
/// （欄位 → 狀態 → 留言；說明帶建立建議時的 descriptionRevision 檢查衝突），只是包成一份。
/// 只有建立它的人看得到、能套用；別人的、不存在的、專案已經看不到的一律 <see cref="AiErrorCodes.ItemBatchProposalNotFound"/>。
/// </summary>
public interface IItemBatchProposalService
{
    Task<Result<ItemBatchProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 照順序一筆一筆套用（每一筆照單一項目建議的步驟）。每一筆都在它的第一步就失敗（什麼都沒改）時才維持待決定、回傳第一筆的失敗；
    /// 只要有任何一筆有進展就標成已套用：失敗的不復原已經改的、繼續下一筆，每一筆的結果在 <see cref="ItemBatchEntry.Error"/>，整份的摘要在 <see cref="ItemBatchProposal.Error"/>。
    /// 逾時（<see cref="AiErrorCodes.ApplyTimedOut"/>）：沒有任何進展時維持待決定；有進展時剩下的每一筆 Error 是「逾時沒有套用」、標成已套用。
    /// 在建議的 advisory lock 裡排隊。已經套用或作廢的：<see cref="AiErrorCodes.ItemBatchProposalClosed"/>。
    /// </summary>
    Task<Result<ItemBatchProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用或作廢的：<see cref="AiErrorCodes.ItemBatchProposalClosed"/>。</summary>
    Task<Result<ItemBatchProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>建立批次修改建議。內建助理與 MCP 的 propose_bulk_item_changes 都呼叫它。</summary>
public interface IItemBatchProposalCreator
{
    /// <summary>
    /// 專案要看得到；1 到 <see cref="AiRules.MaxItemsPerBatchProposal"/> 筆、同一個項目只出現一次。每一筆的檢查和單一項目的修改建議同一份
    /// （項目在這個專案看得到、欄位與值、權限、說明要帶 expectedDescriptionRevision…），問題回 <see cref="AiErrorCodes.FieldInvalid"/>，
    /// 細項路徑前面加 <c>items[3].</c>（例如 <c>items[3].changes[0].value</c>），一次回全部，什麼都不存。
    /// </summary>
    Task<Result<ItemBatchProposal>> CreateAsync(Actor actor, ItemBatchProposalDraft draft, CancellationToken cancellationToken);
}

public sealed record ItemBatchProposalDraft(string ProjectKey, IReadOnlyList<ItemBatchEntryDraft> Items)
{
    /// <summary>內建助理建立的才有；MCP 是 null。</summary>
    public Guid? ConversationId { get; init; }
}

/// <param name="Changes">要改的欄位（規則同 <see cref="ItemFieldChange"/>）；可以是空的，只要有 <paramref name="Comment"/>。</param>
/// <param name="ExpectedDescriptionRevision">改說明時必帶（get_item 回的 descriptionRevision）。</param>
public sealed record ItemBatchEntryDraft(
    string ItemKey, IReadOnlyList<ItemFieldChange> Changes, string? Comment, string? ExpectedDescriptionRevision)
{
    /// <summary>留言是回覆哪一則（規則同 <see cref="ItemProposalDraft.ReplyTo"/>）。</summary>
    public Guid? ReplyTo { get; init; }
}

/// <param name="ItemTitle">建立建議時的標題。</param>
/// <param name="Error">套用後這一筆沒成功的部分；還沒套用或全部成功是 null。</param>
public sealed record ItemBatchEntry(
    string ItemKey, string ItemTitle, IReadOnlyList<ItemFieldChange> Changes, string? Comment, string? Error)
{
    /// <summary>留言是回覆哪一則；新的一則是 null。</summary>
    public CommentReplyTarget? ReplyTo { get; init; }
}

public sealed record ItemBatchProposal(
    Guid Id, string ProjectKey, IReadOnlyList<ItemBatchEntry> Items, ItemProposalStatus Status, DateTimeOffset CreatedAt)
{
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>有幾筆沒完全套用：摘要，繁體中文。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 一則對話裡助理提出過的建議與它們現在的狀態，以及撤回。每次回覆前，伺服器用 <see cref="ListAsync"/> 把狀態寫成一則 system 訊息給模型
/// （模型看不到使用者按了什麼：不說的話，它會叫使用者「不要按」一份早就建立好的建議）；助理用 withdraw_proposal 撤回自己還沒套用的建議。
/// 「這則對話的建議」是對話存下的工具段落裡記著編號的（內建、本機、主機三條路都會記），不是靠建議自己的 conversation_id（MCP 建的是 null）。
/// </summary>
public interface IAssistantProposalLedger
{
    /// <summary>
    /// 這則對話的建議，最新的 <see cref="AiRules.MaxProposalsInLedger"/> 份、由舊到新，狀態是現在讀的。對話不是這個人的回空清單。
    /// 已經讀不到的建議（專案看不到了、被清掉了）不列。
    /// </summary>
    Task<IReadOnlyList<ProposalStatusEntry>> ListAsync(Actor actor, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// 撤回（作廢）一份還沒套用的建議。只能撤回這則對話裡、這個人自己的建議；變更集不能撤回（只有能核准的人能否決）。
    /// 不在這則對話裡、不存在、別人的：<see cref="AiErrorCodes.ProposalNotFound"/>；已經套用、作廢或建立中：<see cref="AiErrorCodes.ProposalClosed"/>；
    /// 變更集：<see cref="AiErrorCodes.ProposalNotWithdrawable"/>。撤回只丟掉助理自己的草稿、不碰使用者的資料，所以助理可以直接做，不必再提建議。
    /// </summary>
    Task<Result<ProposalStatusEntry>> WithdrawAsync(Actor actor, Guid conversationId, string reference, CancellationToken cancellationToken);
}

public enum ProposalKind
{
    ChangeSet,
    ItemChanges,
    BulkItemChanges,
    NewItems,
    ItemDeletions,
    Members,
    Project,
    Planning,
    Comments,
    ItemRestores,
    ProjectChanges,
}

public enum ProposalState
{
    Pending,

    /// <summary>新增項目的建議正在建立。</summary>
    Applying,

    Applied,

    /// <summary>使用者按了「不要」，或助理撤回了。</summary>
    Dismissed,

    /// <summary>變更集被否決。</summary>
    Rejected,
}

/// <param name="Reference">
/// 給模型與 withdraw_proposal 用的代號：建議的編號（GUID 字串），變更集是 <c>changeset:12</c>。
/// </param>
/// <param name="Summary">
/// 一行說明，只放編號與數字、不放標題（標題是使用者寫的內容），例如「新增 7 個項目，已建立 MALL-38、MALL-39…」
/// 「改 MALL-12 的 dueDate、priority」「刪除 3 個項目：MALL-45、MALL-46、MALL-47」「把 1 個人加進專案」。
/// </param>
public sealed record ProposalStatusEntry(
    string Reference, ProposalKind Kind, ProposalState State, string Summary, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);
