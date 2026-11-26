using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 測試用的代理工具額度：預設每次都放行，記住每次呼叫與退還；<see cref="Outcome"/> 換成別的就模擬額度用完、工作對不上。
/// 真正的計數與條件式 UPDATE 在 AI 模組的測試（<c>AgentToolBudgetTests</c>）和 RealStack 的端到端測試裡驗。
/// </summary>
internal sealed class FakeAgentToolBudget : IAgentToolBudget
{
    private readonly ConcurrentQueue<(Guid JobId, string? ProjectKey, bool IsProposal)> _local = new();
    private readonly ConcurrentQueue<(Guid JobId, bool IsProposal)> _host = new();
    private readonly ConcurrentQueue<(AgentJobKind Kind, Guid JobId)> _refunds = new();

    public AgentToolBudgetOutcome Outcome { get; set; } = AgentToolBudgetOutcome.Allowed;

    public IReadOnlyList<(Guid JobId, string? ProjectKey, bool IsProposal)> LocalCalls => _local.ToArray();

    public IReadOnlyList<(Guid JobId, bool IsProposal)> HostCalls => _host.ToArray();

    public IReadOnlyList<(AgentJobKind Kind, Guid JobId)> Refunds => _refunds.ToArray();

    public Task<AgentToolBudgetOutcome> UseLocalAsync(
        Actor actor, Guid jobId, string? projectKey, bool isProposal, CancellationToken cancellationToken)
    {
        _local.Enqueue((jobId, projectKey, isProposal));
        return Task.FromResult(Outcome);
    }

    public Task<AgentToolBudgetOutcome> UseHostAsync(Guid jobId, bool isProposal, CancellationToken cancellationToken)
    {
        _host.Enqueue((jobId, isProposal));
        return Task.FromResult(Outcome);
    }

    public static readonly Guid ConversationId = Guid.Parse("99999999-9999-9999-9999-999999999991");

    /// <summary>
    /// 工作的範圍；預設是 <c>WEB</c> 專案的對話（既有的本機代理測試都用 WEB；<c>McpHarness</c> 依連線帶的專案標頭換成那個專案）。
    /// 要測組織對話、找不到工作時換成 <see cref="OrganizationJob"/>、<c>null</c>。
    /// </summary>
    public Func<AgentJobKind, Guid, AgentJobScope?> OnFindJobScope { get; set; } = (_, _) => ProjectJob("WEB");

    public static AgentJobScope ProjectJob(string projectKey) => new(ConversationId, projectKey);

    public static AgentJobScope OrganizationJob { get; } = new(ConversationId, null);

    public IReadOnlyList<(AgentJobKind Kind, Guid JobId)> ScopeLookups => _lookups.ToArray();

    private readonly ConcurrentQueue<(AgentJobKind Kind, Guid JobId)> _lookups = new();

    public Task<AgentJobScope?> FindJobScopeAsync(AgentJobKind kind, Actor? actor, Guid jobId, CancellationToken cancellationToken) =>
        Lookup(kind, jobId);

    private Task<AgentJobScope?> Lookup(AgentJobKind kind, Guid jobId)
    {
        _lookups.Enqueue((kind, jobId));
        return Task.FromResult(OnFindJobScope(kind, jobId));
    }

    public Task RefundProposalAsync(AgentJobKind kind, Guid jobId)
    {
        _refunds.Enqueue((kind, jobId));
        return Task.CompletedTask;
    }
}
