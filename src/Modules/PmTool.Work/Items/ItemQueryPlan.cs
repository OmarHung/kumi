using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <param name="ProjectKey">上層項目的專案代號，已經轉成大寫。</param>
internal sealed record ParentFilter(string ProjectKey, long Number);

/// <summary>
/// 項目清單的查詢：檢查條件、套用篩選與排序。所有使用者給的值都經過 LINQ 變成參數，不會拼進 SQL。
/// 排序時空值一律排在最後，同值再依流水號，分頁才穩定。
/// </summary>
internal sealed record ItemQueryPlan(WorkItemQuery Query, int Offset, string? Search, ParentFilter? Parent)
{
    // ILIKE 預設的跳脫字元也是反斜線；寫明白，不依賴資料庫的設定。
    internal const string LikeEscape = "\\";

    public static Result<ItemQueryPlan> From(WorkItemQuery query)
    {
        if (query.Limit is < 1 or > WorkItemQuery.MaxLimit)
        {
            return WorkFailures.QueryInvalid(QueryPaths.Limit, $"每頁的數量要在 1 到 {WorkItemQuery.MaxLimit} 之間。");
        }

        if (FilterProblem(query) is { } problem)
        {
            return WorkFailures.Invalid([problem]);
        }

        if (!Enum.IsDefined(query.SortBy))
        {
            return WorkFailures.QueryInvalid(QueryPaths.Sort, "不認得的排序欄位。");
        }

        if (!ItemCursor.TryDecode(query.Cursor, out var offset) || offset > int.MaxValue - WorkItemQuery.MaxLimit)
        {
            return WorkFailures.QueryInvalid(QueryPaths.Cursor, "分頁的游標不對，請從第一頁重新查詢。");
        }

        var search = query.Search?.Trim();
        if (search is { Length: > WorkRules.MaxTitleLength } || (search is not null && TextRules.HasForbiddenCharacters(search, allowLineBreaks: false)))
        {
            return WorkFailures.QueryInvalid(
                QueryPaths.Search, $"搜尋的文字最多 {WorkRules.MaxTitleLength} 個字，而且不能有換行或控制字元。");
        }

        // 格式不對是呼叫端給錯了，要說；格式對但找不到的上層和找不到項目一樣，結果是空的，不透露別的專案有什麼。
        ItemKeys.TryParse(query.ParentKey, out var parentProject, out var parentNumber);
        if (query.ParentKey is not null && parentProject is null)
        {
            return WorkFailures.QueryInvalid(
                QueryPaths.Parent, $"上層項目的編號要像 WEB-12 這樣，{TextRules.Quote(query.ParentKey)}不是項目編號。");
        }

        var parent = parentProject is null ? null : new ParentFilter(parentProject, parentNumber);
        return new ItemQueryPlan(query, offset, string.IsNullOrEmpty(search) ? null : search, parent);
    }

    /// <summary>
    /// 篩選條件。查詢不只從 API 來，清單本身或裡面的值都可能是 null，列舉也可能是沒定義過的整數。
    /// 類型與狀態會變成資料庫的參數：NUL 與落單的代理字元會讓資料庫或驅動程式出錯，要在這裡擋下；
    /// 其他寫法正常、只是藍圖裡沒有的 key 照常查，自然找不到。
    /// </summary>
    private static FailureDetail? FilterProblem(WorkItemQuery query) =>
        KeysProblem(query.Types, QueryPaths.Type, "類型")
        ?? KeysProblem(query.States, QueryPaths.State, "狀態")
        ?? CategoriesProblem(query.StateCategories)
        ?? PrioritiesProblem(query.Priorities)
        ?? (query.OnlyWithoutDueDate && (query.DueFrom is not null || query.DueTo is not null
            || query.DatesFrom is not null || query.DatesTo is not null)
            ? Problem(QueryPaths.NoDue, "不能同時篩選「沒有到期日」和日期的範圍。")
            : null)
        ?? (query.AssigneeId is not null && query.OnlyUnassigned
            ? Problem(QueryPaths.Assignee, "不能同時篩選「某人負責」和「沒有負責人」。")
            : null)
        ?? (query.CycleId is not null && query.OnlyWithoutCycle
            ? Problem(QueryPaths.Cycle, "不能同時篩選「某個週期」和「沒有週期」。")
            : null)
        ?? (query.MilestoneId is not null && query.OnlyWithoutMilestone
            ? Problem(QueryPaths.Milestone, "不能同時篩選「某個里程碑」和「沒有里程碑」。")
            : null);

    private static FailureDetail? KeysProblem(IReadOnlyList<string>? keys, string parameter, string name)
    {
        if (keys is null)
        {
            return Problem(parameter, $"{name}的條件不能是 null；沒有條件時給空的清單。");
        }

        return keys.Any(key => key is null || TextRules.HasForbiddenCharacters(key, allowLineBreaks: false))
            ? Problem(parameter, $"{name}的條件不能是空值，也不能有控制字元或無效的字元。")
            : null;
    }

    private static FailureDetail? CategoriesProblem(IReadOnlyList<StateCategory>? categories)
    {
        if (categories is null)
        {
            return Problem(QueryPaths.StateCategory, "狀態類別的條件不能是 null；沒有條件時給空的清單。");
        }

        return categories.All(category => Enum.IsDefined(category)) ? null : Problem(QueryPaths.StateCategory, "不認得的狀態類別。");
    }

    private static FailureDetail? PrioritiesProblem(IReadOnlyList<Priority>? priorities)
    {
        if (priorities is null)
        {
            return Problem(QueryPaths.Priority, "優先度的條件不能是 null；沒有條件時給空的清單。");
        }

        return priorities.All(priority => Enum.IsDefined(priority)) ? null : Problem(QueryPaths.Priority, "不認得的優先度。");
    }

    private static FailureDetail Problem(string parameter, string message) => new(WorkErrorCodes.QueryInvalid, parameter, message);

    /// <summary>要對照專案藍圖的條件（欄位的篩選與排序）；格式的檢查在 <see cref="From"/>，這裡要先讀到專案。</summary>
    public Failure? CheckAgainst(ProjectSchema schema) =>
        FieldQueryRules.Check(Query, schema) is { Count: > 0 } details ? WorkFailures.Invalid(details) : null;

    public IQueryable<ItemEntity> Apply(IQueryable<ItemEntity> items, ProjectAccess access, Guid me) =>
        Sort(access.Items.Apply(Filter(items, access.Project, access.Schema, me)), access.Schema).Skip(Offset).Take(Query.Limit + 1);

    private IQueryable<ItemEntity> Filter(IQueryable<ItemEntity> items, ProjectEntity project, ProjectSchema schema, Guid me)
    {
        var query = Query;

        // 刪除的項目不列。上層的篩選不必另外排除刪除的上層：刪除的項目底下只會有刪除的下層，這裡已經排掉了。
        var filtered = items.Where(i => i.OrganizationId == project.OrganizationId && i.ProjectId == project.Id && i.DeletedAt == null);
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
        filtered = query.CreatedBy is { } creator ? filtered.Where(i => i.CreatedBy == creator) : filtered;
        // timestamptz 只收 Offset 是 0 的值，呼叫端傳進來的可能是別的時區。
        filtered = query.UpdatedBefore?.ToUniversalTime() is { } updatedBefore ? filtered.Where(i => i.UpdatedAt < updatedBefore) : filtered;
        filtered = FilterByPriorityAndDue(filtered, query);
        filtered = FilterByPlanning(filtered, query);
        filtered = query.FieldFilters.Count > 0 ? FieldQuery.Filter(filtered, query.FieldFilters, schema, me) : filtered;
        filtered = Parent is null ? filtered : ChildrenOf(filtered, items, project, Parent);
        return Search is null ? filtered : Searching(filtered, project, Search);
    }

    // 有日期範圍時，沒有到期日的項目 DueDate 是 null，比較結果不成立，自然不會出現。
    internal static IQueryable<ItemEntity> FilterByPriorityAndDue(IQueryable<ItemEntity> filtered, WorkItemQuery query)
    {
        if (query.Priorities.Count > 0)
        {
            var priorities = query.Priorities.Select(priority => (short)priority).ToArray();
            filtered = filtered.Where(i => priorities.Contains(i.Priority));
        }

        if (query.OnlyWithoutDueDate)
        {
            filtered = filtered.Where(i => i.DueDate == null);
        }

        if (query.DueFrom is { } from)
        {
            filtered = filtered.Where(i => i.DueDate >= from);
        }

        filtered = query.DueTo is { } to ? filtered.Where(i => i.DueDate <= to) : filtered;
        return FilterByDatesOverlap(filtered, query);
    }

    // 只有一個日期的項目，期間就是那一天，所以兩邊都用 coalesce 補上；兩個都 null 時比較不成立，自然排除。
    private static IQueryable<ItemEntity> FilterByDatesOverlap(IQueryable<ItemEntity> filtered, WorkItemQuery query)
    {
        if (query.DatesTo is { } datesTo)
        {
            filtered = filtered.Where(i => (i.StartDate ?? i.DueDate) <= datesTo);
        }

        return query.DatesFrom is { } datesFrom
            ? filtered.Where(i => (i.DueDate ?? i.StartDate) >= datesFrom)
            : filtered;
    }

    // 別的專案或不存在的編號：這個專案的項目不會指到它，自然是空的，不算錯。
    internal static IQueryable<ItemEntity> FilterByPlanning(IQueryable<ItemEntity> filtered, WorkItemQuery query)
    {
        filtered = query.OnlyWithoutCycle ? filtered.Where(i => i.CycleId == null)
            : query.CycleId is { } cycle ? filtered.Where(i => i.CycleId == cycle)
            : filtered;
        return query.OnlyWithoutMilestone ? filtered.Where(i => i.MilestoneId == null)
            : query.MilestoneId is { } milestone ? filtered.Where(i => i.MilestoneId == milestone)
            : filtered;
    }

    /// <summary>只要直接掛在這個上層底下的；上層在別的專案時一定沒有，不必查。</summary>
    private static IQueryable<ItemEntity> ChildrenOf(
        IQueryable<ItemEntity> filtered, IQueryable<ItemEntity> items, ProjectEntity project, ParentFilter parent)
    {
        if (parent.ProjectKey != project.Key)
        {
            return filtered.Where(_ => false);
        }

        var number = parent.Number;
        return filtered.Where(i => items.Any(p =>
            p.OrganizationId == project.OrganizationId && p.ProjectId == project.Id && p.Number == number && p.Id == i.ParentId));
    }

    /// <summary>標題包含這段文字（萬用字元當成普通字元），或項目編號就是它。</summary>
    private static IQueryable<ItemEntity> Searching(IQueryable<ItemEntity> items, ProjectEntity project, string search)
    {
        var pattern = "%" + EscapeLike(search) + "%";
        return ItemKeys.TryParse(search, out var projectKey, out var number) && projectKey == project.Key
            ? items.Where(i => EF.Functions.ILike(i.Title, pattern, LikeEscape) || i.Number == number)
            : items.Where(i => EF.Functions.ILike(i.Title, pattern, LikeEscape));
    }

    internal static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

    private IQueryable<ItemEntity> Sort(IQueryable<ItemEntity> items, ProjectSchema schema)
    {
        var descending = Query.Descending;
        var sorted = Query.SortBy switch
        {
            WorkItemSortField.Field => FieldQuery.Sort(items, Query.SortFieldKey!, schema, descending),
            WorkItemSortField.Number => By(items, i => i.Number, descending),
            WorkItemSortField.Title => By(items, i => i.Title, descending),
            WorkItemSortField.State => By(items, StateRank(schema), descending),
            WorkItemSortField.Priority => EmptyLast(items, i => i.Priority == 0, i => i.Priority, descending),
            WorkItemSortField.StartDate => EmptyLast(items, i => i.StartDate == null, i => i.StartDate, descending),
            WorkItemSortField.DueDate => EmptyLast(items, i => i.DueDate == null, i => i.DueDate, descending),
            WorkItemSortField.Estimate => EmptyLast(items, i => i.Estimate == null, i => i.Estimate, descending),
            WorkItemSortField.CreatedAt => By(items, i => i.CreatedAt, descending),
            WorkItemSortField.UpdatedAt => By(items, i => i.UpdatedAt, descending),
            _ => throw new ArgumentOutOfRangeException(nameof(items), Query.SortBy, "排序欄位在建立查詢時就檢查過了。"),
        };
        return sorted.ThenBy(i => i.Number);
    }

    private static IOrderedQueryable<ItemEntity> By<TKey>(
        IQueryable<ItemEntity> items, Expression<Func<ItemEntity, TKey>> key, bool descending) =>
        descending ? items.OrderByDescending(key) : items.OrderBy(key);

    // 先依「是不是空的」排（false 在前），空值在兩個方向都排最後。優先順序「無」也算空的。
    private static IOrderedQueryable<ItemEntity> EmptyLast<TKey>(
        IQueryable<ItemEntity> items,
        Expression<Func<ItemEntity, bool>> isEmpty,
        Expression<Func<ItemEntity, TKey>> key,
        bool descending)
    {
        var ordered = items.OrderBy(isEmpty);
        return descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
    }

    /// <summary>
    /// 狀態依它在流程裡的位置排，不是依 key 的字母。組成 CASE WHEN state = '…' THEN 位置；
    /// 這些 key 來自已驗證的藍圖（只有小寫字母、數字、底線），不是使用者輸入。不在藍圖裡的狀態排最後。
    /// </summary>
    private static Expression<Func<ItemEntity, int>> StateRank(ProjectSchema schema)
    {
        var item = Expression.Parameter(typeof(ItemEntity), "item");
        var state = Expression.Property(item, nameof(ItemEntity.State));
        Expression rank = Expression.Constant(int.MaxValue);
        foreach (var (key, position) in schema.StateRanks.OrderByDescending(pair => pair.Value))
        {
            rank = Expression.Condition(Expression.Equal(state, Expression.Constant(key)), Expression.Constant(position), rank);
        }

        return Expression.Lambda<Func<ItemEntity, int>>(rank, item);
    }
}
