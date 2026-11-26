using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理提出的「專案成員」建議（加入、改角色、移出）：助理（內建的 propose_member_changes，或本機、主機代理經 MCP 的同名工具）只建立建議，
/// 使用者在網頁按「套用」才以自己的身分照工作引擎的權限寫入（<c>IProjectService.SetMemberAsync</c>／<c>RemoveMemberAsync</c>），按「不要」就作廢。
/// 只有建立它的人看得到、能套用；別人的、不存在的、專案已經看不到的一律 <see cref="AiErrorCodes.MemberProposalNotFound"/>。
/// 規格見 <c>internal/HANDOFF-ai-assign.md</c>。
/// </summary>
public interface IMemberProposalService
{
    Task<Result<MemberProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 照順序寫入。成員的寫入是冪等的（同樣的角色再設一次不會怎樣），所以不需要「套用中」的狀態，只在建議的 advisory lock 裡排隊。
    /// 第一筆就失敗時什麼都沒改、維持待決定，回傳工作引擎的失敗（代碼與訊息原樣，例如 <c>project.last_admin</c>）；
    /// 後面的失敗時已經改的不復原，標成已套用、<see cref="MemberProposal.Error"/> 說明哪些沒套用。
    /// 已經套用或作廢的：<see cref="AiErrorCodes.MemberProposalClosed"/>。
    /// </summary>
    Task<Result<MemberProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用或作廢的：<see cref="AiErrorCodes.MemberProposalClosed"/>。</summary>
    Task<Result<MemberProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>建立成員建議。內建助理與 MCP 的 propose_member_changes 都呼叫它，檢查的規則只有一份。</summary>
public interface IMemberProposalCreator
{
    /// <summary>
    /// 專案要看得到（不然原樣回傳 <c>project.not_found</c>）。使用者在專案裡沒有 <c>project.manage</c>（組織管理員一律有）時回 <see cref="AiErrorCodes.Forbidden"/>，
    /// 訊息寫出誰能做：「你沒有管理這個專案成員的權限。能管理成員的是：甲、乙（專案的管理者），以及組織管理員。」
    /// （名單是角色有 project.manage、帳號沒停用的成員，最多 10 個；沒有人時只寫組織管理員），讓助理請使用者去找他們。
    /// 每一筆的檢查不過時回 <see cref="AiErrorCodes.FieldInvalid"/>，每個問題一個細項（路徑 <c>changes[1].role</c>），什麼都不存：
    /// <list type="bullet">
    /// <item>1 到 <see cref="AiRules.MaxMemberChangesPerProposal"/> 筆，同一個人只能出現一次；</item>
    /// <item>人要在這個組織裡；加入或改角色時帳號不能是停用的；</item>
    /// <item>加入或改角色：角色要是 <c>IProjectStaffingQueries.ListRolesAsync</c> 列出的（錯誤列出可用的角色 key 與名稱）；已經是這個角色時是錯誤（「已經是 X 了」）；</item>
    /// <item>移出：要是現在的成員。</item>
    /// </list>
    /// </summary>
    Task<Result<MemberProposal>> CreateAsync(Actor actor, MemberProposalDraft draft, CancellationToken cancellationToken);
}

public sealed record MemberProposalDraft(string ProjectKey, IReadOnlyList<MemberChangeDraft> Changes)
{
    /// <summary>內建助理建立的才有；MCP 是 null。</summary>
    public Guid? ConversationId { get; init; }
}

/// <param name="Role">加入或改成這個角色；null 是移出專案。</param>
public sealed record MemberChangeDraft(Guid PersonId, string? Role);

public enum MemberProposalStatus
{
    /// <summary>等使用者決定。</summary>
    Pending,

    Applied,

    Dismissed,
}

/// <summary>一筆成員的變動。名稱都是建立建議時的（給卡片顯示；之後改名，這裡還是當時的）。</summary>
/// <param name="DisplayName">這個人的顯示名稱。</param>
/// <param name="Role">改成的角色 key；null 是移出。</param>
/// <param name="RoleName">改成的角色的名稱；移出是 null。</param>
/// <param name="PreviousRole">建立建議時的角色 key；還不是成員是 null。</param>
/// <param name="PreviousRoleName">建立建議時的角色的名稱；還不是成員是 null。</param>
public sealed record MemberChange(
    Guid PersonId, string DisplayName, string? Role, string? RoleName, string? PreviousRole, string? PreviousRoleName);

public sealed record MemberProposal(
    Guid Id, string ProjectKey, IReadOnlyList<MemberChange> Changes, MemberProposalStatus Status, DateTimeOffset CreatedAt)
{
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>有幾筆沒套用成功：說明哪些沒套用，繁體中文。</summary>
    public string? Error { get; init; }
}
