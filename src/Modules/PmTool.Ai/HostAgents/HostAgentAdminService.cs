using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 擁有者的主機代理管理（internal/HANDOFF-host-agent.md 第 6.3 節）。這個檔案是總覽與代理本身；登入、來源、用量在其他檔案。
/// 每個方法開頭向身分模組重新確認「現在」是擁有者，不看 <see cref="Actor.IsOrganizationAdmin"/>。
/// 影響工作的動作（停用、重新配對、移除、登出）在同一個交易裡拿來源的 advisory lock、取消相關的工作。
/// </summary>
internal sealed partial class HostAgentAdminService(
    IDbContextFactory<AiDbContext> contexts,
    HostPeople people,
    HostUsageReader usage,
    HostAgentSignals signals,
    TimeProvider time) : IHostAgentAdminService
{
    private const string DisabledMessage = "主機代理被組織擁有者停用了，這則沒有執行。";

    private const string RepairedMessage = "主機代理被重新配對了，這則沒有執行。";

    private const string RemovedMessage = "主機代理被組織擁有者移除了，這則沒有執行。";

    public async Task<Result<HostAgentOverview>> GetAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        var org = actor.OrganizationId;
        var month = (await usage.MonthAsync(org, null, cancellationToken))!;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var agents = await db.HostAgents.AsNoTracking().Where(a => a.OrganizationId == org)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        var sources = await db.HostSources.AsNoTracking().Where(s => s.OrganizationId == org)
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToListAsync(cancellationToken);
        var logins = await db.HostLogins.AsNoTracking().Where(l => l.OrganizationId == org).ToListAsync(cancellationToken);
        var auths = await HostReadiness.AuthorizationsAsync(db, org, [.. agents.Select(a => a.Id)], cancellationToken);
        var sourceView = await SourceMapper(db, org, month, cancellationToken);
        return new HostAgentOverview(
            [.. agents.Select(a => HostAgentMapper.ToAgent(a, auths, logins, now))],
            // 兩次讀取之間別人新增的來源（它的代理不在第一次讀到的清單裡）略過，下一次讀就有。
            [.. sources
                .Select(s => (Source: s, Agent: agents.FirstOrDefault(a => a.Id == s.AgentId)))
                .Where(pair => pair.Agent is not null)
                .Select(pair => sourceView(pair.Source, pair.Agent!, auths, now))]);
    }

    public async Task<Result<HostAgentPairing>> CreateAgentAsync(Actor actor, HostAgentInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        var name = HostSourceValidator.CleanName(input.Name);
        if (name is null)
        {
            return HostFailures.Settings(HostFailures.Invalid("name", $"名稱要是 1～{AiRules.MaxNameLength} 個字，不能有控制字元。"));
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var existing = await db.HostAgents.AsNoTracking().Where(a => a.OrganizationId == actor.OrganizationId)
            .Select(a => a.NameKey).ToListAsync(cancellationToken);
        if (existing.Count >= AiRules.MaxHostAgentsPerOrganization)
        {
            return HostFailures.Settings(new FailureDetail(
                AiErrorCodes.TooManyConnections, string.Empty, $"一個組織最多 {AiRules.MaxHostAgentsPerOrganization} 台主機代理。"));
        }

        if (existing.Contains(HostSourceValidator.KeyOf(name)))
        {
            return HostFailures.Settings(HostFailures.Invalid("name", "已經有同名的主機代理。"));
        }

        var now = time.GetUtcNow();
        var code = HostSecrets.NewCode();
        var row = new HostAgentRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            Name = name,
            NameKey = HostSourceValidator.KeyOf(name),
            State = HostAgentStates.AwaitingPairing,
            PairingCodeHash = HostSecrets.HashCode(code),
            PairingExpiresAt = now + AiRules.HostPairingLifetime,
            PairingStartedBy = actor.PersonId,
            CreatedAt = now,
            CreatedBy = actor.PersonId,
            UpdatedAt = now,
            UpdatedBy = actor.PersonId,
        };
        db.HostAgents.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HostAgentPairing(
            HostAgentMapper.ToAgent(row, new Dictionary<(Guid, string), HostAuthorization>(), [], now), code, row.PairingExpiresAt!.Value);
    }

    public async Task<Result<HostAgent>> UpdateAgentAsync(
        Actor actor, Guid agentId, HostAgentInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        var name = HostSourceValidator.CleanName(input.Name);
        if (name is null)
        {
            return HostFailures.Settings(HostFailures.Invalid("name", $"名稱要是 1～{AiRules.MaxNameLength} 個字，不能有控制字元。"));
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var row = await FindAgentAsync(db, actor, agentId, cancellationToken);
        if (row is null)
        {
            return HostFailures.AgentNotFound();
        }

        var key = HostSourceValidator.KeyOf(name);
        if (await db.HostAgents.AnyAsync(a => a.OrganizationId == actor.OrganizationId && a.NameKey == key && a.Id != agentId, cancellationToken))
        {
            return HostFailures.Settings(HostFailures.Invalid("name", "已經有同名的主機代理。"));
        }

        // 還沒配對的代理沒有機器憑證可以「啟用」：維持等配對，只改名字。
        var state = row.State == HostAgentStates.AwaitingPairing
            ? row.State
            : input.Enabled ? HostAgentStates.Active : HostAgentStates.Disabled;
        var now = time.GetUtcNow();
        if (state == HostAgentStates.Disabled && row.State != HostAgentStates.Disabled)
        {
            await CancelAgentJobsAsync(db, row, DisabledMessage, now, cancellationToken);
            await CancelOpenLoginsAsync(db, agentId, cancellationToken);
        }

        var updated = row with { Name = name, NameKey = key, State = state, UpdatedAt = now, UpdatedBy = actor.PersonId };
        await db.HostAgents.Where(a => a.Id == agentId).ExecuteUpdateAsync(
            set => set
                .SetProperty(a => a.Name, updated.Name)
                .SetProperty(a => a.NameKey, updated.NameKey)
                .SetProperty(a => a.State, updated.State)
                .SetProperty(a => a.UpdatedAt, now)
                .SetProperty(a => a.UpdatedBy, actor.PersonId),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Signal(agentId);
        return await ReadAgentAsync(updated, cancellationToken);
    }

    public async Task<Result<HostAgentPairing>> ResetPairingAsync(Actor actor, Guid agentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var row = await FindAgentAsync(db, actor, agentId, cancellationToken);
        if (row is null)
        {
            return HostFailures.AgentNotFound();
        }

        var now = time.GetUtcNow();
        await CancelAgentJobsAsync(db, row, RepairedMessage, now, cancellationToken);
        await CancelOpenLoginsAsync(db, agentId, cancellationToken);
        var code = HostSecrets.NewCode();
        var expires = now + AiRules.HostPairingLifetime;
        var hash = HostSecrets.HashCode(code);
        await db.HostAgents.Where(a => a.Id == agentId).ExecuteUpdateAsync(
            set => set
                .SetProperty(a => a.State, HostAgentStates.AwaitingPairing)
                .SetProperty(a => a.CredentialHash, (string?)null)
                .SetProperty(a => a.PairingCodeHash, hash)
                .SetProperty(a => a.PairingExpiresAt, expires)
                .SetProperty(a => a.PairingStartedBy, actor.PersonId)
                .SetProperty(a => a.UpdatedAt, now)
                .SetProperty(a => a.UpdatedBy, actor.PersonId),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var updated = row with { State = HostAgentStates.AwaitingPairing, PairingExpiresAt = expires, CredentialHash = null };
        return new HostAgentPairing((await ReadAgentAsync(updated, cancellationToken)).Value, code, expires);
    }

    public async Task<Result<Unit>> RemoveAgentAsync(Actor actor, Guid agentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var row = await FindAgentAsync(db, actor, agentId, cancellationToken);
        if (row is null)
        {
            return HostFailures.AgentNotFound();
        }

        await CancelAgentJobsAsync(db, row, RemovedMessage, time.GetUtcNow(), cancellationToken);
        await db.HostSources.Where(s => s.AgentId == agentId).ExecuteDeleteAsync(cancellationToken);
        await db.HostLogins.Where(l => l.AgentId == agentId).ExecuteDeleteAsync(cancellationToken);
        await db.HostAgents.Where(a => a.Id == agentId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Signal(agentId);
        return Unit.Value;
    }

    private async Task<Failure?> GuardAsync(Actor actor, CancellationToken cancellationToken) =>
        await people.IsOwnerAsync(actor.OrganizationId, actor.PersonId, cancellationToken) ? null : HostFailures.OwnerOnly();

    private static Task<HostAgentRow?> FindAgentAsync(AiDbContext db, Actor actor, Guid agentId, CancellationToken cancellationToken) =>
        db.HostAgents.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == agentId && a.OrganizationId == actor.OrganizationId, cancellationToken);

    /// <summary>代理的所有來源的工作都取消（先拿所有來源的鎖，依編號排序）。</summary>
    private static async Task CancelAgentJobsAsync(
        AiDbContext db, HostAgentRow agent, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sourceIds = await db.HostSources.AsNoTracking().Where(s => s.AgentId == agent.Id).Select(s => s.Id).ToListAsync(cancellationToken);
        await HostJobFinisher.LockSourcesAsync(db, sourceIds, cancellationToken);
        await HostJobFinisher.CancelAsync(db, db.HostJobs.Where(j => j.AgentId == agent.Id), message, now, cancellationToken);
    }

    /// <summary>還沒完成的登入作廢：代理停用或換了機器憑證之後，之前發的登入碼不該還能在主機上完成。</summary>
    private static Task CancelOpenLoginsAsync(AiDbContext db, Guid agentId, CancellationToken cancellationToken) =>
        db.HostLogins
            .Where(l => l.AgentId == agentId && (l.State == HostLoginStates.Pending || l.State == HostLoginStates.Claimed))
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.State, HostLoginStates.Cancelled), cancellationToken);

    private async Task<Result<HostAgent>> ReadAgentAsync(HostAgentRow row, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var auths = await HostReadiness.AuthorizationsAsync(db, row.OrganizationId, [row.Id], cancellationToken);
        var logins = await db.HostLogins.AsNoTracking().Where(l => l.AgentId == row.Id).ToListAsync(cancellationToken);
        var fresh = await db.HostAgents.AsNoTracking().SingleAsync(a => a.Id == row.Id, cancellationToken);
        return HostAgentMapper.ToAgent(fresh, auths, logins, time.GetUtcNow());
    }
}
