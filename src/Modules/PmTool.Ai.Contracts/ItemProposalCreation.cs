using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 建立修改建議（<see cref="IItemProposalService"/> 之外的另一半）。內建助理的 propose_item_changes 與 MCP 的同名工具都呼叫它，
/// 檢查的規則只有一份。只存建議、不改項目。
/// </summary>
public interface IItemProposalCreator
{
    /// <summary>
    /// 項目要在 <see cref="ItemProposalDraft.ProjectKey"/> 這個專案、<paramref name="actor"/> 看得到（不然 <see cref="AiErrorCodes.ItemNotFound"/>）；
    /// 欄位與留言的檢查不過時回 <see cref="AiErrorCodes.FieldInvalid"/>，每個問題一個細項，什麼都不存。
    /// </summary>
    Task<Result<ItemProposal>> CreateAsync(Actor actor, ItemProposalDraft draft, CancellationToken cancellationToken);
}

/// <param name="Changes">1 到 <see cref="AiRules.MaxItemProposalChanges"/> 個欄位；只有留言時可以是空的。</param>
/// <param name="Comment">要加的留言（Markdown）；沒有是 null。</param>
public sealed record ItemProposalDraft(string ProjectKey, string ItemKey, IReadOnlyList<ItemFieldChange> Changes, string? Comment)
{
    /// <summary>內建助理建立的才有；MCP 是 null。</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>
    /// 要改說明（<c>description</c>）時必填：模型讀項目時拿到的說明版本（get_item 的 descriptionRevision）。套用時用它檢查，
    /// 讀了之後說明被別人改過就整份不套用。沒有改說明時不用給。
    /// </summary>
    public string? DescriptionRevision { get; init; }
}
