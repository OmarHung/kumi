using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理提出的「復原已刪除的項目」建議（內建的 propose_item_restores，或本機、主機代理經 MCP 的同名工具）：使用者在網頁按「復原」
/// 才以自己的身分照工作引擎的規則復原（<c>IBulkItemService.RestoreAsync</c>，上下層一起在這份裡時先復原上層），按「不要」就作廢。
/// 只有建立它的人看得到、能套用；別人的、不存在的、專案已經看不到的一律 <see cref="AiErrorCodes.ItemRestoreProposalNotFound"/>。
/// 規格見 <c>internal/HANDOFF-ai-coverage.md</c> 5.6。
/// </summary>
public interface IItemRestoreProposalService
{
    Task<Result<ItemRestoreProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 一次批次復原。復原是冪等的（沒有刪除的算成功），所以只在建議的 advisory lock 裡排隊。一筆都沒復原成（工作引擎全部拒絕）時維持待決定、
    /// 回傳第一筆的失敗；有復原成的就標成已套用，沒成的寫進 <see cref="ItemRestoreProposal.Error"/>。
    /// 逾時回 <see cref="AiErrorCodes.ApplyTimedOut"/> 並維持待決定，再按一次是安全的。已經套用或作廢的：<see cref="AiErrorCodes.ItemRestoreProposalClosed"/>。
    /// </summary>
    Task<Result<ItemRestoreProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用或作廢的：<see cref="AiErrorCodes.ItemRestoreProposalClosed"/>。</summary>
    Task<Result<ItemRestoreProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>建立復原建議。內建助理與 MCP 的 propose_item_restores 都呼叫它，檢查的規則只有一份。</summary>
public interface IItemRestoreProposalCreator
{
    /// <summary>
    /// 專案要看得到（不然原樣回傳 <c>project.not_found</c>）；使用者沒有 <c>item.delete</c> 時回 <see cref="AiErrorCodes.Forbidden"/>（「你沒有復原項目的權限。」）。
    /// 每一筆的問題回 <see cref="AiErrorCodes.FieldInvalid"/>，路徑 <c>itemKeys[2]</c>，一次回全部，什麼都不存：
    /// 1 到 <see cref="AiRules.MaxItemsPerDeletionProposal"/> 個、不重複；每個都要是這個專案已刪除的項目（<c>IWorkItemService.ListDeletedAsync</c>
    /// 從最近的往回翻，最多 <see cref="AiRules.MaxDeletedPagesScanned"/> 頁；翻完還找不到的寫「找不到這個已刪除的項目（太久以前刪的請使用者到已刪除頁復原）」）；
    /// 上層也是刪除的、又不在這份裡時是錯誤，訊息寫出上層的編號，讓模型問使用者要不要一起復原。
    /// 存起來的會記下每個項目刪除時的標題與刪除的時間（給卡片顯示）。
    /// </summary>
    Task<Result<ItemRestoreProposal>> CreateAsync(Actor actor, ItemRestoreProposalDraft draft, CancellationToken cancellationToken);
}

/// <param name="ItemKeys">要復原的項目編號，依建議的順序。</param>
public sealed record ItemRestoreProposalDraft(string ProjectKey, IReadOnlyList<string> ItemKeys)
{
    /// <summary>內建助理建立的才有；MCP 是 null。</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>為什麼復原（一行，最長 200 字），給卡片顯示；可以不給。擋控制字元。</summary>
    public string? Reason { get; init; }
}

/// <param name="Title">建立建議時的標題。</param>
/// <param name="DeletedAt">刪除的時間。</param>
public sealed record ItemRestoreTarget(string ItemKey, string Title, DateTimeOffset DeletedAt);

public sealed record ItemRestoreProposal(
    Guid Id, string ProjectKey, IReadOnlyList<ItemRestoreTarget> Items, ItemProposalStatus Status, DateTimeOffset CreatedAt)
{
    public string? Reason { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>套用之後真的復原的編號（含原本就沒刪除的）。</summary>
    public IReadOnlyList<string> RestoredKeys { get; init; } = [];

    /// <summary>有幾筆沒復原成：說明哪些、為什麼，繁體中文。</summary>
    public string? Error { get; init; }
}
