using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;

namespace PmTool.Work.SavedViews;

// 「我的工作」的檢視不屬於專案，條件的意思和專案的不同（見 SavedViewQuery）；專案的檢視則不能帶「我的工作」專用的條件。
internal static partial class SavedViewChecks
{
    private const string All = "all";
    private const string CreatedMine = "created";
    private const string ProjectGroup = "project";

    private static readonly string[] CategoryValues = [All, "backlog", "not_started", "in_progress", "done", "canceled"];

    public static Result<SavedViewDraft> CheckMyWork(SavedViewDraft? draft)
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
                CategoryProblem(query),
                MyWorkAssigneeProblem(query.Assignee),
                ProjectProblem(query.Project),
                OneOfProblem("mine", query.Mine, [CreatedMine], "範圍"),
                query.CreatedBy is null ? null : Problem($"{QueryPath}.createdBy", "「我的工作」的檢視沒有建立者的條件，要看自己建立的請用「我建立的」。"),
                OneOfProblem("group", query.Group, [ProjectGroup], "分組"),
                SearchProblem(query.Search),
                MyWorkSortProblem(query.Sort),
                OneOfProblem("due", query.Due, DueValues, "到期"),
                OneOfProblem("priority", query.Priority, PriorityValues, "優先度"),
                query.Cycle is null ? null : Problem($"{QueryPath}.cycle", "「我的工作」的檢視沒有週期的條件：週期是各專案自己的。"),
                query.Milestone is null ? null : Problem($"{QueryPath}.milestone", "「我的工作」的檢視沒有里程碑的條件：里程碑是各專案自己的。"),
                query.Fields is null ? null : Problem($"{QueryPath}.fields", "「我的工作」的檢視沒有自訂欄位的條件：欄位是各專案自己的。"),
                query.Open is null ? null : Problem($"{QueryPath}.open", "「我的工作」的檢視用狀態的條件選「未結束的狀態」，沒有 open。"),
                query.Stale is null ? null : Problem($"{QueryPath}.stale", "「我的工作」的檢視沒有「很久沒有更新」的條件，只有專案的檢視才有。"),
            }
            .OfType<FailureDetail>()
            .ToArray();
        return details.Length > 0
            ? WorkFailures.Invalid(details)
            : draft with { Name = name, Query = query with { Project = NormalizedProject(query.Project) } };
    }

    /// <summary>專案的檢視不能帶 Category、Project：這些只有「我的工作」看得懂，存了也不會有人用，之後意思還可能變。</summary>
    private static IEnumerable<FailureDetail> MyWorkOnlyProblems(SavedViewQuery query)
    {
        if (query.Category is not null)
        {
            yield return Problem($"{QueryPath}.category", "專案的檢視沒有狀態類別的條件，只有「我的工作」的檢視才有。");
        }

        if (query.Project is not null)
        {
            yield return Problem($"{QueryPath}.project", "專案的檢視不能指定專案，只有「我的工作」的檢視才有。");
        }

        if (query.Mine is not null)
        {
            yield return Problem($"{QueryPath}.mine", "專案的檢視沒有「我建立的」這個範圍，只有「我的工作」的檢視才有。");
        }
    }

    // 跨專案的清單不能依自訂欄位排序（各專案的欄位不同），存了套用時會被 422 整頁擋掉。
    // 「我的工作」的表格多一個「依專案」：網頁在每一組裡自己排，不是清單 API 的排序欄位，所以只有這裡收。
    private static FailureDetail? MyWorkSortProblem(string? sort) => sort switch
    {
        ProjectSortAscending or ProjectSortDescending => null,
        not null when sort.StartsWith("field.", StringComparison.Ordinal) =>
            Problem($"{QueryPath}.sort", "「我的工作」的檢視不能依自訂欄位排序：欄位是各專案自己的。"),
        _ => SortProblem(sort),
    };

    private const string ProjectSortAscending = "project:asc";
    private const string ProjectSortDescending = "project:desc";

    // 狀態的 key 只有選了專案才有意思，但存的時候不強迫：專案可能之後才選，格式對就收。
    private static FailureDetail? CategoryProblem(SavedViewQuery query)
    {
        if (query.State is not null && query.Category is not null)
        {
            return Problem($"{QueryPath}.category", "狀態類別和個別的狀態只能選一個。");
        }

        return OneOfProblem("category", query.Category, CategoryValues, "狀態類別");
    }

    // 「我的工作」永遠是自己負責的：沒有負責人的條件，存了也沒有人讀（使用者 2026-11-19 的決定）。
    private static FailureDetail? MyWorkAssigneeProblem(string? value) =>
        value is null
            ? null
            : Problem($"{QueryPath}.assignee", "「我的工作」就是你負責的項目，沒有負責人的條件。");

    // 只檢查格式，不檢查專案存不存在、看不看得到：專案之後可能封存或離開，套用時網頁會略過。
    private static FailureDetail? ProjectProblem(string? value) =>
        value is null || ProjectKeys.TryNormalize(value, out _)
            ? null
            : Problem($"{QueryPath}.project", "專案要是專案代號：大寫英文字母開頭，2 到 10 個大寫英文字母或數字。");

    private static string? NormalizedProject(string? value) => ProjectKeys.TryNormalize(value, out var normalized) ? normalized : null;
}
