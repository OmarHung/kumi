using System.Globalization;
using PmTool.Abstractions;
using PmTool.Work.Common;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Planning;

/// <summary>
/// 週期與里程碑的欄位檢查。一次回報所有問題；文字去掉頭尾空白後才檢查與儲存。
/// 範例檔也用這裡的規則（<see cref="Templates.ExamplePlanningChecker"/>），範例才不會存進一般使用者存不進去的東西。
/// </summary>
internal static class PlanningChecks
{
    public const string NamePath = "name";
    public const string StartDatePath = "startDate";
    public const string EndDatePath = "endDate";
    public const string DueDatePath = "dueDate";
    public const string DescriptionPath = "description";

    public static Result<CycleDraft> Check(CycleDraft? draft)
    {
        if (draft is null)
        {
            return WorkFailures.Invalid(WorkErrorCodes.PlanningInvalid, string.Empty, "請提供週期的內容。");
        }

        var name = draft.Name?.Trim() ?? string.Empty;
        var problems = CycleProblems(name, draft.StartDate, draft.EndDate);
        return problems.Count > 0 ? WorkFailures.Invalid(problems) : draft with { Name = name };
    }

    public static Result<MilestoneDraft> Check(MilestoneDraft? draft)
    {
        if (draft is null)
        {
            return WorkFailures.Invalid(WorkErrorCodes.PlanningInvalid, string.Empty, "請提供里程碑的內容。");
        }

        var name = draft.Name?.Trim() ?? string.Empty;
        var description = NormalizeDescription(draft.Description);
        var problems = MilestoneProblems(name, description);
        return problems.Count > 0 ? WorkFailures.Invalid(problems) : draft with { Name = name, Description = description };
    }

    /// <param name="name">已經去掉頭尾空白。</param>
    public static IReadOnlyList<FailureDetail> CycleProblems(string name, DateOnly start, DateOnly end) =>
        [.. new[] { NameProblem(name, PlanningRules.MaxCycleNameLength, "週期"), SpanProblem(start, end) }.OfType<FailureDetail>()];

    /// <param name="name">已經去掉頭尾空白。</param>
    /// <param name="description">已經 <see cref="NormalizeDescription"/>。</param>
    public static IReadOnlyList<FailureDetail> MilestoneProblems(string name, string? description) =>
        [.. new[] { NameProblem(name, PlanningRules.MaxMilestoneNameLength, "里程碑"), DescriptionProblem(description) }.OfType<FailureDetail>()];

    /// <summary>換行統一成 \n、去掉頭尾空白；空的當成沒有說明。</summary>
    public static string? NormalizeDescription(string? description)
    {
        var trimmed = NormalizeLineBreaks(description)?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>頭尾都算：兩週的衝刺是 14 天，最長 <see cref="PlanningRules.MaxCycleDays"/> 天。</summary>
    public static int DaysOf(DateOnly start, DateOnly end) => end.DayNumber - start.DayNumber + 1;

    private static FailureDetail? NameProblem(string name, int maxLength, string label)
    {
        if (name.Length == 0)
        {
            return Problem(NamePath, $"請填{label}的名稱。");
        }

        if (name.Length > maxLength)
        {
            return Problem(NamePath, $"{label}的名稱最多 {maxLength} 個字，現在有 {name.Length} 個字。");
        }

        return HasUnsafeNameCharacters(name)
            ? Problem(NamePath, $"{label}的名稱不能有換行、控制字元、看不見的字元或無效的字元。")
            : null;
    }

    private static FailureDetail? SpanProblem(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return Problem(EndDatePath, $"結束日不能早於開始日：開始日是 {Format(start)}，結束日是 {Format(end)}。");
        }

        return DaysOf(start, end) > PlanningRules.MaxCycleDays
            ? Problem(EndDatePath, $"一個週期最長 {PlanningRules.MaxCycleDays} 天（頭尾都算），現在有 {DaysOf(start, end)} 天。")
            : null;
    }

    private static FailureDetail? DescriptionProblem(string? description)
    {
        if (description is null)
        {
            return null;
        }

        if (description.Length > PlanningRules.MaxDescriptionLength)
        {
            return Problem(DescriptionPath, $"說明最多 {PlanningRules.MaxDescriptionLength} 個字，現在有 {description.Length} 個字。");
        }

        return HasForbiddenCharacters(description, allowLineBreaks: true)
            ? Problem(DescriptionPath, "說明不能有控制字元或無效的字元；換行與 Tab 可以。")
            : null;
    }

    private static FailureDetail Problem(string path, string message) => new(WorkErrorCodes.PlanningInvalid, path, message);

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
