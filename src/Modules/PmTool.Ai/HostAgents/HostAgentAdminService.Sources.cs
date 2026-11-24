using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

internal sealed partial class HostAgentAdminService
{
    private const string SourceClosedMessage = "主機上的來源被組織擁有者關掉了，這則沒有執行。";

    public async Task<Result<HostSource>> CreateSourceAsync(Actor actor, HostSourceInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (!HostEnums.IsEngine(input.Engine))
        {
            return HostFailures.Settings(HostFailures.Invalid("engine", "引擎只能是 Claude Code 或 Codex。"));
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var agent = await FindAgentAsync(db, actor, input.AgentId, cancellationToken);
        if (agent is null)
        {
            return HostFailures.AgentNotFound();
        }

        var engine = AiEnumNames.NameOf(input.Engine);
        if (await db.HostSources.AnyAsync(s => s.AgentId == agent.Id && s.Engine == engine, cancellationToken))
        {
            return HostFailures.Settings(HostFailures.Invalid("engine", "這台代理的這個引擎已經有來源了。"));
        }

        var checkedInput = await CheckAsync(db, actor, agent, input, existing: null, cancellationToken);
        if (checkedInput.Failure is { } invalid)
        {
            return invalid;
        }

        var now = time.GetUtcNow();
        var row = ToRow(checkedInput.Value, input, agent.Id, actor, now, existing: null);
        db.HostSources.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadSourceAsync(row.Id, actor.OrganizationId, cancellationToken);
    }

    public async Task<Result<HostSource>> UpdateSourceAsync(
        Actor actor, Guid sourceId, HostSourceInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var existing = await db.HostSources.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sourceId && s.OrganizationId == actor.OrganizationId, cancellationToken);
        if (existing is null)
        {
            return HostFailures.SourceNotFound();
        }

        var immutable = new List<FailureDetail>();
        if (input.AgentId != existing.AgentId)
        {
            immutable.Add(HostFailures.Invalid("agentId", "來源所屬的代理不能改。"));
        }

        if (AiEnumNames.NameOf(input.Engine) != existing.Engine)
        {
            immutable.Add(HostFailures.Invalid("engine", "來源的引擎不能改。"));
        }

        if (immutable.Count > 0)
        {
            return HostFailures.Settings([.. immutable]);
        }

        var agent = await db.HostAgents.AsNoTracking().SingleAsync(a => a.Id == existing.AgentId, cancellationToken);
        var checkedInput = await CheckAsync(db, actor, agent, input, existing, cancellationToken);
        if (checkedInput.Failure is { } invalid)
        {
            return invalid;
        }

        var now = time.GetUtcNow();
        if (existing.Enabled && !input.Enabled)
        {
            await HostJobFinisher.LockSourceAsync(db, sourceId, cancellationToken);
            await HostJobFinisher.CancelAsync(db, db.HostJobs.Where(j => j.SourceId == sourceId), SourceClosedMessage, now, cancellationToken);
        }

        var row = ToRow(checkedInput.Value, input, agent.Id, actor, now, existing);
        await db.HostSources.Where(s => s.Id == sourceId).ExecuteUpdateAsync(
            set => set
                .SetProperty(s => s.Name, row.Name)
                .SetProperty(s => s.NameKey, row.NameKey)
                .SetProperty(s => s.Enabled, row.Enabled)
                .SetProperty(s => s.EnabledModels, row.EnabledModels)
                .SetProperty(s => s.DefaultModel, row.DefaultModel)
                .SetProperty(s => s.DefaultEffort, row.DefaultEffort)
                .SetProperty(s => s.DailyRequestsPerPerson, row.DailyRequestsPerPerson)
                .SetProperty(s => s.MonthlyRequests, row.MonthlyRequests)
                .SetProperty(s => s.MonthlyTokens, row.MonthlyTokens)
                .SetProperty(s => s.BillingAcknowledgedAt, row.BillingAcknowledgedAt)
                .SetProperty(s => s.BillingAcknowledgedBy, row.BillingAcknowledgedBy)
                .SetProperty(s => s.UpdatedAt, now)
                .SetProperty(s => s.UpdatedBy, actor.PersonId),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Signal(agent.Id);
        return await ReadSourceAsync(sourceId, actor.OrganizationId, cancellationToken);
    }

    public async Task<Result<Unit>> DeleteSourceAsync(Actor actor, Guid sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockOrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var existing = await db.HostSources.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sourceId && s.OrganizationId == actor.OrganizationId, cancellationToken);
        if (existing is null)
        {
            return HostFailures.SourceNotFound();
        }

        await HostJobFinisher.LockSourceAsync(db, sourceId, cancellationToken);
        await HostJobFinisher.CancelAsync(
            db, db.HostJobs.Where(j => j.SourceId == sourceId), SourceClosedMessage, time.GetUtcNow(), cancellationToken);
        await db.HostSources.Where(s => s.Id == sourceId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Signal(existing.AgentId);
        return Unit.Value;
    }

    /// <summary>欄位檢查加上開著時的條件。順序：計費不能共用、還沒登入、沒勾計費確認。</summary>
    private static async Task<Result<ValidatedSource>> CheckAsync(
        AiDbContext db, Actor actor, HostAgentRow agent, HostSourceInput input, HostSourceRow? existing, CancellationToken cancellationToken)
    {
        var kind = input.Engine;
        var report = HostReadiness.ReportOf(agent, kind);
        var validated = HostSourceValidator.Validate(input, report?.Models ?? []);
        if (validated.Failure is { } invalid)
        {
            return invalid;
        }

        var key = validated.Value.NameKey;
        if (await db.HostSources.AnyAsync(
            s => s.OrganizationId == actor.OrganizationId && s.NameKey == key && (existing == null || s.Id != existing.Id), cancellationToken))
        {
            return HostFailures.Settings(HostFailures.Invalid("name", "已經有同名的來源。"));
        }

        return input.Enabled && await CheckEnableAsync(db, actor.OrganizationId, agent, kind, input, report, cancellationToken) is { } blocked
            ? blocked
            : validated;
    }

    private static async Task<Failure?> CheckEnableAsync(
        AiDbContext db, Guid organizationId, HostAgentRow agent, AiProviderKind kind, HostSourceInput input, StoredHostEngine? report,
        CancellationToken cancellationToken)
    {
        if (report is { Installed: true, LoggedIn: true } && report.Billing != HostBilling.Api)
        {
            return HostFailures.BillingNotShareable();
        }

        var auths = await HostReadiness.AuthorizationsAsync(db, organizationId, [agent.Id], cancellationToken);
        if (!auths.ContainsKey((agent.Id, AiEnumNames.NameOf(kind))))
        {
            return HostFailures.Settings(HostFailures.Invalid("enabled", "這個引擎還沒完成登入。"));
        }

        return input.AcknowledgeApiBilling
            ? null
            : HostFailures.Settings(HostFailures.Required("acknowledgeApiBilling", "要確認知道這個來源以 API 用量計費（不是訂閱的額度）。"));
    }

    private static HostSourceRow ToRow(
        ValidatedSource valid, HostSourceInput input, Guid agentId, Actor actor, DateTimeOffset now, HostSourceRow? existing) => new()
    {
        Id = existing?.Id ?? Guid.CreateVersion7(),
        OrganizationId = actor.OrganizationId,
        AgentId = agentId,
        Engine = AiEnumNames.NameOf(input.Engine),
        Name = valid.Name,
        NameKey = valid.NameKey,
        Enabled = input.Enabled,
        EnabledModels = HostEnginesJson.SerializeNames(valid.EnabledModels),
        DefaultModel = valid.DefaultModel,
        DefaultEffort = AiEnumNames.NameOf(valid.DefaultEffort),
        DailyRequestsPerPerson = valid.Limits.DailyRequestsPerPerson,
        MonthlyRequests = valid.Limits.MonthlyRequests,
        MonthlyTokens = valid.Limits.MonthlyTokens,
        BillingAcknowledgedAt = input.AcknowledgeApiBilling ? existing?.BillingAcknowledgedAt ?? now : existing?.BillingAcknowledgedAt,
        BillingAcknowledgedBy = input.AcknowledgeApiBilling ? existing?.BillingAcknowledgedBy ?? actor.PersonId : existing?.BillingAcknowledgedBy,
        CreatedAt = existing?.CreatedAt ?? now,
        CreatedBy = existing?.CreatedBy ?? actor.PersonId,
        UpdatedAt = now,
        UpdatedBy = actor.PersonId,
    };

    private async Task<Result<HostSource>> ReadSourceAsync(Guid sourceId, Guid organizationId, CancellationToken cancellationToken)
    {
        var month = (await usage.MonthAsync(organizationId, null, cancellationToken))!;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var source = await db.HostSources.AsNoTracking().SingleAsync(s => s.Id == sourceId, cancellationToken);
        var agent = await db.HostAgents.AsNoTracking().SingleAsync(a => a.Id == source.AgentId, cancellationToken);
        var auths = await HostReadiness.AuthorizationsAsync(db, organizationId, [agent.Id], cancellationToken);
        var map = await SourceMapper(db, organizationId, month, cancellationToken);
        return map(source, agent, auths, time.GetUtcNow());
    }

    /// <summary>先把一個月的用量與隊伍的件數整批讀好，再一個一個來源組出 <see cref="HostSource"/>。</summary>
    private static async Task<Func<HostSourceRow, HostAgentRow, IReadOnlyDictionary<(Guid AgentId, string Engine), HostAuthorization>, DateTimeOffset, HostSource>>
        SourceMapper(AiDbContext db, Guid organizationId, HostWindow month, CancellationToken cancellationToken)
    {
        var totals = await HostUsageReader.TotalsByAsync(HostUsageReader.Counted(db, organizationId, month), u => u.SourceId, cancellationToken);
        var counts = await db.HostJobs.AsNoTracking()
            .Where(j => j.OrganizationId == organizationId && j.State != HostJobStates.Finished)
            .GroupBy(j => new { j.SourceId, j.State })
            .Select(g => new { g.Key.SourceId, g.Key.State, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return (source, agent, auths, now) =>
        {
            var view = HostReadiness.ViewOf(source, agent, auths);
            var total = totals.GetValueOrDefault(source.Id) ?? new HostUsageTotals(0, 0, 0, 0, 0, 0);
            var limits = new HostSourceLimits(source.DailyRequestsPerPerson, source.MonthlyRequests, source.MonthlyTokens);
            var quotaReached = (limits.MonthlyRequests is { } r && total.Requests >= r)
                || (limits.MonthlyTokens is { } t && total.InputTokens + total.OutputTokens >= t);
            var (status, message) = HostReadiness.SourceStatus(view, quotaReached, now);
            var enabled = HostEnginesJson.DeserializeNames(source.EnabledModels);
            var running = counts.Where(c => c.SourceId == source.Id && c.State == HostJobStates.Running).Sum(c => c.Count);
            var queued = counts.Where(c => c.SourceId == source.Id && c.State != HostJobStates.Running).Sum(c => c.Count);
            return new HostSource(
                source.Id,
                source.AgentId,
                AiEnumNames.ParseKind(source.Engine),
                source.Name,
                source.Enabled,
                status,
                message,
                [.. view.Engine.Models.Select(m => new AiModel(m.Id, m.DisplayName, m.Efforts, enabled.Contains(m.Id), Custom: false))],
                source.DefaultModel,
                AiEnumNames.ParseOptionalEffort(source.DefaultEffort),
                limits,
                running,
                queued,
                total,
                source.BillingAcknowledgedAt,
                source.UpdatedAt,
                source.UpdatedBy);
        };
    }
}
