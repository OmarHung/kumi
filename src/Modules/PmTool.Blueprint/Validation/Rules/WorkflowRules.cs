using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>規則 5 與 7：流程的狀態清單與走不走得通。依流程順序。</summary>
internal static class WorkflowRules
{
    /// <summary>規則 5。沒有狀態時只回報這一件，「沒有完成類狀態」此時沒有意義。</summary>
    public static IEnumerable<ValidationIssue> CheckStates(ProjectBlueprint blueprint)
    {
        for (var i = 0; i < blueprint.Workflows.Count; i++)
        {
            var workflow = blueprint.Workflows[i];
            var path = $"/workflows/{i}/states";
            if (workflow.States.Count == 0)
            {
                yield return new ValidationIssue(
                    IssueCodes.WorkflowNoStates, path, $"流程{Quote(workflow.Key)}至少要有一個狀態。");
            }
            else if (!workflow.States.Any(s => s.Category == StateCategory.Done))
            {
                yield return new ValidationIssue(
                    IssueCodes.WorkflowNoDoneState, path, $"流程{Quote(workflow.Key)}至少要有一個類別是 done 的狀態。");
            }
        }
    }

    /// <summary>
    /// 規則 7。只在有列轉換時檢查：沒列（null）表示任何狀態之間都能互轉，自然都走得到；
    /// 列了空清單表示完全不能轉。每個狀態依序先看走不走得到、再看有沒有出路。
    /// </summary>
    public static IEnumerable<ValidationIssue> CheckReachability(ProjectBlueprint blueprint, BlueprintIndex index)
    {
        for (var i = 0; i < blueprint.Workflows.Count; i++)
        {
            var workflow = blueprint.Workflows[i];
            if (workflow.Transitions is null || workflow.States.Count == 0)
            {
                continue;
            }

            foreach (var issue in Reachability(workflow, i, index.StatesOfWorkflowAt(i)))
            {
                yield return issue;
            }
        }
    }

    private static IEnumerable<ValidationIssue> Reachability(Workflow workflow, int position, IReadOnlySet<string> states)
    {
        var graph = StateGraph.Build(workflow.Transitions!, states);
        var reachable = graph.ReachableFrom(workflow.States[0].Key);

        // 可達性依 key 判斷，重複 key 的狀態結果和第一個一樣；重複本身由規則 1 回報，這裡不重複回報。
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var s = 0; s < workflow.States.Count; s++)
        {
            var state = workflow.States[s];
            if (!visited.Add(state.Key))
            {
                continue;
            }

            var path = $"/workflows/{position}/states/{s}";
            if (!reachable.Contains(state.Key))
            {
                yield return new ValidationIssue(
                    IssueCodes.WorkflowUnreachableState,
                    path,
                    $"流程{Quote(workflow.Key)}的狀態{Quote(state.Key)}無法從起始狀態{Quote(workflow.States[0].Key)}走到。");
            }

            if (!state.IsTerminal && !graph.HasExit(state.Key))
            {
                yield return new ValidationIssue(
                    IssueCodes.WorkflowDeadEndState,
                    path,
                    $"流程{Quote(workflow.Key)}的狀態{Quote(state.Key)}不是結束類，卻沒有任何轉換可以離開。");
            }
        }
    }
}
