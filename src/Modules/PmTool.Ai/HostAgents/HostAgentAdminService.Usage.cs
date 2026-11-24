using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Ai.HostAgents;

internal sealed partial class HostAgentAdminService
{
    public async Task<Result<HostSourceUsage>> GetUsageAsync(Actor actor, Guid sourceId, string? month, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await GuardAsync(actor, cancellationToken) is { } denied)
        {
            return denied;
        }

        var window = await usage.MonthAsync(actor.OrganizationId, month, cancellationToken);
        if (window is null)
        {
            return HostFailures.Settings(HostFailures.Invalid("month", "月份的格式要是 yyyy-MM。"));
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await db.HostSources.AnyAsync(s => s.Id == sourceId && s.OrganizationId == actor.OrganizationId, cancellationToken))
        {
            return HostFailures.SourceNotFound();
        }

        // 刪掉來源之後用量還在，但這裡只回現存來源的；只有則數、狀態與 token，沒有對話的內容與標題。
        var rows = HostUsageReader.Counted(db, actor.OrganizationId, window).Where(u => u.SourceId == sourceId);
        var total = await HostUsageReader.TotalsAsync(rows, cancellationToken);
        var byPerson = await HostUsageReader.TotalsByAsync(rows, u => u.PersonId, cancellationToken);
        var byProject = await HostUsageReader.TotalsByAsync(rows, u => u.ProjectKey, cancellationToken);
        return new HostSourceUsage(
            window.Label,
            total,
            [.. byPerson.OrderByDescending(p => p.Value.Requests).ThenBy(p => p.Key).Select(p => new HostPersonUsage(p.Key, p.Value))],
            [.. byProject.OrderByDescending(p => p.Value.Requests).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => new HostProjectUsage(p.Key, p.Value))]);
    }
}
