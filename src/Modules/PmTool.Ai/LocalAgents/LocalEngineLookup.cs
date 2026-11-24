using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.LocalAgents;

internal sealed record LocalEngine(LocalEngineRow Engine, LocalAgentRow Agent);

internal static class LocalEngineLookup
{
    /// <summary>
    /// 這個人自己的引擎。別人的、不存在的都是 null：組織、人兩個條件都在查詢裡，
    /// 不是查出來再比對，別人的引擎編號猜中了也看不出來存不存在。
    /// </summary>
    public static async Task<LocalEngine?> FindAsync(AiDbContext db, Actor actor, Guid engineId, CancellationToken cancellationToken)
    {
        var found = await (
            from engine in db.LocalEngines.AsNoTracking()
            join agent in db.LocalAgents.AsNoTracking() on engine.AgentId equals agent.Id
            where engine.Id == engineId && engine.OrganizationId == actor.OrganizationId
                && agent.OrganizationId == actor.OrganizationId && agent.PersonId == actor.PersonId
            select new LocalEngine(engine, agent)).SingleOrDefaultAsync(cancellationToken);
        return found;
    }

    /// <summary>能選清單裡的名稱：「Claude Code（電腦名稱）」。</summary>
    public static string NameOf(AiProviderKind kind, string machineName) =>
        $"{(kind == AiProviderKind.ClaudeCode ? "Claude Code" : "Codex")}（{machineName}）";
}
