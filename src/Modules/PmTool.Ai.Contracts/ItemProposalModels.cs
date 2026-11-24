using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理對一個項目提出的修改建議：助理（內建的工具 propose_item_changes，或本機代理經 MCP）只建立建議、不改項目，
/// 使用者在網頁按「套用」才以自己的身分與權限寫入（<see cref="ApplyAsync"/>），按「不要」就作廢。規格見 <c>internal/HANDOFF-ai-items.md</c>。
/// 只有建立它的人看得到、能套用；別人的、不存在的一律 <see cref="AiErrorCodes.ItemProposalNotFound"/>。
/// </summary>
public interface IItemProposalService
{
    Task<Result<ItemProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 以 <paramref name="actor"/> 的身分照順序寫入：欄位一次（含說明，帶建立建議時的說明版本檢查衝突）、再改狀態、最後留言。
    /// 第一步就失敗時什麼都沒改、建議維持「待決定」，回傳那一步的失敗（工作引擎的代碼，例如 <c>item.description_conflict</c>）；
    /// 後面的步驟失敗時已經改的不復原，建議標成已套用並在 <see cref="ItemProposal.Error"/> 說明哪些沒有套用。
    /// 已經套用或作廢的：<see cref="AiErrorCodes.ItemProposalClosed"/>。
    /// </summary>
    Task<Result<ItemProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用或作廢的：<see cref="AiErrorCodes.ItemProposalClosed"/>。</summary>
    Task<Result<ItemProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

public enum ItemProposalStatus
{
    /// <summary>等使用者決定。</summary>
    Pending,

    Applied,

    Dismissed,
}

/// <summary>一份修改建議。</summary>
/// <param name="ItemKey">要改的項目，例如 <c>WEB-12</c>。</param>
/// <param name="ItemTitle">建立建議時的標題（給卡片顯示；項目之後改名，這裡還是當時的）。</param>
/// <param name="Changes">要改的欄位，依建議的順序；至少一個或有 <paramref name="Comment"/>。</param>
/// <param name="Comment">要加的留言（Markdown），沒有是 null。</param>
public sealed record ItemProposal(
    Guid Id,
    string ProjectKey,
    string ItemKey,
    string ItemTitle,
    IReadOnlyList<ItemFieldChange> Changes,
    string? Comment,
    ItemProposalStatus Status,
    DateTimeOffset CreatedAt)
{
    /// <summary>套用或作廢的時間。</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>套用時有步驟沒有成功：說明哪些沒有套用，繁體中文。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 一個欄位改成什麼。<see cref="Field"/> 是 <c>title</c>、<c>description</c>（Markdown）、<c>status</c>（狀態的 key）、
/// <c>assignee</c>（人員編號，null 是清掉）、<c>priority</c>（<c>none</c>、<c>low</c>、<c>medium</c>、<c>high</c>、<c>urgent</c>）、
/// <c>startDate</c>／<c>dueDate</c>（<c>yyyy-MM-dd</c>，null 是清掉）、<c>estimate</c>（數字，null 是清掉），
/// 或 <c>fields.&lt;自訂欄位的 key&gt;</c>（值照藍圖的欄位型別，null 是清掉）。同一個欄位在一份建議裡只出現一次。
/// </summary>
public sealed record ItemFieldChange(string Field, JsonElement Value);
