using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Work.Persistence;

namespace PmTool.Work.Tests;

/// <summary>刪除（封存）的項目留在資料表裡的樣子：誰在什麼時候刪的、停在哪個狀態、有哪些活動。</summary>
internal sealed record StoredItem(DateTimeOffset? DeletedAt, Guid? DeletedBy, string State, IReadOnlyList<string> ActivityKinds);

/// <summary>
/// 刪除的項目在公開介面上讀不到（之後的復原才會用到），所以測試直接讀資料表，確認資料與活動都還留著、
/// 變更集也沒有動到它。
/// </summary>
internal static class DeletedItems
{
    public static async Task<StoredItem> ReadAsync(WorkTestHost host, Scenario scenario, string itemKey)
    {
        var number = long.Parse(itemKey[(itemKey.IndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        var projectKey = itemKey[..itemKey.IndexOf('-')];
        await using var db = await host.Root.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();
        var item = await db.Items
            .Join(db.Projects, i => i.ProjectId, p => p.Id, (i, p) => new { Item = i, p.Key })
            .Where(x => x.Item.OrganizationId == scenario.OrganizationId && x.Key == projectKey && x.Item.Number == number)
            .Select(x => x.Item)
            .SingleAsync();
        var kinds = await db.ItemActivities
            .Where(a => a.OrganizationId == scenario.OrganizationId && a.ItemId == item.Id)
            .OrderBy(a => a.At)
            .ThenBy(a => a.Id)
            .Select(a => a.Kind)
            .ToListAsync();
        return new StoredItem(item.DeletedAt, item.DeletedBy, item.State, kinds);
    }
}
