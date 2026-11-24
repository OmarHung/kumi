using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Settings;

/// <summary>資料列轉成對外的型別，以及這個月（組織的時區）的用量。</summary>
internal sealed class AiConnectionMapper(IOrganizationClock organizationClock, TimeProvider clock)
{
    public async Task<UsageWindow> ReadUsageAsync(AiDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var zone = await organizationClock.TimeZoneAsync(organizationId, cancellationToken);
        var today = OrganizationTime.Today(zone, clock.GetUtcNow());
        var start = LocalMidnight(zone, new DateTime(today.Year, today.Month, 1));
        var end = LocalMidnight(zone, new DateTime(today.Year, today.Month, 1).AddMonths(1));
        var totals = await db.Messages
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.Role == "assistant" && m.ConnectionId != null
                && m.CreatedAt >= start && m.CreatedAt < end)
            .GroupBy(m => m.ConnectionId)
            .Select(g => new { Id = g.Key, Input = g.Sum(m => m.InputTokens ?? 0), Output = g.Sum(m => m.OutputTokens ?? 0), Replies = g.Count() })
            .ToListAsync(cancellationToken);
        return new UsageWindow(
            $"{today.Year:D4}-{today.Month:D2}",
            totals.ToDictionary(t => t.Id!.Value, t => (t.Input, t.Output, t.Replies)));
    }

    public static AiConnection ToConnection(AiConnectionRow row, UsageWindow usage)
    {
        var (input, output, replies) = usage.Totals.GetValueOrDefault(row.Id);
        return new AiConnection(
            row.Id,
            row.Name,
            AiEnumNames.ParseKind(row.Kind),
            row.BaseUrl,
            row.ProtectedApiKey is not null,
            row.ApiKeyHint,
            AiModelsJson.Deserialize(row.Models),
            row.ModelsCheckedAt,
            new AiUsage(usage.Month, input, output, replies),
            row.CreatedAt,
            row.UpdatedAt,
            row.UpdatedBy);
    }

    private static DateTimeOffset LocalMidnight(TimeZoneInfo zone, DateTime local) =>
        new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
}

/// <summary>這個月的用量，依連線編號加總。</summary>
internal sealed record UsageWindow(string Month, IReadOnlyDictionary<Guid, (long Input, long Output, int Replies)> Totals);
