using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 代理一件工作能用多少工具（AG-07）。計數加在同一個條件式的 UPDATE 裡：同時來的呼叫不會一起超過上限，
/// 也不需要先讀再寫。上限由伺服器定（<see cref="AiRules.MaxAgentToolCallsPerJob"/>、<see cref="AiRules.MaxAgentProposalsPerJob"/>），
/// 不靠送給引擎的參數：引擎被提示注入時，參數擋不住它。
/// </summary>
internal sealed class AgentToolBudget(IDbContextFactory<AiDbContext> contexts, IProjectService projects) : IAgentToolBudget
{
    public async Task<AgentJobScope?> FindJobScopeAsync(AgentJobKind kind, Actor? actor, Guid jobId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (kind == AgentJobKind.Host)
        {
            // 主機的工作列在預留時就記了專案代號（組織對話是 null），授權本身不帶 actor，所以不再查專案的可見度。
            var host = await db.HostJobs.AsNoTracking()
                .Where(j => j.Id == jobId && j.State == HostJobStates.Running && !j.CancelRequested)
                .Select(j => new { j.ConversationId, j.ProjectKey })
                .SingleOrDefaultAsync(cancellationToken);
            return host is null ? null : new AgentJobScope(host.ConversationId, host.ProjectKey);
        }

        if (actor is null)
        {
            return null;
        }

        var org = actor.OrganizationId;
        var person = actor.PersonId;
        var local = await (
            from j in db.LocalJobs.AsNoTracking()
            join c in db.Conversations.AsNoTracking() on j.ConversationId equals c.Id
            where j.Id == jobId && j.OrganizationId == org && j.PersonId == person
                && j.State == LocalJobStates.Claimed && !j.CancelRequested && c.OrganizationId == org
            select new { j.ConversationId, c.ProjectId }).SingleOrDefaultAsync(cancellationToken);
        if (local is null)
        {
            return null;
        }

        if (local.ProjectId is not { } projectId)
        {
            return new AgentJobScope(local.ConversationId, null);
        }

        // 工作列的內容在領走時就清了，專案代號要從對話的專案編號找回來；專案要是呼叫的人看得到的，看不到就當作找不到工作。
        var visible = await projects.ListAsync(actor, cancellationToken);
        return visible.FirstOrDefault(p => p.Id == projectId) is { } project ? new AgentJobScope(local.ConversationId, project.Key) : null;
    }

    public async Task<AgentToolBudgetOutcome> UseLocalAsync(
        Actor actor, Guid jobId, string? projectKey, bool isProposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // 工作列的內容在領走時就清了，專案代號不在裡面：用對話的專案編號比對，專案要是呼叫的人看得到的。
        // 沒有專案代號是不選專案的組織對話：工作的對話也要沒有專案，不能拿它去用某個專案的工具。
        Guid? projectId = null;
        if (projectKey is not null)
        {
            var project = await projects.GetAsync(actor, projectKey, cancellationToken);
            if (project.Failure is not null)
            {
                return AgentToolBudgetOutcome.JobNotFound;
            }

            projectId = project.Value.Project.Id;
        }

        var org = actor.OrganizationId;
        var person = actor.PersonId;
        var proposalStep = isProposal ? 1 : 0;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var updated = await db.LocalJobs
            .Where(j => j.Id == jobId && j.OrganizationId == org && j.PersonId == person
                && j.State == LocalJobStates.Claimed && !j.CancelRequested
                && j.ToolCalls < AiRules.MaxAgentToolCallsPerJob
                && (!isProposal || j.Proposals < AiRules.MaxAgentProposalsPerJob)
                && db.Conversations.Any(c => c.Id == j.ConversationId && c.OrganizationId == org && c.ProjectId == projectId))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.ToolCalls, j => j.ToolCalls + 1)
                    .SetProperty(j => j.Proposals, j => j.Proposals + proposalStep),
                cancellationToken);
        if (updated == 1)
        {
            return AgentToolBudgetOutcome.Allowed;
        }

        var row = await db.LocalJobs.AsNoTracking()
            .Where(j => j.Id == jobId && j.OrganizationId == org && j.PersonId == person
                && j.State == LocalJobStates.Claimed && !j.CancelRequested
                && db.Conversations.Any(c => c.Id == j.ConversationId && c.OrganizationId == org && c.ProjectId == projectId))
            .Select(j => new { j.ToolCalls, j.Proposals })
            .SingleOrDefaultAsync(cancellationToken);
        return Classify(row?.ToolCalls, row?.Proposals);
    }

    public async Task<AgentToolBudgetOutcome> UseHostAsync(Guid jobId, bool isProposal, CancellationToken cancellationToken)
    {
        var proposalStep = isProposal ? 1 : 0;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var updated = await db.HostJobs
            .Where(j => j.Id == jobId && j.State == HostJobStates.Running && !j.CancelRequested
                && j.ToolCalls < AiRules.MaxAgentToolCallsPerJob
                && (!isProposal || j.Proposals < AiRules.MaxAgentProposalsPerJob))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.ToolCalls, j => j.ToolCalls + 1)
                    .SetProperty(j => j.Proposals, j => j.Proposals + proposalStep),
                cancellationToken);
        if (updated == 1)
        {
            return AgentToolBudgetOutcome.Allowed;
        }

        var row = await db.HostJobs.AsNoTracking()
            .Where(j => j.Id == jobId && j.State == HostJobStates.Running && !j.CancelRequested)
            .Select(j => new { j.ToolCalls, j.Proposals })
            .SingleOrDefaultAsync(cancellationToken);
        return Classify(row?.ToolCalls, row?.Proposals);
    }

    public async Task RefundProposalAsync(AgentJobKind kind, Guid jobId)
    {
        // 退還不跟著請求取消：請求斷線時名額也要放回去；找不到或已經是 0 就算了。
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        if (kind == AgentJobKind.Local)
        {
            await db.LocalJobs.Where(j => j.Id == jobId && j.Proposals > 0)
                .ExecuteUpdateAsync(set => set.SetProperty(j => j.Proposals, j => j.Proposals - 1), CancellationToken.None);
            return;
        }

        await db.HostJobs.Where(j => j.Id == jobId && j.Proposals > 0)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.Proposals, j => j.Proposals - 1), CancellationToken.None);
    }

    // 沒更新到：工作不在（或已結束、專案不符）、呼叫次數滿了、提出的份數滿了。工具呼叫的次數先看。
    private static AgentToolBudgetOutcome Classify(int? toolCalls, int? proposals)
    {
        if (toolCalls is null || proposals is null)
        {
            return AgentToolBudgetOutcome.JobNotFound;
        }

        return toolCalls >= AiRules.MaxAgentToolCallsPerJob
            ? AgentToolBudgetOutcome.ToolCallsExhausted
            : AgentToolBudgetOutcome.ProposalsExhausted;
    }
}
