using System.Linq.Expressions;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>一段時間（組織時區的某一天或某個月）。<paramref name="End"/> 不含。</summary>
internal sealed record HostWindow(string Label, DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// 用量與配額的算法：「今天」「這個月」一律依組織的時區算。只算沒退還的（<see cref="HostUsageStates.Released"/> 不算）。
/// </summary>
internal sealed class HostUsageReader(IOrganizationClock organizationClock, TimeProvider time)
{
    /// <summary>某個月（<c>yyyy-MM</c>；null 是這個月）的範圍；格式不對是 null。</summary>
    public async Task<HostWindow?> MonthAsync(Guid organizationId, string? month, CancellationToken cancellationToken)
    {
        var zone = await organizationClock.TimeZoneAsync(organizationId, cancellationToken);
        DateOnly first;
        if (month is null)
        {
            var today = OrganizationTime.Today(zone, time.GetUtcNow());
            first = new DateOnly(today.Year, today.Month, 1);
        }
        else if (!TryParseMonth(month, out first))
        {
            return null;
        }

        return new HostWindow(
            $"{first.Year:D4}-{first.Month:D2}", LocalMidnight(zone, first), LocalMidnight(zone, first.AddMonths(1)));
    }

    public async Task<HostWindow> DayAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var zone = await organizationClock.TimeZoneAsync(organizationId, cancellationToken);
        var today = OrganizationTime.Today(zone, time.GetUtcNow());
        return new HostWindow(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), LocalMidnight(zone, today), LocalMidnight(zone, today.AddDays(1)));
    }

    public static IQueryable<HostUsageRow> Counted(AiDbContext db, Guid organizationId, HostWindow window) =>
        db.HostUsage.AsNoTracking().Where(u =>
            u.OrganizationId == organizationId && u.State != HostUsageStates.Released && u.CreatedAt >= window.Start && u.CreatedAt < window.End);

    public static async Task<HostUsageTotals> TotalsAsync(IQueryable<HostUsageRow> rows, CancellationToken cancellationToken) =>
        (await TotalsByAsync(rows, _ => 0, cancellationToken)).GetValueOrDefault(0) ?? new HostUsageTotals(0, 0, 0, 0, 0, 0);

    public static async Task<Dictionary<TKey, HostUsageTotals>> TotalsByAsync<TKey>(
        IQueryable<HostUsageRow> rows, Expression<Func<HostUsageRow, TKey>> key, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var complete = AssistantMessageMapper.StatusName(AssistantMessageStatus.Complete);
        var failed = AssistantMessageMapper.StatusName(AssistantMessageStatus.Failed);
        var stopped = AssistantMessageMapper.StatusName(AssistantMessageStatus.Stopped);
        var grouped = await rows.GroupBy(key)
            .Select(g => new
            {
                g.Key,
                Requests = g.Count(),
                Completed = g.Count(u => u.FinishStatus == complete),
                Failed = g.Count(u => u.FinishStatus == failed),
                Stopped = g.Count(u => u.FinishStatus == stopped),
                Input = g.Sum(u => u.InputTokens),
                Output = g.Sum(u => u.OutputTokens),
            })
            .ToListAsync(cancellationToken);
        return grouped.ToDictionary(
            g => g.Key,
            g => new HostUsageTotals(g.Requests, g.Completed, g.Failed, g.Stopped, g.Input, g.Output));
    }

    private static bool TryParseMonth(string month, out DateOnly first)
    {
        first = default;
        return month.Length == 7
            && month[4] == '-'
            && DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first);
    }

    private static DateTimeOffset LocalMidnight(TimeZoneInfo zone, DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
