using System.Text.RegularExpressions;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <summary>
/// 依自訂欄位篩選與排序的條件檢查。要對照專案的藍圖（欄位在不在、型別能不能篩），所以在讀到專案之後才做。
/// 選項的 key 只檢查格式、不對照藍圖：藍圖可能改過，不認得的自然找不到，和類型、狀態的做法一樣。
/// </summary>
internal static partial class FieldQueryRules
{
    public const string Me = "me";

    public const string FieldPath = "field";

    public const string True = "true";

    public const string False = "false";

    private static readonly FieldType[] FilterableTypes =
        [FieldType.SingleSelect, FieldType.MultiSelect, FieldType.Person, FieldType.Checkbox];

    private static readonly FieldType[] SortableTypes =
        [FieldType.Text, FieldType.Number, FieldType.Currency, FieldType.Date, FieldType.SingleSelect, FieldType.Checkbox];

    public static string PathOf(string key) => $"{FieldPath}.{key}";

    public static IReadOnlyList<FailureDetail> Check(WorkItemQuery query, ProjectSchema schema)
    {
        var details = new List<FailureDetail>();
        var filters = query.FieldFilters;
        if (filters is null)
        {
            details.Add(Problem(FieldPath, "欄位的條件不能是 null；沒有條件時給空的清單。"));
        }
        else if (filters.Count > FieldFilter.MaxPerQuery)
        {
            details.Add(Problem(FieldPath, $"欄位的條件最多 {FieldFilter.MaxPerQuery} 個，現在有 {filters.Count} 個。"));
        }
        else
        {
            details.AddRange(CheckFilters(filters, schema));
        }

        if (CheckSort(query, schema) is { } sortProblem)
        {
            details.Add(sortProblem);
        }

        return details;
    }

    private static IEnumerable<FailureDetail> CheckFilters(IReadOnlyList<FieldFilter> filters, ProjectSchema schema)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var filter in filters)
        {
            // 不像 key 的不回顯：長度、內容都不限，原樣放進路徑與訊息就是把任意文字反射回去。
            if (filter?.Key is not { } key || !OptionKeyPattern().IsMatch(key))
            {
                yield return Problem(FieldPath, "欄位的名稱要是藍圖的 key：小寫字母開頭，只有小寫字母、數字與底線，2 到 40 個字元。");
                continue;
            }

            var problem = !seen.Add(key)
                ? $"欄位{Quote(key)}只能出現一次。"
                : CheckFilter(filter, schema);
            if (problem is not null)
            {
                yield return Problem(PathOf(key), problem);
            }
        }
    }

    private static string? CheckFilter(FieldFilter filter, ProjectSchema schema)
    {
        if (!schema.Fields.TryGetValue(filter.Key, out var field))
        {
            return $"這個專案沒有{Quote(filter.Key)}這個欄位。";
        }

        if (!FilterableTypes.Contains(field.Type))
        {
            return $"{Quote(field.Name)}不能篩選。";
        }

        if (filter.Values is null)
        {
            return $"{Quote(field.Name)}的值不能是 null；沒有值時給空的清單。";
        }

        if (filter.Values.Count > FieldFilter.MaxValues)
        {
            return $"{Quote(field.Name)}最多 {FieldFilter.MaxValues} 個值，現在有 {filter.Values.Count} 個。";
        }

        if (filter.IncludeEmpty && field.Type == FieldType.Checkbox)
        {
            return $"{Quote(field.Name)}是勾選欄位，沒勾就是 false，不能再篩選「沒有填」。";
        }

        if (filter.Values.Count == 0 && !filter.IncludeEmpty)
        {
            return $"{Quote(field.Name)}要給至少一個值，或篩選「沒有填」。";
        }

        return filter.Values.Select(value => ValueProblem(field, value)).FirstOrDefault(problem => problem is not null);
    }

    private static string? ValueProblem(FieldDefinition field, string? value)
    {
        if (value is null || HasForbiddenCharacters(value, allowLineBreaks: false))
        {
            return $"{Quote(field.Name)}的值不能是空值，也不能有控制字元或無效的字元。";
        }

        return field.Type switch
        {
            FieldType.Person when value == Me || Guid.TryParseExact(value, "D", out _) => null,
            FieldType.Person => $"{Quote(field.Name)}的值要是 {Me} 或人員編號。",
            FieldType.Checkbox when value is True or False => null,
            FieldType.Checkbox => $"{Quote(field.Name)}的值要是 {True} 或 {False}。",
            _ when OptionKeyPattern().IsMatch(value) => null,
            _ => $"{Quote(field.Name)}的值要是選項的 key：小寫字母開頭，只有小寫字母、數字與底線，2 到 40 個字元。",
        };
    }

    private static FailureDetail? CheckSort(WorkItemQuery query, ProjectSchema schema)
    {
        if (query.SortBy != WorkItemSortField.Field)
        {
            return query.SortFieldKey is null ? null : Problem(QueryPaths.Sort, "只有依欄位排序時才能指定欄位。");
        }

        if (query.SortFieldKey is not { } key || HasForbiddenCharacters(key, allowLineBreaks: false))
        {
            return Problem(QueryPaths.Sort, "依欄位排序要指定欄位的 key。");
        }

        if (!schema.Fields.TryGetValue(key, out var field))
        {
            return Problem(QueryPaths.Sort, $"這個專案沒有{Quote(key)}這個欄位。");
        }

        return SortableTypes.Contains(field.Type) ? null : Problem(QueryPaths.Sort, $"{Quote(field.Name)}不能排序。");
    }

    private static FailureDetail Problem(string path, string message) => new(WorkErrorCodes.QueryInvalid, path, message);

    // \A \z 而不是 ^ $：.NET 的 $ 會讓結尾多一個換行也通過。
    [GeneratedRegex(@"\A[a-z][a-z0-9_]{1,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OptionKeyPattern();
}
