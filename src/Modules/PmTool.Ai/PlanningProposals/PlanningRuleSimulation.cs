using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.PlanningProposals;

/// <summary>
/// 照 changes 的順序模擬一次（刪除的拿掉、修改的換掉、新增的加進去），每一步套用工作引擎同一套規則：
/// 名稱不分大小寫不能重複（週期與里程碑各自算）、週期的日期含頭尾不能重疊（<c>PlanningService.RuleBreakAsync</c>）。
/// 建立建議時就擋，使用者按「套用」才不會失敗、還部分套用。呼叫前每一筆的欄位都已經檢查過，索引和 <c>changes</c> 一一對應。
/// </summary>
internal static class PlanningRuleSimulation
{
    private sealed record Entry(Guid? Id, string Name, DateOnly? Start, DateOnly? End);

    public static void Check(
        IReadOnlyList<StoredPlanningChange> changes, IReadOnlyList<Cycle> cycles, IReadOnlyList<Milestone> milestones, List<FailureDetail> issues)
    {
        var cycleState = cycles.Select(c => new Entry(c.Id, c.Name, c.StartDate, c.EndDate)).ToList();
        var milestoneState = milestones.Select(m => new Entry(m.Id, m.Name, null, null)).ToList();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            var state = change.Target == PlanningTarget.Cycle ? cycleState : milestoneState;
            if (change.Action == PlanningAction.Delete)
            {
                state.RemoveAll(e => e.Id == change.Id);
                continue;
            }

            var after = change.After!;
            var others = state.Where(e => e.Id != change.Id || change.Id is null).ToList();
            if (CheckOne(change.Target, after, others, $"changes[{i}]", issues))
            {
                state.RemoveAll(e => e.Id == change.Id && change.Id is not null);
                state.Add(new Entry(change.Id ?? Guid.NewGuid(), after.Name, after.StartDate, after.EndDate));
            }
        }
    }

    private static bool CheckOne(PlanningTarget target, PlanningValues after, List<Entry> others, string path, List<FailureDetail> issues)
    {
        var label = target == PlanningTarget.Cycle ? "週期" : "里程碑";
        if (others.Any(e => string.Equals(e.Name, after.Name, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(PlanningProposalChecker.Issue($"{path}.name", $"這個專案已經有一個叫「{Safe(after.Name)}」的{label}了（不分大小寫），請換一個名稱。"));
            return false;
        }

        if (target != PlanningTarget.Cycle)
        {
            return true;
        }

        var overlapping = others
            .Where(e => e.Start <= after.EndDate && e.End >= after.StartDate)
            .OrderBy(e => e.Start)
            .FirstOrDefault();
        if (overlapping is null)
        {
            return true;
        }

        issues.Add(PlanningProposalChecker.Issue(
            $"{path}.startDate",
            $"日期和週期「{Safe(overlapping.Name)}」（{overlapping.Start:yyyy-MM-dd} 到 {overlapping.End:yyyy-MM-dd}）重疊了；同一個專案的週期日期不能重疊。"));
        return false;
    }

    private static string Safe(string name) => name.Replace('「', '『').Replace('」', '』');
}
