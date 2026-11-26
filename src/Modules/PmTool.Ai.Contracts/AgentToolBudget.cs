using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 本機代理與主機代理的一件工作能用多少工具：伺服器端計，不靠送給引擎的參數（internal/HANDOFF-security-fixes.md 4B、AG-07）。
/// 每次工具呼叫在執行之前先 <c>Use…Async</c>；提出類的工具（propose_changeset、propose_item_changes）同時佔一份提出的名額，
/// 執行失敗（工具的錯誤、例外、取消）要呼叫 <see cref="RefundProposalAsync"/> 退還那一份（呼叫次數不退）。
/// 數與加在同一個條件式的 UPDATE 裡，同時的呼叫不會一起超過上限。
/// </summary>
public interface IAgentToolBudget
{
    /// <summary>
    /// 本機代理（權杖有 <c>agent:run</c>，請求帶 <c>X-Kumi-Job</c>）：工作要是這個人的、已經被領走、還沒結束，專案代號要和 <c>X-Kumi-Project</c> 相同
    /// （<paramref name="projectKey"/> 是 null 時，工作的對話也要沒有專案：不選專案的組織對話）；不然 <see cref="AgentToolBudgetOutcome.JobNotFound"/>。
    /// </summary>
    Task<AgentToolBudgetOutcome> UseLocalAsync(
        Actor actor, Guid jobId, string? projectKey, bool isProposal, CancellationToken cancellationToken);

    /// <summary>
    /// 工作屬於哪則對話、那則對話有沒有專案：MCP 用它決定範圍（限定專案或只限組織），<c>propose_project</c> 用它記對話。
    /// 本機（<see cref="AgentJobKind.Local"/>）要 <paramref name="actor"/> 是工作的主人、工作已經被領走、還沒結束；
    /// 主機（<see cref="AgentJobKind.Host"/>）只看工作還沒結束（<paramref name="actor"/> 不用）。找不到是 null。
    /// </summary>
    Task<AgentJobScope?> FindJobScopeAsync(AgentJobKind kind, Actor? actor, Guid jobId, CancellationToken cancellationToken);

    /// <summary>主機代理（<c>kjt_</c> 授權，工作編號來自授權本身）。工作已經結束是 <see cref="AgentToolBudgetOutcome.JobNotFound"/>。</summary>
    Task<AgentToolBudgetOutcome> UseHostAsync(Guid jobId, bool isProposal, CancellationToken cancellationToken);

    /// <summary>提出類的工具沒有成功：退還那一份提出的名額。不會失敗（找不到就算了），不跟著請求取消。</summary>
    Task RefundProposalAsync(AgentJobKind kind, Guid jobId);
}

/// <param name="ProjectKey">對話所屬的專案；不選專案的組織對話是 null。</param>
public sealed record AgentJobScope(Guid ConversationId, string? ProjectKey);

public enum AgentJobKind
{
    Local,

    Host,
}

public enum AgentToolBudgetOutcome
{
    Allowed,

    /// <summary>這件工作的工具呼叫已經 <see cref="AiRules.MaxAgentToolCallsPerJob"/> 次。</summary>
    ToolCallsExhausted,

    /// <summary>這件工作已經成功提出 <see cref="AiRules.MaxAgentProposalsPerJob"/> 份。</summary>
    ProposalsExhausted,

    JobNotFound,
}
