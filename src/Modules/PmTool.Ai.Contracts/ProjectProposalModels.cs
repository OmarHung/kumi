using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理在不選專案的組織對話裡提出的新專案建議：助理（內建的工具 propose_project，或本機與主機的引擎經 MCP 的同名工具）
/// 只建立建議、不建立專案，使用者在網頁按「建立專案」才以自己的身分與權限建立（<see cref="ApplyAsync"/>，走工作引擎的
/// <c>IProjectService.CreateAsync</c>，所以只有組織管理員建得成），按「不要」就作廢。規格見 <c>internal/HANDOFF-org-assistant.md</c>。
/// 只有建立它的人看得到、能決定；別人的、不存在的一律 <see cref="AiErrorCodes.ProjectProposalNotFound"/>。
/// </summary>
public interface IProjectProposalService
{
    Task<Result<ProjectProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 以 <paramref name="actor"/> 的身分建立專案。建立失敗（例如代號在建議之後被別人用掉、已經不是管理員）時什麼都沒建、
    /// 建議維持「待決定」，回傳工作引擎的失敗（代碼照舊，例如 <c>project.key_taken</c>）。成功時建議標成已建立，
    /// <see cref="ProjectProposal.CreatedProjectKey"/> 是建好的專案代號。已經建立或作廢的：<see cref="AiErrorCodes.ProjectProposalClosed"/>。
    /// </summary>
    Task<Result<ProjectProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經建立或作廢的：<see cref="AiErrorCodes.ProjectProposalClosed"/>。</summary>
    Task<Result<ProjectProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>
/// 建立新專案建議（<see cref="IProjectProposalService"/> 之外的另一半）。內建助理的 propose_project 與 MCP 的同名工具都呼叫它，
/// 檢查的規則只有一份：先擋權限（不是組織管理員回 <see cref="AiErrorCodes.AssistantForbidden"/>，訊息說只有組織管理員能開新專案），
/// 再用工作引擎的 <c>IProjectService.CheckNewProjectAsync</c> 檢查代號、名稱、模板與說明（不過時回它的失敗，細項照舊），什麼都不存。
/// </summary>
public interface IProjectProposalCreator
{
    Task<Result<ProjectProposal>> CreateAsync(Actor actor, ProjectProposalDraft draft, CancellationToken cancellationToken);
}

/// <param name="Key">新專案的代號（規則和網頁的「新專案」相同，由工作引擎檢查）。</param>
/// <param name="Name">新專案的名稱。</param>
/// <param name="Template">模板的 key（<c>list_templates</c> 列的）。</param>
/// <param name="Description">一起寫進去的專案說明（Markdown）；沒有是 null。</param>
public sealed record ProjectProposalDraft(string Key, string Name, string Template, bool IncludeExamples, string? Description)
{
    /// <summary>內建助理與本機、主機的引擎建立的都帶（引擎的由伺服器從工作找到）；沒有對話時是 null。</summary>
    public Guid? ConversationId { get; init; }
}

public enum ProjectProposalStatus
{
    /// <summary>等使用者決定。</summary>
    Pending,

    Applied,

    Dismissed,
}

/// <summary>一份新專案建議。</summary>
/// <param name="TemplateName">建立建議時模板的顯示名稱（給卡片顯示）。</param>
public sealed record ProjectProposal(
    Guid Id,
    string Key,
    string Name,
    string Template,
    string TemplateName,
    bool IncludeExamples,
    string? Description,
    ProjectProposalStatus Status,
    DateTimeOffset CreatedAt)
{
    /// <summary>建立或作廢的時間。</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>建立成功時的專案代號（和 <see cref="Key"/> 相同，工作引擎可能把大小寫正規化）。</summary>
    public string? CreatedProjectKey { get; init; }
}
