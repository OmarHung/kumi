using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// <c>host_agents.logout_requested</c>（要登出的引擎名稱，jsonb 陣列）的加入與移除。
/// 擁有者按登出（加入）、worker 登記（移除已經登出的）、登入完成（移除那個引擎）會同時改它，
/// 讀出整份再寫回會蓋掉對方剛寫的：擁有者按了登出卻不見，或已經做完的登出又被寫回來。
/// 所以在資料庫裡逐個元素改，不經過讀到的舊值。
/// </summary>
internal static class HostLogoutRequests
{
    public static async Task AddAsync(AiDbContext db, Guid agentId, string engine, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
             UPDATE ai.host_agents SET logout_requested = logout_requested || jsonb_build_array({engine}::text)
             WHERE id = {agentId} AND NOT logout_requested @> jsonb_build_array({engine}::text)
             """,
            cancellationToken);

    public static async Task RemoveAsync(AiDbContext db, Guid agentId, IReadOnlyCollection<string> engines, CancellationToken cancellationToken)
    {
        if (engines.Count == 0)
        {
            return;
        }

        // jsonb 減 text[]：拿掉陣列裡等於任何一個名稱的元素，沒有的就不變。
        var names = engines.ToArray();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE ai.host_agents SET logout_requested = logout_requested - {names} WHERE id = {agentId}",
            cancellationToken);
    }
}
