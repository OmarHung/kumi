using System.Globalization;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.PlanningProposals;

/// <summary>
/// 週期與里程碑建議的檢查：每個問題一個細項（路徑 <c>changes[1].endDate</c>），一次回給模型，它才能一次修好。
/// 欄位的規則和 <c>PlanningService</c> 一樣（名稱長度、日期先後、週期最長天數、說明長度）。
/// </summary>
internal static class PlanningProposalChecker
{
    public static Result<IReadOnlyList<StoredPlanningChange>> Check(
        IReadOnlyList<PlanningChangeDraft>? drafts, IReadOnlyList<Cycle> cycles, IReadOnlyList<Milestone> milestones)
    {
        var list = drafts ?? [];
        var issues = new List<FailureDetail>();
        if (list.Count is 0 or > AiRules.MaxPlanningChangesPerProposal)
        {
            issues.Add(Issue("changes", $"一份建議要有 1 到 {AiRules.MaxPlanningChangesPerProposal} 筆變動。"));
        }

        var seen = new HashSet<(PlanningTarget, Guid)>();
        var checkedChanges = new List<StoredPlanningChange>();
        for (var i = 0; i < list.Count && i < AiRules.MaxPlanningChangesPerProposal; i++)
        {
            var path = $"changes[{i}]";
            if (list[i] is not { } draft)
            {
                issues.Add(Issue(path, "這一筆是空的。"));
                continue;
            }

            var change = CheckOne(draft, path, cycles, milestones, seen, issues);
            if (change is not null)
            {
                checkedChanges.Add(change);
            }
        }

        CheckLimits(list, cycles.Count, milestones.Count, issues);
        if (issues.Count == 0)
        {
            PlanningRuleSimulation.Check(checkedChanges, cycles, milestones, issues);
        }

        return issues.Count == 0
            ? checkedChanges
            : new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "週期與里程碑的建議不合規定，沒有建立。") { Details = issues };
    }

    internal static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);

    private static StoredPlanningChange? CheckOne(
        PlanningChangeDraft draft,
        string path,
        IReadOnlyList<Cycle> cycles,
        IReadOnlyList<Milestone> milestones,
        HashSet<(PlanningTarget, Guid)> seen,
        List<FailureDetail> issues)
    {
        var before = issues.Count;
        CheckWrongKind(draft, path, issues);
        if (draft.Action == PlanningAction.Create)
        {
            var created = CheckCreate(draft, path, issues);
            return issues.Count == before ? created : null;
        }

        var existing = FindExisting(draft, path, cycles, milestones, seen, issues);
        if (existing is null)
        {
            return null;
        }

        var result = draft.Action == PlanningAction.Delete
            ? CheckDelete(draft, path, existing.Value, issues)
            : CheckUpdate(draft, path, existing.Value, issues);
        return issues.Count == before ? result : null;
    }

    // 週期沒有到期日與說明，里程碑沒有開始與結束：給了就是模型搞混了，照實說。
    private static void CheckWrongKind(PlanningChangeDraft draft, string path, List<FailureDetail> issues)
    {
        if (draft.Target == PlanningTarget.Cycle)
        {
            AddIfGiven(draft.DueDate is not null, $"{path}.dueDate", "週期沒有到期日，只有 startDate 與 endDate。", issues);
            AddIfGiven(draft.Description is not null, $"{path}.description", "週期沒有說明；說明是里程碑的欄位。", issues);
            return;
        }

        AddIfGiven(draft.StartDate is not null, $"{path}.startDate", "里程碑沒有開始日，只有 dueDate。", issues);
        AddIfGiven(draft.EndDate is not null, $"{path}.endDate", "里程碑沒有結束日，只有 dueDate。", issues);
    }

    private static void AddIfGiven(bool given, string path, string message, List<FailureDetail> issues)
    {
        if (given)
        {
            issues.Add(Issue(path, message));
        }
    }

    private static StoredPlanningChange? CheckCreate(PlanningChangeDraft draft, string path, List<FailureDetail> issues)
    {
        if (draft.Id is not null)
        {
            issues.Add(Issue($"{path}.id", "新增不能給 id，id 是建立之後才有的。"));
        }

        var name = CheckName(draft.Name, draft.Target, $"{path}.name", issues);
        if (draft.Target == PlanningTarget.Cycle)
        {
            RequireDate(draft.StartDate, $"{path}.startDate", issues);
            RequireDate(draft.EndDate, $"{path}.endDate", issues);
            CheckSpan(draft.StartDate, draft.EndDate, $"{path}.endDate", issues);
            return name is null || draft.StartDate is not { } start || draft.EndDate is not { } end
                ? null
                : Create(draft, new PlanningValues(name, start, end, null, null));
        }

        RequireDate(draft.DueDate, $"{path}.dueDate", issues);
        var description = CheckDescription(draft.Description, $"{path}.description", issues);
        return name is null || draft.DueDate is not { } due
            ? null
            : Create(draft, new PlanningValues(name, null, null, due, description));
    }

    private static StoredPlanningChange Create(PlanningChangeDraft draft, PlanningValues after) =>
        new(draft.Target, PlanningAction.Create, null, null, after, null, null);

    private static (Guid Id, PlanningValues Values, int Total)? FindExisting(
        PlanningChangeDraft draft,
        string path,
        IReadOnlyList<Cycle> cycles,
        IReadOnlyList<Milestone> milestones,
        HashSet<(PlanningTarget, Guid)> seen,
        List<FailureDetail> issues)
    {
        if (draft.Id is not { } id)
        {
            issues.Add(Issue($"{path}.id", "修改與刪除要給 id（get_planning 或 get_project 回的週期、里程碑的 id）。"));
            return null;
        }

        if (!seen.Add((draft.Target, id)))
        {
            issues.Add(Issue($"{path}.id", "同一個週期或里程碑在一份建議裡只能出現一次。"));
            return null;
        }

        if (draft.Target == PlanningTarget.Cycle)
        {
            return cycles.FirstOrDefault(c => c.Id == id) is { } cycle
                ? (id, new PlanningValues(cycle.Name, cycle.StartDate, cycle.EndDate, null, null), cycle.Progress.Total)
                : NotFound(path, "週期", issues);
        }

        return milestones.FirstOrDefault(m => m.Id == id) is { } milestone
            ? (id, new PlanningValues(milestone.Name, null, null, milestone.DueDate, milestone.Description), milestone.Progress.Total)
            : NotFound(path, "里程碑", issues);
    }

    private static (Guid, PlanningValues, int)? NotFound(string path, string label, List<FailureDetail> issues)
    {
        issues.Add(Issue($"{path}.id", $"這個專案沒有這個{label}。"));
        return null;
    }

    private static StoredPlanningChange? CheckDelete(
        PlanningChangeDraft draft,
        string path,
        (Guid Id, PlanningValues Values, int Total) existing,
        List<FailureDetail> issues)
    {
        var hasContent = draft.Name is not null || draft.StartDate is not null || draft.EndDate is not null
            || draft.DueDate is not null || draft.Description is not null;
        if (hasContent)
        {
            issues.Add(Issue(path, "刪除只要給 target、action 與 id，不要帶其他欄位。"));
            return null;
        }

        return new StoredPlanningChange(draft.Target, PlanningAction.Delete, existing.Id, existing.Values, null, existing.Total, null);
    }

    private static StoredPlanningChange? CheckUpdate(
        PlanningChangeDraft draft, string path, (Guid Id, PlanningValues Values, int Total) existing, List<FailureDetail> issues)
    {
        var current = existing.Values;
        var name = draft.Name is null ? current.Name : CheckName(draft.Name, draft.Target, $"{path}.name", issues);
        PlanningValues? after = null;
        if (draft.Target == PlanningTarget.Cycle)
        {
            var start = draft.StartDate ?? current.StartDate;
            var end = draft.EndDate ?? current.EndDate;
            CheckSpan(start, end, $"{path}.endDate", issues);
            after = name is null ? null : new PlanningValues(name, start, end, null, null);
        }
        else
        {
            var description = draft.Description is null ? current.Description : CheckDescription(draft.Description, $"{path}.description", issues);
            after = name is null ? null : new PlanningValues(name, null, null, draft.DueDate ?? current.DueDate, description);
        }

        if (after is null || issues.Any(i => i.Path == path || i.Path.StartsWith(path + ".", StringComparison.Ordinal)))
        {
            return null;
        }

        if (after == current)
        {
            issues.Add(Issue(path, "這一筆沒有任何欄位和現在不同，不需要修改。"));
            return null;
        }

        return new StoredPlanningChange(draft.Target, PlanningAction.Update, existing.Id, current, after, null, null);
    }

    private static string? CheckName(string? raw, PlanningTarget target, string path, List<FailureDetail> issues)
    {
        var label = target == PlanningTarget.Cycle ? "週期" : "里程碑";
        var max = target == PlanningTarget.Cycle ? PlanningRules.MaxCycleNameLength : PlanningRules.MaxMilestoneNameLength;
        var name = raw?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            issues.Add(Issue(path, $"請填{label}的名稱。"));
            return null;
        }

        if (name.Length > max)
        {
            issues.Add(Issue(path, $"{label}的名稱最多 {max} 個字，現在有 {name.Length} 個字。"));
            return null;
        }

        if (!PlanningText.HasUnsafeNameCharacters(name))
        {
            return name;
        }

        issues.Add(Issue(path, $"{label}的名稱不能有換行、控制字元、看不見的字元或無效的字元。"));
        return null;
    }

    // 里程碑的說明：空字串是清掉，換行統一成 \n、去掉頭尾空白。
    private static string? CheckDescription(string? raw, string path, List<FailureDetail> issues)
    {
        var description = PlanningText.NormalizeMultiline(raw);
        if (description is null)
        {
            return null;
        }

        if (description.Length > PlanningRules.MaxDescriptionLength)
        {
            issues.Add(Issue(path, $"說明最多 {PlanningRules.MaxDescriptionLength} 個字，現在有 {description.Length} 個字。"));
        }
        else if (PlanningText.HasForbiddenCharacters(description, allowLineBreaks: true))
        {
            issues.Add(Issue(path, "說明不能有控制字元或無效的字元；換行與 Tab 可以。"));
        }

        return description;
    }

    private static void RequireDate(DateOnly? value, string path, List<FailureDetail> issues)
    {
        if (value is null)
        {
            issues.Add(Issue(path, "新增要給這個日期（yyyy-MM-dd）。"));
        }
    }

    private static void CheckSpan(DateOnly? start, DateOnly? end, string path, List<FailureDetail> issues)
    {
        if (start is not { } from || end is not { } to)
        {
            return;
        }

        if (to < from)
        {
            issues.Add(Issue(path, $"結束日不能早於開始日：開始日是 {Format(from)}，結束日是 {Format(to)}。"));
            return;
        }

        var days = to.DayNumber - from.DayNumber + 1;
        if (days > PlanningRules.MaxCycleDays)
        {
            issues.Add(Issue(path, $"一個週期最長 {PlanningRules.MaxCycleDays} 天（頭尾都算），現在有 {days} 天。"));
        }
    }

    private static void CheckLimits(IReadOnlyList<PlanningChangeDraft> list, int cycleCount, int milestoneCount, List<FailureDetail> issues)
    {
        var newCycles = list.Count(d => d is { Action: PlanningAction.Create, Target: PlanningTarget.Cycle });
        var newMilestones = list.Count(d => d is { Action: PlanningAction.Create, Target: PlanningTarget.Milestone });
        if (newCycles > 0 && cycleCount + newCycles > PlanningRules.MaxPerProject)
        {
            issues.Add(Issue("changes", $"一個專案最多 {PlanningRules.MaxPerProject} 個週期，請先刪掉用不到的。"));
        }

        if (newMilestones > 0 && milestoneCount + newMilestones > PlanningRules.MaxPerProject)
        {
            issues.Add(Issue("changes", $"一個專案最多 {PlanningRules.MaxPerProject} 個里程碑，請先刪掉用不到的。"));
        }
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
