using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 「我的帳號」的電腦清單。只看自己的：別人的、不存在的一律「找不到」，不透露存不存在。
/// 組織不允許時照樣列得出來、移除得掉（和自己的連線一樣，關掉之後使用者還是能清掉自己留下的）。
/// </summary>
internal sealed class LocalAgentDirectory(IDbContextFactory<AiDbContext> contexts, TimeProvider time) : ILocalAgentDirectory
{
    public async Task<IReadOnlyList<LocalAgentMachine>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var agents = await db.LocalAgents.AsNoTracking()
            .Where(a => a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId)
            .OrderByDescending(a => a.LastSeenAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
        var ids = agents.Select(a => a.Id).ToList();
        var engines = await db.LocalEngines.AsNoTracking()
            .Where(e => e.OrganizationId == actor.OrganizationId && ids.Contains(e.AgentId))
            .ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        return
        [
            .. agents.Select(agent => new LocalAgentMachine(
                agent.Id,
                agent.Name,
                agent.AgentVersion,
                [.. engines.Where(e => e.AgentId == agent.Id)
                    .OrderBy(e => e.Kind, StringComparer.Ordinal)
                    .Select(e => new LocalAgentEngineSummary(AiEnumNames.ParseKind(e.Kind), e.Version))],
                IsOnline(agent.LastSeenAt, now),
                agent.LastSeenAt)),
        ];
    }

    public async Task<Result<Unit>> RemoveAsync(Actor actor, Guid machineId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 引擎由外鍵連帶刪除。還在等的工作沒有人領得到，回覆那一頭會因為沒被領走而失敗。
        var deleted = await db.LocalAgents
            .Where(a => a.Id == machineId && a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? LocalAgentFailures.MachineNotFound() : Unit.Value;
    }

    /// <summary>最近 <see cref="AiRules.LocalAgentOnlineWindow"/> 內登記過。</summary>
    public static bool IsOnline(DateTimeOffset lastSeenAt, DateTimeOffset now) => now - lastSeenAt <= AiRules.LocalAgentOnlineWindow;
}
