using System.Globalization;
using Microsoft.Extensions.Primitives;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 項目清單的查詢參數轉成 <see cref="WorkItemQuery"/>。這裡只管字串認不認得；
/// 類型與狀態存不存在由工作引擎判斷，規則只有一份；空值、重複、limit 與 cursor 的收法見 <see cref="QueryParameters"/>。
/// 不認得的參數不理會。
/// </summary>
internal static class ItemQueryParser
{
    private const string AssigneeMe = "me";
    private const string AssigneeUnassigned = "unassigned";
    private const string None = "none";
    private const string Ascending = "asc";
    private const string Descending = "desc";

    private const string FieldParameterPrefix = "field.";
    private const string NoneValue = "_none";

    // field 要帶欄位的 key，單獨寫沒有意思，所以訊息裡另外說明，不當成排序欄位列出來。
    private static readonly string SortFieldNames = string.Join(
        "、", SnakeCaseEnums.AllNames<WorkItemSortField>().Where(name => name != "field").Order());

    public static Result<WorkItemQuery> Parse(IQueryCollection query, Guid me)
    {
        return ToQuery(query, ParseAll(query, me));
    }

    private static ParsedQuery ParseAll(IQueryCollection query, Guid me)
    {
        return new ParsedQuery(
            ParseFieldFilters(query),
            ParseCategories(query["stateCategory"]),
            QueryParameters.Single(query, "assignee").Then(value => ParseAssignee(value, me)),
            QueryParameters.Single(query, "createdBy").Then(value => ParseCreatedBy(value, me)),
            QueryParameters.Single(query, "updatedBefore").Then(ParseInstant),
            QueryParameters.Single(query, "sort").Then(ParseSort),
            QueryParameters.LimitOf(query, WorkItemQuery.DefaultLimit),
            QueryParameters.Single(query, "q"),
            QueryParameters.Single(query, "parent"),
            QueryParameters.Single(query, QueryParameters.Cursor),
            ParsePriorities(query["priority"]),
            DateRange(query, "dueFrom", "dueTo"),
            DateRange(query, "datesFrom", "datesTo"),
            QueryParameters.Single(query, "noDue").Then(ParseNoDue),
            QueryParameters.Single(query, "cycle").Then(value => ParseReference(value, "cycle", "週期")),
            QueryParameters.Single(query, "milestone").Then(value => ParseReference(value, "milestone", "里程碑")));
    }

    private static DateBounds DateRange(IQueryCollection query, string from, string to) =>
        new(QueryParameters.Single(query, from).Then(value => ParseDate(value, from)),
            QueryParameters.Single(query, to).Then(value => ParseDate(value, to)));

    private static Result<WorkItemQuery> ToQuery(IQueryCollection query, ParsedQuery p)
    {
        var failure = QueryParameters.FailureOf(
            [.. p.Fields.Problems,
            p.Categories.Problem, p.Assignee.Problem, p.CreatedBy.Problem, p.UpdatedBefore.Problem, p.Sort.Problem, p.Limit.Problem, p.Search.Problem, p.Parent.Problem, p.Cursor.Problem,
            p.Priorities.Problem, p.Due.From.Problem, p.Due.To.Problem, p.Dates.From.Problem, p.Dates.To.Problem, p.NoDue.Problem, p.Cycle.Problem, p.Milestone.Problem]);
        if (failure is not null)
        {
            return failure;
        }

        return new WorkItemQuery
        {
            Types = QueryParameters.NonEmpty(query["type"]),
            States = QueryParameters.NonEmpty(query["state"]),
            StateCategories = p.Categories.Value,
            AssigneeId = p.Assignee.Value.PersonId,
            OnlyUnassigned = p.Assignee.Value.OnlyUnassigned,
            CreatedBy = p.CreatedBy.Value,
            UpdatedBefore = p.UpdatedBefore.Value,
            Priorities = p.Priorities.Value,
            DueFrom = p.Due.From.Value,
            DueTo = p.Due.To.Value,
            DatesFrom = p.Dates.From.Value,
            DatesTo = p.Dates.To.Value,
            OnlyWithoutDueDate = p.NoDue.Value,
            CycleId = p.Cycle.Value.Id,
            OnlyWithoutCycle = p.Cycle.Value.OnlyNone,
            MilestoneId = p.Milestone.Value.Id,
            OnlyWithoutMilestone = p.Milestone.Value.OnlyNone,
            Search = p.Search.Value,
            ParentKey = p.Parent.Value,
            FieldFilters = p.Fields.Filters,
            SortBy = p.Sort.Value.Field,
            SortFieldKey = p.Sort.Value.FieldKey,
            Descending = p.Sort.Value.Descending,
            Limit = p.Limit.Value,
            Cursor = p.Cursor.Value,
        };
    }

    private static Parsed<IReadOnlyList<StateCategory>> ParseCategories(StringValues values)
    {
        // 和 JSON 的列舉用同一份名字，網頁在兩邊看到的字串一樣。
        var names = QueryParameters.NonEmpty(values);
        var unknown = names.Where(name => !SnakeCaseEnums.TryParse<StateCategory>(name, out _)).ToArray();
        return unknown.Length > 0
            ? Parsed<IReadOnlyList<StateCategory>>.Invalid([], "stateCategory", $"不認得的狀態類別：{string.Join("、", unknown)}。")
            : new Parsed<IReadOnlyList<StateCategory>>([.. names.Select(Category)]);
    }

    private static Parsed<IReadOnlyList<Priority>> ParsePriorities(StringValues values)
    {
        var names = QueryParameters.NonEmpty(values);
        var unknown = names.Where(name => !SnakeCaseEnums.TryParse<Priority>(name, out _)).ToArray();
        return unknown.Length > 0
            ? Parsed<IReadOnlyList<Priority>>.Invalid([], "priority", $"不認得的優先度：{string.Join("、", unknown)}。")
            : new Parsed<IReadOnlyList<Priority>>([.. names.Select(name => SnakeCaseEnums.TryParse<Priority>(name, out var priority) ? priority : default)]);
    }

    // 只收 YYYY-MM-DD：網頁與 CLI 都送這個格式，寬鬆的寫法（少補零、帶時間）留給呼叫端自己轉。
    private static Parsed<DateOnly?> ParseDate(string? value, string parameter) =>
        value is null ? new Parsed<DateOnly?>(null)
        : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? new Parsed<DateOnly?>(date)
        : Parsed<DateOnly?>.Invalid(null, parameter, "日期要寫成 YYYY-MM-DD。");

    // 只收帶時區（Z 或 ±HH:mm）的 ISO 8601，小數秒可有可無：沒帶時區的時間不知道是哪裡的幾點，伺服器不猜；
    // 寬鬆的解析會把 RFC 1123 這類寫法也收進來。結果一律換成 UTC，資料庫的 timestamptz 只收 Offset 是 0 的值。
    private static readonly string[] InstantFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    private static Parsed<DateTimeOffset?> ParseInstant(string? value)
    {
        if (value is null)
        {
            return new Parsed<DateTimeOffset?>(null);
        }

        // 引號裡的 Z 不帶時區資訊，要明說當 UTC，否則會照伺服器的本地時區算。zzz 也收 +0800，這裡擋掉。
        return HasStandardZone(value)
            && DateTimeOffset.TryParseExact(value, InstantFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? new Parsed<DateTimeOffset?>(instant.ToUniversalTime())
            : Parsed<DateTimeOffset?>.Invalid(null, "updatedBefore", "時間要寫成含時區的 ISO 8601，例如 2026-11-26T10:00:00Z。");
    }

    private static bool HasStandardZone(string value) =>
        value.EndsWith('Z') || (value.Length > 6 && value[^3] == ':' && value[^6] is '+' or '-');

    private static Parsed<bool> ParseNoDue(string? value) => value switch
    {
        null => new Parsed<bool>(false),
        _ when value.Equals("true", StringComparison.OrdinalIgnoreCase) => new Parsed<bool>(true),
        _ when value.Equals("false", StringComparison.OrdinalIgnoreCase) => new Parsed<bool>(false),
        _ => Parsed<bool>.Invalid(false, "noDue", "noDue 要是 true 或 false。"),
    };

    // 「目前的週期」伺服器不知道使用者的今天，所以只收 none 或編號；網頁自己換成編號再送。
    private static Parsed<ReferenceFilter> ParseReference(string? value, string parameter, string label) => value switch
    {
        null => new Parsed<ReferenceFilter>(new ReferenceFilter(null, false)),
        None => new Parsed<ReferenceFilter>(new ReferenceFilter(null, true)),
        _ when Guid.TryParseExact(value, "D", out var id) => new Parsed<ReferenceFilter>(new ReferenceFilter(id, false)),
        _ => Parsed<ReferenceFilter>.Invalid(new ReferenceFilter(null, false), parameter, $"{label}要是 {None} 或{label}編號。"),
    };

    private static Parsed<AssigneeFilter> ParseAssignee(string? value, Guid me) => value switch
    {
        null => new Parsed<AssigneeFilter>(new AssigneeFilter(null, false)),
        AssigneeMe => new Parsed<AssigneeFilter>(new AssigneeFilter(me, false)),
        AssigneeUnassigned => new Parsed<AssigneeFilter>(new AssigneeFilter(null, true)),
        _ when Guid.TryParse(value, out var personId) => new Parsed<AssigneeFilter>(new AssigneeFilter(personId, false)),
        _ => Parsed<AssigneeFilter>.Invalid(new AssigneeFilter(null, false), "assignee", "負責人要是 me、unassigned 或人員編號。"),
    };

    private static Parsed<Guid?> ParseCreatedBy(string? value, Guid me) => value switch
    {
        null => new Parsed<Guid?>(null),
        AssigneeMe => new Parsed<Guid?>(me),
        _ when Guid.TryParse(value, out var personId) => new Parsed<Guid?>(personId),
        _ => Parsed<Guid?>.Invalid(null, "createdBy", "建立者要是 me 或人員編號。"),
    };

    private static Parsed<SortOrder> ParseSort(string? value)
    {
        var fallback = new SortOrder(new WorkItemQuery().SortBy, new WorkItemQuery().Descending);
        if (value is null)
        {
            return new Parsed<SortOrder>(fallback);
        }

        var parts = value.Split(':');
        if (parts is [var custom, Ascending or Descending] && custom.StartsWith(FieldParameterPrefix, StringComparison.Ordinal))
        {
            return new Parsed<SortOrder>(new SortOrder(WorkItemSortField.Field, parts[1] == Descending, custom[FieldParameterPrefix.Length..]));
        }

        // field 單獨寫不收：它一定要帶欄位的 key。
        return parts is [var field, Ascending or Descending]
            && field != "field"
            && SnakeCaseEnums.TryParse<WorkItemSortField>(field, out var sortBy)
            ? new Parsed<SortOrder>(new SortOrder(sortBy, parts[1] == Descending))
            : Parsed<SortOrder>.Invalid(
                fallback, "sort", $"排序要寫成「欄位:asc」或「欄位:desc」，欄位是 {SortFieldNames} 其中之一，或 field.<欄位的 key>。");
    }

    /// <summary>
    /// <c>field.&lt;key&gt;=值</c>：同一個 key 可以重複，值之間是「或」；<c>_none</c> 是沒有填。
    /// 空的值當作沒給（網頁常把空的欄位照樣送出來）；值裡有逗號是錯，CLI 要自己拆開再送。
    /// 欄位在不在藍圖、值的格式由模組檢查，規則只有一份。
    /// </summary>
    private static ParsedFields ParseFieldFilters(IQueryCollection query)
    {
        var filters = new List<FieldFilter>();
        var problems = new List<FailureDetail>();
        // 前綴不分大小寫，和其他參數一樣；分大小寫的話 Field.client 會被默默略過，使用者以為已經篩了。
        foreach (var (name, raw) in query.Where(pair => pair.Key.StartsWith(FieldParameterPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var values = QueryParameters.NonEmpty(raw);
            if (values.Any(value => value.Contains(',', StringComparison.Ordinal)))
            {
                problems.Add(new FailureDetail(
                    WorkErrorCodes.QueryInvalid, name, "一個值裡不能有逗號；要選多個值，把參數重複給幾次。"));
                continue;
            }

            if (values.Count > 0)
            {
                filters.Add(new FieldFilter(
                    name[FieldParameterPrefix.Length..],
                    [.. values.Where(value => value != NoneValue)],
                    IncludeEmpty: values.Contains(NoneValue)));
            }
        }

        return new ParsedFields(filters, problems);
    }

    private static StateCategory Category(string name) =>
        SnakeCaseEnums.TryParse<StateCategory>(name, out var category) ? category : default;

    private sealed record ReferenceFilter(Guid? Id, bool OnlyNone);

    private sealed record AssigneeFilter(Guid? PersonId, bool OnlyUnassigned);

    private sealed record SortOrder(WorkItemSortField Field, bool Descending, string? FieldKey = null);

    private sealed record DateBounds(Parsed<DateOnly?> From, Parsed<DateOnly?> To);

    private sealed record ParsedQuery(
        ParsedFields Fields,
        Parsed<IReadOnlyList<StateCategory>> Categories,
        Parsed<AssigneeFilter> Assignee,
        Parsed<Guid?> CreatedBy,
        Parsed<DateTimeOffset?> UpdatedBefore,
        Parsed<SortOrder> Sort,
        Parsed<int> Limit,
        Parsed<string?> Search,
        Parsed<string?> Parent,
        Parsed<string?> Cursor,
        Parsed<IReadOnlyList<Priority>> Priorities,
        DateBounds Due,
        DateBounds Dates,
        Parsed<bool> NoDue,
        Parsed<ReferenceFilter> Cycle,
        Parsed<ReferenceFilter> Milestone);

    private sealed record ParsedFields(IReadOnlyList<FieldFilter> Filters, IReadOnlyList<FailureDetail> Problems);
}
