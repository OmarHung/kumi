using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Work.Persistence;

namespace PmTool.Work.Tests;

/// <summary>
/// 封存表沒有公開的讀取介面（之後的還原才會用到），所以測試直接讀它：每一筆是哪個項目、哪個欄位、什麼值、從哪裡來。
/// </summary>
internal static class ArchivedValues
{
    /// <summary>這個組織裡套用變更集時封存的值，依項目編號與欄位排序；值寫成不帶空白的 JSON。</summary>
    public static async Task<(long ItemNumber, string Field, string Value, long ChangeSetNumber)[]> ListAsync(WorkTestHost host, Scenario scenario)
    {
        await using var db = await host.Root.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();
        var rows = await db.ArchivedFieldValues
            .Where(a => a.OrganizationId == scenario.OrganizationId)
            .Join(db.Items, a => a.ItemId, i => i.Id, (a, i) => new { a, i.Number })
            .Join(db.ChangeSets, x => x.a.ChangeSetId, c => (Guid?)c.Id, (x, c) => new { x.Number, x.a.Field, x.a.Value, ChangeSet = c.Number })
            .ToListAsync();
        return [.. rows
            .Select(r => (r.Number, r.Field, Compact(r.Value), r.ChangeSet))
            .OrderBy(r => r.Number)
            .ThenBy(r => r.Field, StringComparer.Ordinal)];
    }

    /// <summary>
    /// 這個組織裡改類型時封存的值，依項目編號與欄位排序；帶著封存它的那一筆活動的編號與種類，
    /// 和公開介面讀到的活動對得起來。
    /// </summary>
    public static async Task<(long ItemNumber, string Field, string Value, Guid ActivityId, string ActivityKind)[]> ByActivityAsync(
        WorkTestHost host, Scenario scenario)
    {
        await using var db = await host.Root.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();
        var rows = await db.ArchivedFieldValues
            .Where(a => a.OrganizationId == scenario.OrganizationId && a.ChangeSetId == null)
            .Join(db.Items, a => a.ItemId, i => i.Id, (a, i) => new { a, i.Number })
            .Join(db.ItemActivities, x => x.a.ItemActivityId, t => (Guid?)t.Id, (x, t) => new { x.Number, x.a.Field, x.a.Value, t.Id, t.Kind })
            .ToListAsync();
        return [.. rows
            .Select(r => (r.Number, r.Field, Compact(r.Value), r.Id, r.Kind))
            .OrderBy(r => r.Number)
            .ThenBy(r => r.Field, StringComparer.Ordinal)];
    }

    private static string Compact(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ActivityLog.Text(document.RootElement)!;
    }
}
