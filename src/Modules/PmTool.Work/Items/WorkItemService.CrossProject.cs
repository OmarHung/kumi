using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

// 跨專案的清單與類型。放在另一個檔案：和單一專案的清單共用條件的檢查與篩選，但範圍與排序是另一套。
internal sealed partial class WorkItemService
{
    public async Task<Result<WorkItemPage>> ListAcrossProjectsAsync(
        Actor actor, CrossProjectScope scope, WorkItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        var keys = CrossProjectScopes.NormalizeKeys(scope);
        if (keys.Failure is { } badScope)
        {
            return badScope;
        }

        var plan = ItemQueryPlan.From(query);
        if (plan.Failure is { } invalid)
        {
            return invalid;
        }

        if (FieldConditionProblem(query) is { } fieldProblem)
        {
            return fieldProblem;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var projects = await CrossProjectScopes.ReadableAsync(
            db, cache, actor, keys.Value, restricted: scope.ProjectKeys.Count > 0, cancellationToken);
        if (projects.Count == 0)
        {
            return new WorkItemPage([], null);
        }

        var projectIds = projects.Select(p => p.Id).ToArray();
        var inProjects = ItemStore.Live(db).AsNoTracking().Where(i => i.OrganizationId == actor.OrganizationId && projectIds.Contains(i.ProjectId));

        // 上層的篩選要在完整的項目裡找（看不到的上層底下，看得到的下層照樣列出來）；看得到哪些放在最後。
        var filtered = ItemScope.Across(
            CrossProjectQuery.Filter(inProjects, plan.Value, projects),
            [.. projects.Where(p => !p.OwnOnly).Select(p => p.Id)],
            [.. projects.Where(p => p.OwnOnly).Select(p => p.Id)],
            actor.PersonId);

        // 多拿一筆，看得出還有沒有下一頁，不必另外算總數。
        var rows = await CrossProjectQuery.Sort(CrossProjectQuery.WithProjectOrder(db, filtered), query)
            .Skip(plan.Value.Offset)
            .Take(query.Limit + 1)
            .Select(r => r.Item)
            .Select(ItemReader.WithoutDescription)
            .ToListAsync(cancellationToken);
        var page = rows.Take(query.Limit).ToArray();
        var next = rows.Count > query.Limit ? ItemCursor.Encode(plan.Value.Offset + query.Limit) : null;
        return new WorkItemPage(await ItemReader.ToWorkItemsAsync(db, actor.OrganizationId, page, cancellationToken), next);
    }

    // 各專案的自訂欄位不同，跨專案沒有共同的欄位可以篩、可以排；不擋的話會被當成沒有條件，結果比使用者要的多。
    private static Failure? FieldConditionProblem(WorkItemQuery query)
    {
        if (query.FieldFilters is [var first, ..])
        {
            return WorkFailures.QueryInvalid($"field.{first.Key}", "跨專案的清單不能用自訂欄位篩選：各專案的欄位不同。");
        }

        return query.SortBy == WorkItemSortField.Field
            ? WorkFailures.QueryInvalid(QueryPaths.Sort, "跨專案的清單不能依自訂欄位排序：各專案的欄位不同。")
            : null;
    }

    public async Task<IReadOnlyList<ItemTypeSummary>> ListItemTypesAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var projects = await CrossProjectScopes.ReadableAsync(db, cache, actor, [], restricted: false, cancellationToken);

        // 專案已經照代號排好，名稱與代號就照這個順序收；藍圖從快取讀，不重新解析。
        var merged = new SortedDictionary<string, (List<string> Names, List<string> ProjectKeys)>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            var schema = await cache.GetAsync(db, actor.OrganizationId, project.Id, project.BlueprintVersion, cancellationToken);
            foreach (var type in schema.Blueprint.ItemTypes)
            {
                if (!merged.TryGetValue(type.Key, out var entry))
                {
                    entry = ([], []);
                    merged[type.Key] = entry;
                }

                if (!entry.Names.Contains(type.Name))
                {
                    entry.Names.Add(type.Name);
                }

                entry.ProjectKeys.Add(project.Key);
            }
        }

        return [.. merged.Select(pair => new ItemTypeSummary(pair.Key, pair.Value.Names, pair.Value.ProjectKeys))];
    }
}
