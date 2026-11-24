using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Projects;

/// <summary>
/// 專案設定的使用狀況：只回數字，不讀項目的標題與內文。和變更集的資料影響一樣是整個專案的數量，
/// 所以只看自己項目的人不能用（數字會洩漏他看不到的項目有多少）。
/// </summary>
internal sealed class BlueprintUsageService(IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache) : IBlueprintUsageService
{
    public async Task<Result<BlueprintUsage>> DescribeAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.BlueprintRead))
        {
            return access.Missing(WorkPermissions.BlueprintRead);
        }

        if (!access.ReadsAllItems)
        {
            return WorkFailures.NeedsAllItems("查看設定的使用狀況");
        }

        var items = db.Items.AsNoTracking()
            .Where(i => i.OrganizationId == actor.OrganizationId && i.ProjectId == access.Project.Id && i.DeletedAt == null);
        var states = await items
            .GroupBy(i => new { i.Type, i.State })
            .Select(g => new StateUsage(g.Key.Type, g.Key.State, g.LongCount()))
            .ToListAsync(cancellationToken);
        var fields = new List<FieldUsage>();
        foreach (var key in access.Schema.Fields.Keys.Order(StringComparer.Ordinal))
        {
            fields.Add(new FieldUsage(key, await CountFilledAsync(items, key, cancellationToken)));
        }

        return new BlueprintUsage(
            access.Project.CurrentBlueprintVersion,
            states.Sum(s => s.Items),
            [.. states.OrderBy(s => s.ItemType, StringComparer.Ordinal).ThenBy(s => s.State, StringComparer.Ordinal)],
            fields);
    }

    // 值不存在、空字串、空陣列、null 都不算有值。欄位的 key 來自驗證過的藍圖，而且一律當參數傳。
    private static Task<long> CountFilledAsync(IQueryable<ItemEntity> items, string key, CancellationToken cancellationToken) =>
        items.LongCountAsync(
            i => EF.Functions.JsonExists(i.Fields, key)
                && FieldSql.Text(i.Fields, key) != null
                && FieldSql.Text(i.Fields, key) != string.Empty
                && FieldSql.Text(i.Fields, key) != "[]",
            cancellationToken);
}
