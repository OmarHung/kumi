using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

internal sealed partial class HostAgentAdminService
{
    private const string LoggedOutMessage = "主機上的登入被組織擁有者登出了，這則沒有執行。";

    public async Task<Result<HostLoginStart>> StartLoginAsync(
        Actor actor, Guid agentId, AiProviderKind engine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (!HostEnums.IsEngine(engine))
        {
            return HostFailures.Settings(HostFailures.Invalid("engine", "引擎只能是 Claude Code 或 Codex。"));
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var agent = await FindAgentAsync(db, actor, agentId, cancellationToken);
        if (agent is null)
        {
            return HostFailures.AgentNotFound();
        }

        if (agent.State != HostAgentStates.Active)
        {
            return HostFailures.AgentUnavailable();
        }

        // 同一個代理的同一個引擎同時只有一個進行中的登入：新的開始時舊的作廢。
        var name = AiEnumNames.NameOf(engine);
        await db.HostLogins
            .Where(l => l.AgentId == agentId && l.Engine == name && (l.State == HostLoginStates.Pending || l.State == HostLoginStates.Claimed))
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.State, HostLoginStates.Cancelled), cancellationToken);

        var now = time.GetUtcNow();
        var code = HostSecrets.NewCode();
        var row = new HostLoginRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            AgentId = agentId,
            Engine = name,
            CodeHash = HostSecrets.HashCode(code)!,
            State = HostLoginStates.Pending,
            StartedBy = actor.PersonId,
            ExpiresAt = now + AiRules.HostLoginLifetime,
            CreatedAt = now,
        };
        db.HostLogins.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HostLoginStart(HostAgentMapper.ToLogin(row), code);
    }

    public async Task<Result<Unit>> CancelLoginAsync(Actor actor, Guid loginId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await db.HostLogins.AnyAsync(l => l.Id == loginId && l.OrganizationId == actor.OrganizationId, cancellationToken))
        {
            return HostFailures.LoginNotFound();
        }

        // 已經完成或作廢的照樣成功、什麼都不改。
        await db.HostLogins
            .Where(l => l.Id == loginId && (l.State == HostLoginStates.Pending || l.State == HostLoginStates.Claimed))
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.State, HostLoginStates.Cancelled), cancellationToken);
        return Unit.Value;
    }

    public async Task<Result<Unit>> LogoutAsync(Actor actor, Guid agentId, AiProviderKind engine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (!HostEnums.IsEngine(engine))
        {
            return HostFailures.Settings(HostFailures.Invalid("engine", "引擎只能是 Claude Code 或 Codex。"));
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var agent = await FindAgentAsync(db, actor, agentId, cancellationToken);
        if (agent is null)
        {
            return HostFailures.AgentNotFound();
        }

        var name = AiEnumNames.NameOf(engine);
        var sourceIds = await db.HostSources.AsNoTracking()
            .Where(s => s.AgentId == agentId && s.Engine == name).Select(s => s.Id).ToListAsync(cancellationToken);
        await HostJobFinisher.LockSourcesAsync(db, sourceIds, cancellationToken);
        await HostJobFinisher.CancelAsync(
            db, db.HostJobs.Where(j => sourceIds.Contains(j.SourceId)), LoggedOutMessage, time.GetUtcNow(), cancellationToken);

        // 目前的授權作廢；worker 下一次登記時收到登出、執行 CLI 自己的登出。
        await db.HostLogins
            .Where(l => l.AgentId == agentId && l.Engine == name && l.State == HostLoginStates.Completed)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.State, HostLoginStates.Cancelled), cancellationToken);
        var requested = HostEnginesJson.DeserializeNames(agent.LogoutRequested);
        if (!requested.Contains(name))
        {
            var updated = HostEnginesJson.SerializeNames([.. requested, name]);
            await db.HostAgents.Where(a => a.Id == agentId)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.LogoutRequested, updated), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        signals.Signal(agentId);
        return Unit.Value;
    }
}
