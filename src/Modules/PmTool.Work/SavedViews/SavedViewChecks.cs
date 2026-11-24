using System.Text.Json;
using System.Text.RegularExpressions;
using PmTool.Abstractions;
using PmTool.Work.Common;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.SavedViews;

/// <summary>檢視的欄位檢查。一次回報所有問題；名稱去掉頭尾空白後才檢查與儲存。</summary>
internal static partial class SavedViewChecks
{
    public const string NamePath = "name";
    public const string LayoutPath = "layout";
    private const string QueryPath = "query";

    private const string Me = "me";
    private const string Unassigned = "unassigned";
    private const string Current = "current";
    private const string None = "none";
    private const string Ascending = "asc";
    private const string Descending = "desc";

    private static readonly string[] DueValues = ["overdue", "today", "week", "none"];
    private static readonly string[] PriorityValues = ["none", "low", "medium", "high", "urgent"];

    // 和 API 的 sort 參數同一套：列舉名稱的 snake_case。模組不認得 HTTP，所以這裡自己用同一個規則轉。
    // field 要帶欄位的 key（field.<key>），單獨寫沒有意思，所以不收。
    private const string FieldPrefix = "field.";
    private const string NoneValue = "_none";

    private static readonly string[] SortFields =
        [.. Enum.GetNames<WorkItemSortField>()
            .Where(name => name != nameof(WorkItemSortField.Field))
            .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)
            .Order(StringComparer.Ordinal)];

    private static readonly string[] GroupValues = ["state", "assignee"];

    public static Result<SavedViewDraft> Check(SavedViewDraft? draft)
    {
        if (draft is null)
        {
            return WorkFailures.Invalid(WorkErrorCodes.SavedViewInvalid, QueryPath, "請提供檢視的內容。");
        }

        var name = draft.Name?.Trim() ?? string.Empty;
        var query = draft.Query ?? new SavedViewQuery(null, null, null, null, null, null, null);
        var details = new[]
            {
                NameProblem(name),
                Enum.IsDefined(draft.Layout) ? null : Problem(LayoutPath, "版面要是表格、看板、行事曆或時間軸。"),
                KeyProblem("type", query.Type, "類型"),
                KeyProblem("state", query.State, "狀態"),
                AssigneeProblem(query.Assignee),
                SearchProblem(query.Search),
                SortProblem(query.Sort),
                GroupProblem(query.Group),
                OneOfProblem("due", query.Due, DueValues, "到期"),
                OneOfProblem("priority", query.Priority, PriorityValues, "優先度"),
                CycleProblem(query.Cycle),
                MilestoneProblem(query.Milestone),
            }
            .Concat(FieldsProblems(query.Fields))
            .OfType<FailureDetail>()
            .Concat(MyWorkOnlyProblems(query))
            .ToArray();
        return details.Length > 0 ? WorkFailures.Invalid(details) : draft with { Name = name, Query = query };
    }

    private static FailureDetail? NameProblem(string name)
    {
        if (name.Length == 0)
        {
            return Problem(NamePath, "請填檢視的名稱。");
        }

        if (name.Length > SavedViewRules.MaxNameLength)
        {
            return Problem(NamePath, $"檢視的名稱最多 {SavedViewRules.MaxNameLength} 個字，現在有 {name.Length} 個字。");
        }

        return HasUnsafeNameCharacters(name)
            ? Problem(NamePath, "檢視的名稱不能有換行、控制字元、看不見的字元或無效的字元。")
            : null;
    }

    // 只檢查格式：藍圖之後可能改，存的時候有的類型與狀態，套用時不一定還在，網頁套用時會略過。
    // 週期存 current 而不存編號：檢視過了一個衝刺，打開還是目前的那一個。編號只檢查格式，週期之後可能被刪。
    private static FailureDetail? CycleProblem(string? value) =>
        value is null or Current or None || Guid.TryParseExact(value, "D", out _)
            ? null
            : Problem($"{QueryPath}.cycle", $"週期要是 {Current}、{None} 或週期編號。");

    private static FailureDetail? MilestoneProblem(string? value) =>
        value is null or None || Guid.TryParseExact(value, "D", out _)
            ? null
            : Problem($"{QueryPath}.milestone", $"里程碑要是 {None} 或里程碑編號。");

    private static FailureDetail? KeyProblem(string field, string? value, string label) =>
        value is null || KeyPattern().IsMatch(value)
            ? null
            : Problem($"{QueryPath}.{field}", $"{label}要是藍圖的 key：小寫字母開頭，只有小寫字母、數字與底線，2 到 40 個字元。");

    private static FailureDetail? AssigneeProblem(string? value) =>
        value is null or Me or Unassigned || Guid.TryParseExact(value, "D", out _)
            ? null
            : Problem($"{QueryPath}.assignee", $"負責人要是 {Me}、{Unassigned} 或人員編號。");

    private static FailureDetail? SearchProblem(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > SavedViewRules.MaxSearchLength)
        {
            return Problem($"{QueryPath}.q", $"搜尋的文字最多 {SavedViewRules.MaxSearchLength} 個字。");
        }

        return HasForbiddenCharacters(value, allowLineBreaks: false)
            ? Problem($"{QueryPath}.q", "搜尋的文字不能有換行、控制字元或無效的字元。")
            : null;
    }

    private static FailureDetail? SortProblem(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var parts = value.Split(':');
        return parts is [var field, Ascending or Descending] && IsSortField(field)
            ? null
            : Problem($"{QueryPath}.sort", $"排序要寫成「欄位:asc」或「欄位:desc」，欄位是 {string.Join("、", SortFields)} 其中之一，或 field.<欄位的 key>。");
    }

    private static bool IsSortField(string field) =>
        SortFields.Contains(field, StringComparer.Ordinal) || IsFieldReference(field);

    private static bool IsFieldReference(string value) =>
        value.StartsWith(FieldPrefix, StringComparison.Ordinal) && KeyPattern().IsMatch(value[FieldPrefix.Length..]);

    private static FailureDetail? GroupProblem(string? value) =>
        value is null || GroupValues.Contains(value, StringComparer.Ordinal) || IsFieldReference(value)
            ? null
            : Problem($"{QueryPath}.group", $"分組要是 {string.Join("、", GroupValues)} 或 field.<欄位的 key>。");

    // 只檢查格式，不對照現在的藍圖：欄位可能被刪、選項可能被改，套用時不認得的略過。
    private static IEnumerable<FailureDetail?> FieldsProblems(IReadOnlyDictionary<string, string>? fields)
    {
        if (fields is null)
        {
            yield break;
        }

        if (fields.Count > FieldFilter.MaxPerQuery)
        {
            yield return Problem($"{QueryPath}.fields", $"欄位的條件最多 {FieldFilter.MaxPerQuery} 個，現在有 {fields.Count} 個。");
            yield break;
        }

        foreach (var (key, value) in fields)
        {
            var path = $"{QueryPath}.fields.{(KeyPattern().IsMatch(key) ? key : string.Empty)}".TrimEnd('.');
            yield return !KeyPattern().IsMatch(key)
                ? Problem(path, "欄位的 key 要像藍圖的 key：小寫字母開頭，只有小寫字母、數字與底線，2 到 40 個字元。")
                : FieldValueProblem(path, value);
        }
    }

    private static FailureDetail? FieldValueProblem(string path, string? value)
    {
        var tokens = value?.Split(',');
        if (tokens is null || tokens.Length > FieldFilter.MaxValues)
        {
            return Problem(path, $"欄位的條件要給 1 到 {FieldFilter.MaxValues} 個值，用逗號分開。");
        }

        return tokens.All(IsFieldToken)
            ? null
            : Problem(path, $"欄位的值要是選項的 key、人員編號、{Me}、true、false 或 {NoneValue}，多個值用逗號分開。");
    }

    private static bool IsFieldToken(string token) =>
        token is Me or "true" or "false" or NoneValue || KeyPattern().IsMatch(token) || Guid.TryParseExact(token, "D", out _);

    private static FailureDetail? OneOfProblem(string field, string? value, string[] allowed, string label) =>
        value is null || allowed.Contains(value, StringComparer.Ordinal)
            ? null
            : Problem($"{QueryPath}.{field}", $"{label}要是 {string.Join("、", allowed)} 其中之一。");

    private static FailureDetail Problem(string path, string message) =>
        new(WorkErrorCodes.SavedViewInvalid, path, message);

    // \A \z 而不是 ^ $：.NET 的 $ 會讓結尾多一個換行也通過。
    [GeneratedRegex(@"\A[a-z][a-z0-9_]{1,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
