using PmTool.Work.Planning;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Templates;

/// <summary>
/// 範例週期與里程碑的內容檢查：參照不重複、名稱與日期用一般新增時的規則、名稱不重複、週期不重疊。
/// 重複與重疊都算在檔案裡排在後面的那一筆。
/// </summary>
internal static class ExamplePlanningChecker
{
    public static IReadOnlyList<string> Check(
        string templateKey, IReadOnlyList<ExampleCycle> cycles, IReadOnlyList<ExampleMilestone> milestones, DateOnly today)
    {
        var problems = new List<string>();
        for (var index = 0; index < cycles.Count; index++)
        {
            var cycle = cycles[index];
            void Add(string path, string message) => problems.Add(ExampleProblems.Cycle(templateKey, cycle.Position, path, message));
            var earlier = cycles.Take(index).ToArray();
            CheckCycle(cycle, earlier, today, Add);
        }

        for (var index = 0; index < milestones.Count; index++)
        {
            var milestone = milestones[index];
            void Add(string path, string message) => problems.Add(ExampleProblems.Milestone(templateKey, milestone.Position, path, message));
            CheckMilestone(milestone, milestones.Take(index).ToArray(), today, Add);
        }

        return problems;
    }

    private static void CheckCycle(ExampleCycle cycle, ExampleCycle[] earlier, DateOnly today, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(cycle.Ref))
        {
            add("ref", "ref 不能是空的。");
        }
        else if (earlier.Any(other => other.Ref == cycle.Ref))
        {
            add("ref", $"ref {Quote(cycle.Ref)}已經用過了，同一個檔案裡不能重複。");
        }

        var resolved = cycle.Resolve(today);
        foreach (var problem in PlanningChecks.CycleProblems(resolved.Name, resolved.StartDate, resolved.EndDate))
        {
            add(ExamplePath(problem.Path), problem.Message);
        }

        if (earlier.Any(other => SameName(other.Name, cycle.Name)))
        {
            add("name", $"週期名稱{Quote(resolved.Name)}已經用過了（不分大小寫）。");
        }

        if (earlier.FirstOrDefault(other => Overlaps(other.Resolve(today), resolved)) is { } overlapped)
        {
            add("startInDays", $"日期和週期{Quote(overlapped.Name)}重疊了；同一個專案的週期日期不能重疊。");
        }
    }

    private static void CheckMilestone(
        ExampleMilestone milestone, ExampleMilestone[] earlier, DateOnly today, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(milestone.Ref))
        {
            add("ref", "ref 不能是空的。");
        }
        else if (earlier.Any(other => other.Ref == milestone.Ref))
        {
            add("ref", $"ref {Quote(milestone.Ref)}已經用過了，同一個檔案裡不能重複。");
        }

        var resolved = milestone.Resolve(today);
        foreach (var problem in PlanningChecks.MilestoneProblems(resolved.Name, resolved.Description))
        {
            add(problem.Path, problem.Message);
        }

        if (earlier.Any(other => SameName(other.Name, milestone.Name)))
        {
            add("name", $"里程碑名稱{Quote(resolved.Name)}已經用過了（不分大小寫）。");
        }
    }

    // 範例檔用相對天數，不叫 startDate、endDate。
    private static string ExamplePath(string path) => path switch
    {
        PlanningChecks.StartDatePath => "startInDays",
        PlanningChecks.EndDatePath => "endInDays",
        _ => path,
    };

    // 和資料庫的 lower(name) 同一套：OrdinalIgnoreCase 看的是大寫，會把 ς 與 σ 當成同一個，資料庫卻不會。
    private static bool SameName(string left, string right) =>
        string.Equals(left.Trim().ToLowerInvariant(), right.Trim().ToLowerInvariant(), StringComparison.Ordinal);

    private static bool Overlaps(ResolvedCycle left, ResolvedCycle right) =>
        left.StartDate <= right.EndDate && left.EndDate >= right.StartDate;
}
