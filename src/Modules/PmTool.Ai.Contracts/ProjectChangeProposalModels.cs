using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理提出的「改專案」建議（內建的 propose_project_changes，或本機、主機代理經 MCP 的同名工具）：改名稱、封存或取消封存、
/// 改專案說明（資訊頁）。使用者在網頁按「套用」才以自己的身分照工作引擎的規則寫入（<c>IProjectDescriptionService.SetAsync</c>、
/// <c>IProjectService.RenameAsync</c>／<c>ArchiveAsync</c>／<c>UnarchiveAsync</c>），按「不要」就作廢。
/// 只有建立它的人看得到、能套用；別人的、不存在的、專案已經看不到的一律 <see cref="AiErrorCodes.ProjectChangeProposalNotFound"/>。
/// 規格見 <c>internal/HANDOFF-ai-coverage.md</c> 5.4。
/// </summary>
public interface IProjectChangeProposalService
{
    Task<Result<ProjectChangeProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 三個寫入都是冪等的，只在建議的 advisory lock 裡排隊。順序：說明 → 名稱 → 封存或取消封存。
    /// 改說明時，專案說明現在的 <c>UpdatedAt</c> 和建立建議時不同（別人在這中間改過）就整份不做、維持待決定，回
    /// <see cref="AiErrorCodes.ProjectChangeConflict"/>。第一個真的要做的步驟失敗時什麼都沒改、維持待決定、回傳工作引擎的失敗；
    /// 之後的失敗不復原已經改的，標成已套用、<see cref="ProjectChangeProposal.Error"/> 說明哪些沒套用。
    /// 和直接封存專案的端點一樣不發 <c>AccessChanged</c>：封存的專案照樣看得到（只是唯讀），即時連線的群組不變。
    /// 已經套用或作廢的：<see cref="AiErrorCodes.ProjectChangeProposalClosed"/>。
    /// </summary>
    Task<Result<ProjectChangeProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用或作廢的：<see cref="AiErrorCodes.ProjectChangeProposalClosed"/>。</summary>
    Task<Result<ProjectChangeProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>建立改專案的建議。內建助理與 MCP 的 propose_project_changes 都呼叫它，檢查的規則只有一份。</summary>
public interface IProjectChangeProposalCreator
{
    /// <summary>
    /// 專案要看得到（不然原樣回傳 <c>project.not_found</c>）；使用者沒有 <c>project.manage</c> 時回 <see cref="AiErrorCodes.Forbidden"/>，
    /// 訊息照成員建議的句型寫出能做的人。問題回 <see cref="AiErrorCodes.FieldInvalid"/>，路徑是欄位名稱，一次回全部，什麼都不存：
    /// <list type="bullet">
    /// <item>名稱、封存、說明至少給一個，而且給了的要和現在不同（全部都和現在一樣時路徑是空字串）；</item>
    /// <item>名稱照 <c>RenameAsync</c> 的規則（去掉前後空白後 1 到 <c>WorkRules.MaxProjectNameLength</c> 字、不能有控制字元）；</item>
    /// <item>說明照 <c>SetAsync</c> 的規則（最長 <c>ProjectDescriptionRules.MaxTextLength</c> 字、只能有換行與 Tab 兩種控制字元、
    ///   不能有私鑰或看得出格式的金鑰：<c>SecretText</c>）；專案現在的說明超過 <see cref="AiRules.MaxEditableProjectDescriptionLength"/> 字時
    ///   不能改說明（助理讀不到全文，會蓋掉後面的內容）。</item>
    /// </list>
    /// 存起來的帶建立建議時的名稱、封存狀態、說明全文與說明的 <c>UpdatedAt</c>（給卡片比對與套用時檢查衝突）。
    /// </summary>
    Task<Result<ProjectChangeProposal>> CreateAsync(Actor actor, ProjectChangeProposalDraft draft, CancellationToken cancellationToken);
}

/// <param name="Name">新的名稱；null 是不改。</param>
/// <param name="Archived">true 是封存、false 是取消封存；null 是不改。</param>
/// <param name="Description">要不要改說明：<see cref="Optional{T}.HasValue"/> 是 false 時不改；值是 null 或整段空白是清掉。</param>
public sealed record ProjectChangeProposalDraft(string ProjectKey, string? Name, bool? Archived, Optional<string?> Description)
{
    /// <summary>內建助理建立的才有；MCP 是 null。</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>為什麼這樣改（一行，最長 200 字），給卡片顯示；可以不給。擋控制字元。</summary>
    public string? Reason { get; init; }
}

/// <summary>建立建議時專案的樣子。</summary>
/// <param name="DescriptionUpdatedAt">專案說明上次修改的時間；沒寫過是 null。套用時用它檢查別人有沒有改過。</param>
public sealed record ProjectSnapshot(string Name, bool Archived, string? Description, DateTimeOffset? DescriptionUpdatedAt);

/// <param name="Name">改成的名稱；不改是 null。</param>
/// <param name="Archived">改成的封存狀態；不改是 null。</param>
/// <param name="ChangesDescription">有沒有改說明。</param>
/// <param name="Description">改成的說明；<paramref name="ChangesDescription"/> 是 true 時 null 是清掉。</param>
/// <param name="Before">建立建議時的樣子。</param>
public sealed record ProjectChangeProposal(
    Guid Id,
    string ProjectKey,
    string? Name,
    bool? Archived,
    bool ChangesDescription,
    string? Description,
    ProjectSnapshot Before,
    ItemProposalStatus Status,
    DateTimeOffset CreatedAt)
{
    public string? Reason { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>有步驟沒套用成功：說明哪些，繁體中文。</summary>
    public string? Error { get; init; }
}
