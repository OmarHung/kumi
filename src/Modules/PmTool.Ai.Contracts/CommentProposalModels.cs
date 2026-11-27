using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理提出的「留言」建議（內建的 propose_comment_changes，或本機、主機代理經 MCP 的同名工具）：編輯、刪除、復原既有的留言，
/// 可以跨同一個專案的好幾個項目，一份最多 <see cref="AiRules.MaxCommentChangesPerProposal"/> 筆。新的留言與回覆不在這裡，
/// 是修改建議的 comment／replyTo。使用者在網頁按「套用」才以自己的身分照工作引擎的規則寫入（<c>IItemActivityService</c> 的
/// <c>EditCommentAsync</c>／<c>DeleteCommentAsync</c>／<c>RestoreCommentAsync</c>），按「不要」就作廢。
/// 只有建立它的人看得到、能套用；別人的、不存在的、專案已經看不到的一律 <see cref="AiErrorCodes.CommentProposalNotFound"/>。
/// 規格見 <c>internal/HANDOFF-ai-coverage.md</c> 5.5。
/// </summary>
public interface ICommentProposalService
{
    Task<Result<CommentProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 三種寫入都是冪等的（內容一樣不改、已經刪除或沒刪除的什麼都不做），只在建議的 advisory lock 裡排隊、照順序一筆一筆做。
    /// 每一筆都失敗時維持待決定、回傳第一筆的失敗；有任何一筆成功就標成已套用，每一筆的結果在 <see cref="CommentChange.Error"/>、
    /// 整份的摘要在 <see cref="CommentProposal.Error"/>。已經套用或作廢的：<see cref="AiErrorCodes.CommentProposalClosed"/>。
    /// </summary>
    Task<Result<CommentProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用或作廢的：<see cref="AiErrorCodes.CommentProposalClosed"/>。</summary>
    Task<Result<CommentProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>建立留言建議。內建助理與 MCP 的 propose_comment_changes 都呼叫它，檢查的規則只有一份。</summary>
public interface ICommentProposalCreator
{
    /// <summary>
    /// 專案要看得到（不然原樣回傳 <c>project.not_found</c>）；封存的專案回 <c>project.archived</c>。
    /// 每一筆的問題回 <see cref="AiErrorCodes.FieldInvalid"/>，路徑 <c>changes[2].commentId</c>，一次回全部，什麼都不存：
    /// <list type="bullet">
    /// <item>1 到 <see cref="AiRules.MaxCommentChangesPerProposal"/> 筆；同一則留言只能出現一次；</item>
    /// <item>項目要是這個專案看得到的；留言要是那個項目的留言（不存在、別的項目的、不是留言的一律「找不到這則留言」）；</item>
    /// <item>編輯：要給 body（規則同留言：去掉頭尾空白後不能是空的、最長 <c>WorkRules.MaxCommentLength</c>、只能有換行與 Tab 兩種控制字元）、
    ///   要和現在的內容不同；只能編輯使用者自己的、沒刪除的、不是匯入的留言；</item>
    /// <item>刪除：要是沒刪除的；別人的留言要有 <c>project.manage</c>；</item>
    /// <item>復原：要是刪除的；照 <c>RestoreCommentAsync</c> 的規則（作者自己刪的，作者或 project.manage；別人刪的只有 project.manage）。</item>
    /// </list>
    /// 不能給 body 的（刪除、復原）給了也是錯誤。權限不夠的那一筆，訊息寫為什麼不行（例如「這是別人的留言，只有作者能編輯」）。
    /// 存起來的每一筆帶作者的顯示名稱、留言的時間與建立建議時內容的摘錄（最多 <see cref="AiRules.MaxCommentExcerptLength"/> 字，刪除的留言讀不到內容是 null）。
    /// </summary>
    Task<Result<CommentProposal>> CreateAsync(Actor actor, CommentProposalDraft draft, CancellationToken cancellationToken);
}

public sealed record CommentProposalDraft(string ProjectKey, IReadOnlyList<CommentChangeDraft> Changes)
{
    /// <summary>內建助理建立的才有；MCP 是 null。</summary>
    public Guid? ConversationId { get; init; }
}

public enum CommentAction
{
    Edit,
    Delete,
    Restore,
}

/// <param name="Body">編輯才給：新的內容（Markdown）。</param>
public sealed record CommentChangeDraft(string ItemKey, Guid CommentId, CommentAction Action, string? Body);

/// <param name="AuthorName">留言作者的顯示名稱（建立建議時的）。</param>
/// <param name="CommentedAt">這則留言第一次留的時間。</param>
/// <param name="Excerpt">建立建議時內容的摘錄；刪除的留言是 null。</param>
/// <param name="Body">編輯才有：新的內容。</param>
/// <param name="Error">套用後這一筆沒成功的原因；還沒套用或成功是 null。</param>
public sealed record CommentChange(
    string ItemKey,
    Guid CommentId,
    CommentAction Action,
    string AuthorName,
    DateTimeOffset CommentedAt,
    string? Excerpt,
    string? Body,
    string? Error);

public sealed record CommentProposal(
    Guid Id, string ProjectKey, IReadOnlyList<CommentChange> Changes, ItemProposalStatus Status, DateTimeOffset CreatedAt)
{
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>有幾筆沒成功：摘要，繁體中文。</summary>
    public string? Error { get; init; }
}
