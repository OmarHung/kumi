using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 成員能選的主機來源（<see cref="AiConnectionOption.Hosted"/>）：這個組織開著的來源，組織的有效成員（擁有者、管理員、一般成員）都看得到，
/// 不看組織的 AI 開關與「允許成員使用自己的 AI 帳號」；訪客與停用的人一個都看不到。新加入的成員自然就在裡面（每次都查身分模組）。
/// 現在不能用的也列出來，帶一句給成員看的說明（<see cref="AiConnectionOption.Notice"/>）。
/// </summary>
internal sealed class HostSourceOptions(
    IDbContextFactory<AiDbContext> contexts, HostPeople people, HostUsageReader usage, TimeProvider time)
{
    /// <summary>組織有開著的來源、而且這個人是有效成員：助理的對話就能開（和組織的 AI 開關無關）。</summary>
    public async Task<bool> AnyAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (!await people.IsMemberAsync(actor.OrganizationId, actor.PersonId, cancellationToken))
        {
            return false;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.HostSources.AsNoTracking().AnyAsync(s => s.OrganizationId == actor.OrganizationId && s.Enabled, cancellationToken);
    }

    public async Task<IReadOnlyList<AiConnectionOption>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (!await people.IsMemberAsync(actor.OrganizationId, actor.PersonId, cancellationToken))
        {
            return [];
        }

        var org = actor.OrganizationId;
        var month = (await usage.MonthAsync(org, null, cancellationToken))!;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var sources = await db.HostSources.AsNoTracking().Where(s => s.OrganizationId == org && s.Enabled).ToListAsync(cancellationToken);
        if (sources.Count == 0)
        {
            return [];
        }

        var agentIds = sources.Select(s => s.AgentId).Distinct().ToList();
        var agents = await db.HostAgents.AsNoTracking().Where(a => a.OrganizationId == org && agentIds.Contains(a.Id)).ToListAsync(cancellationToken);
        var auths = await HostReadiness.AuthorizationsAsync(db, org, agentIds, cancellationToken);
        var totals = await HostUsageReader.TotalsByAsync(HostUsageReader.Counted(db, org, month), u => u.SourceId, cancellationToken);
        var now = time.GetUtcNow();
        return
        [
            .. sources
                .Select(source => (Source: source, Agent: agents.FirstOrDefault(a => a.Id == source.AgentId)))
                .Where(pair => pair.Agent is not null)
                .Select(pair => OptionOf(HostReadiness.ViewOf(pair.Source, pair.Agent!, auths), totals.GetValueOrDefault(pair.Source.Id), now))
                .Where(option => option is not null)
                .Select(option => option!)
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.Id),
        ];
    }

    private static AiConnectionOption? OptionOf(HostSourceView view, HostUsageTotals? total, DateTimeOffset now)
    {
        var source = view.Source;
        var quotaReached = (source.MonthlyRequests is { } r && (total?.Requests ?? 0) >= r)
            || (source.MonthlyTokens is { } t && (total?.InputTokens ?? 0) + (total?.OutputTokens ?? 0) >= t);
        var (status, _) = HostReadiness.SourceStatus(view, quotaReached, now);
        var models = ModelsOf(view);
        if (status == HostSourceStatus.Ready && models.Count == 0)
        {
            return null;
        }

        return new AiConnectionOption(source.Id, source.Name, AiEnumNames.ParseKind(source.Engine), models)
        {
            Hosted = true,
            Notice = HostReadiness.MemberNotice(status, view.Engine.Readiness),
        };
    }

    /// <summary>開放的 ∩ 引擎回報的，預設的模型排第一；預設的思考深度是擁有者設的，沒設用引擎自己回報的。</summary>
    public static IReadOnlyList<AiModelOption> ModelsOf(HostSourceView view)
    {
        var enabled = HostEnginesJson.DeserializeNames(view.Source.EnabledModels);
        var defaultEffort = AiEnumNames.ParseOptionalEffort(view.Source.DefaultEffort);
        return
        [
            .. view.Engine.Models
                .Where(m => enabled.Contains(m.Id))
                .OrderBy(m => m.Id == view.Source.DefaultModel ? 0 : 1)
                .Select(m => m.Id == view.Source.DefaultModel && defaultEffort is { } effort && m.Efforts.Contains(effort)
                    ? new AiModelOption(m.Id, m.DisplayName, m.Efforts) { DefaultEffort = effort }
                    : m),
        ];
    }
}
