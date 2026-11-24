using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>項目和它專案的建立時間：跨專案排序時「專案」依建立的先後，不是依代號。</summary>
internal sealed record RankedItem
{
    public required ItemEntity Item { get; init; }

    public required DateTimeOffset ProjectCreatedAt { get; init; }
}

/// <summary>
/// 跨專案清單的條件與排序。檢查（<see cref="ItemQueryPlan.From"/>）、日期、優先度與週期的篩選和單一專案的清單共用，
/// 這裡只處理「範圍是好幾個專案」才不同的部分：專案的限制、上層與編號搜尋要認專案、排序多一層專案。
/// 所有使用者給的值都經過 LINQ 變成參數，不會拼進 SQL。
/// </summary>
internal static class CrossProjectQuery
{
    /// <param name="items">範圍內（組織、專案、沒刪除）的項目。</param>
    /// <param name="projects">範圍內的專案，用來把項目編號與上層的代號對到專案。</param>
    public static IQueryable<ItemEntity> Filter(IQueryable<ItemEntity> items, ItemQueryPlan plan, IReadOnlyList<ScopedProject> projects)
    {
        var query = plan.Query;
        var filtered = items;
        if (query.Types.Count > 0)
        {
            var types = query.Types.ToArray();
            filtered = filtered.Where(i => types.Contains(i.Type));
        }

        if (query.States.Count > 0)
        {
            var states = query.States.ToArray();
            filtered = filtered.Where(i => states.Contains(i.State));
        }

        if (query.StateCategories.Count > 0)
        {
            var categories = query.StateCategories.Select(StateCategoryText.ToText).ToArray();
            filtered = filtered.Where(i => categories.Contains(i.StateCategory));
        }

        filtered = query.OnlyUnassigned ? filtered.Where(i => i.AssigneeId == null)
            : query.AssigneeId is { } assignee ? filtered.Where(i => i.AssigneeId == assignee)
            : filtered;
        filtered = ItemQueryPlan.FilterByPriorityAndDue(filtered, query);
        filtered = ItemQueryPlan.FilterByPlanning(filtered, query);
        filtered = plan.Parent is null ? filtered : ChildrenOf(filtered, items, plan.Parent, projects);
        return plan.Search is null ? filtered : Searching(filtered, plan.Search, projects);
    }

    /// <summary>上層在範圍外的專案（看不到、封存、不存在）時一定沒有，和單一專案的清單一樣結果是空的。</summary>
    private static IQueryable<ItemEntity> ChildrenOf(
        IQueryable<ItemEntity> filtered, IQueryable<ItemEntity> items, ParentFilter parent, IReadOnlyList<ScopedProject> projects)
    {
        var parentProject = projects.FirstOrDefault(p => p.Key == parent.ProjectKey);
        if (parentProject is null)
        {
            return filtered.Where(_ => false);
        }

        var (projectId, number) = (parentProject.Id, parent.Number);
        return filtered.Where(i => i.ProjectId == projectId
            && items.Any(p => p.ProjectId == projectId && p.Number == number && p.Id == i.ParentId));
    }

    /// <summary>標題包含這段文字（萬用字元當成普通字元），或它就是範圍內某個專案的某個項目編號。</summary>
    private static IQueryable<ItemEntity> Searching(IQueryable<ItemEntity> items, string search, IReadOnlyList<ScopedProject> projects)
    {
        var pattern = "%" + ItemQueryPlan.EscapeLike(search) + "%";
        var keyed = ItemKeys.TryParse(search, out var projectKey, out var number)
            ? projects.FirstOrDefault(p => p.Key == projectKey)
            : null;
        if (keyed is null)
        {
            return items.Where(i => EF.Functions.ILike(i.Title, pattern, ItemQueryPlan.LikeEscape));
        }

        var projectId = keyed.Id;
        return items.Where(i => EF.Functions.ILike(i.Title, pattern, ItemQueryPlan.LikeEscape) || (i.ProjectId == projectId && i.Number == number));
    }

    public static IQueryable<RankedItem> WithProjectOrder(WorkDbContext db, IQueryable<ItemEntity> items) =>
        from item in items
        join project in db.Projects on item.ProjectId equals project.Id
        select new RankedItem { Item = item, ProjectCreatedAt = project.CreatedAt };

    /// <summary>
    /// 排序。同值（與空值）再依專案（建立的先後）、編號，分頁才穩定。
    /// 號碼排序整個一起反過來；狀態依類別的順序再依狀態 key：各專案的流程不同，沒有共同的「流程裡的位置」。
    /// </summary>
    public static IQueryable<RankedItem> Sort(IQueryable<RankedItem> rows, WorkItemQuery query)
    {
        var descending = query.Descending;
        if (query.SortBy == WorkItemSortField.Number)
        {
            return descending
                ? rows.OrderByDescending(r => r.ProjectCreatedAt).ThenByDescending(r => r.Item.ProjectId).ThenByDescending(r => r.Item.Number)
                : rows.OrderBy(r => r.ProjectCreatedAt).ThenBy(r => r.Item.ProjectId).ThenBy(r => r.Item.Number);
        }

        var sorted = query.SortBy switch
        {
            WorkItemSortField.Title => By(rows, r => r.Item.Title, descending),
            WorkItemSortField.State => By(rows, CategoryRank, descending).ThenByDirection(r => r.Item.State, descending),
            WorkItemSortField.Priority => EmptyLast(rows, r => r.Item.Priority == 0, r => r.Item.Priority, descending),
            WorkItemSortField.StartDate => EmptyLast(rows, r => r.Item.StartDate == null, r => r.Item.StartDate, descending),
            WorkItemSortField.DueDate => EmptyLast(rows, r => r.Item.DueDate == null, r => r.Item.DueDate, descending),
            WorkItemSortField.Estimate => EmptyLast(rows, r => r.Item.Estimate == null, r => r.Item.Estimate, descending),
            WorkItemSortField.CreatedAt => By(rows, r => r.Item.CreatedAt, descending),
            WorkItemSortField.UpdatedAt => By(rows, r => r.Item.UpdatedAt, descending),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.SortBy, "排序欄位在建立查詢時就檢查過了。"),
        };
        return InProjectOrder(sorted);
    }

    private static IOrderedQueryable<RankedItem> InProjectOrder(IOrderedQueryable<RankedItem> sorted) =>
        sorted.ThenBy(r => r.ProjectCreatedAt).ThenBy(r => r.Item.ProjectId).ThenBy(r => r.Item.Number);

    private static readonly Expression<Func<RankedItem, int>> CategoryRank = r =>
        r.Item.StateCategory == StateCategoryText.Backlog ? 0
        : r.Item.StateCategory == StateCategoryText.NotStarted ? 1
        : r.Item.StateCategory == StateCategoryText.InProgress ? 2
        : r.Item.StateCategory == StateCategoryText.Done ? 3
        : 4;

    private static IOrderedQueryable<RankedItem> By<TKey>(
        IQueryable<RankedItem> rows, Expression<Func<RankedItem, TKey>> key, bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    private static IOrderedQueryable<RankedItem> ThenByDirection<TKey>(
        this IOrderedQueryable<RankedItem> rows, Expression<Func<RankedItem, TKey>> key, bool descending) =>
        descending ? rows.ThenByDescending(key) : rows.ThenBy(key);

    // 先依「是不是空的」排（false 在前），空值在兩個方向都排最後。優先順序「無」也算空的。
    private static IOrderedQueryable<RankedItem> EmptyLast<TKey>(
        IQueryable<RankedItem> rows,
        Expression<Func<RankedItem, bool>> isEmpty,
        Expression<Func<RankedItem, TKey>> key,
        bool descending)
    {
        var ordered = rows.OrderBy(isEmpty);
        return descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
    }
}
