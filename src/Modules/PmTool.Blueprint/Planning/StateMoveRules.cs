using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Planning;

/// <summary>
/// 護欄：狀態被拿掉時，必須寫明停在那個狀態的項目搬去哪裡。
/// 以類型為單位來看：項目屬於類型，類型決定用哪個流程。某個類型原本的流程有狀態 S，
/// 它在新藍圖用的流程沒有 S，停在 S 的項目就沒有地方待了。這樣類型換流程的情況也涵蓋得到；
/// 沒有類型在用的流程底下不會有項目，新增的類型也不會，所以都不需要搬。
/// </summary>
internal static class StateMoveRules
{
    private const string HintsPath = "/migrations/moveStates";

    public static MigrationOutcome Check(ProjectBlueprint? current, ChangeSetRequest changeSet)
    {
        var hints = changeSet.Migrations?.MoveStates ?? [];
        var next = changeSet.Blueprint;
        var removed = current is null ? [] : RemovedStates(current, next);
        var required = current is null ? [] : RequiredMoves(current, next).ToArray();
        var stateNames = required.ToDictionary(move => (move.Workflow, move.From), move => move.FromName);
        var nextIndex = BlueprintIndex.Build(next);
        return HintMatcher.Match(
            required.Select(move => (move.Workflow, move.From)),
            hints,
            hint => (hint.Workflow, hint.From),
            (move, hint) => new StateMoveImpact(move.Workflow, move.From, hint.To),
            move => Missing(
                QuoteName(next.Workflows[nextIndex.WorkflowPositions[move.Workflow]].Name, move.Workflow),
                QuoteName(stateNames[move], move.From)),
            (index, repeated) =>
                // 狀態真的從這個流程拿掉了，只是剛好沒有既有的類型在用它：指示沒有寫錯，只是沒有項目要搬。
                !repeated && removed.Contains((hints[index].Workflow, hints[index].From)) ? null : Unused(index, hints[index]));
    }

    /// <summary>
    /// 依新藍圖的類型順序、再依目前流程的狀態順序。同一個（流程, 狀態）只算一次。
    /// 狀態的名稱取目前的流程裡的：它不在新的流程裡了，使用者認得的是原本的名稱。
    /// </summary>
    private static IEnumerable<(string Workflow, string From, string FromName)> RequiredMoves(ProjectBlueprint current, ProjectBlueprint next)
    {
        var currentIndex = BlueprintIndex.Build(current);
        var nextIndex = BlueprintIndex.Build(next);
        var seen = new HashSet<(string Workflow, string From)>();
        foreach (var type in next.ItemTypes)
        {
            // 對不到的引用（類型是新的、流程不存在）不會有需要搬的項目；流程不存在這種錯由驗證負責。
            if (StatesOfItemType(current, currentIndex, type.Key) is not { } before
                || nextIndex.StatesOfWorkflow(type.Workflow) is not { } remaining)
            {
                continue;
            }

            foreach (var state in before.Where(state => !remaining.Contains(state.Key) && seen.Add((type.Workflow, state.Key))))
            {
                yield return (type.Workflow, state.Key, state.Name);
            }
        }
    }

    /// <summary>這個類型在藍圖裡用的流程有哪些狀態，依流程裡的順序。類型或它的流程不存在時是 null。</summary>
    private static IReadOnlyList<WorkflowState>? StatesOfItemType(ProjectBlueprint blueprint, BlueprintIndex index, string itemType) =>
        index.ItemTypePositions.TryGetValue(itemType, out var typePosition)
        && index.WorkflowPositions.TryGetValue(blueprint.ItemTypes[typePosition].Workflow, out var workflowPosition)
            ? blueprint.Workflows[workflowPosition].States
            : null;

    /// <summary>兩版都有的流程裡，從流程拿掉的狀態。用來分辨「沒有項目要搬的指示」和「寫錯的指示」。</summary>
    private static HashSet<(string Workflow, string State)> RemovedStates(ProjectBlueprint current, ProjectBlueprint next)
    {
        var nextIndex = BlueprintIndex.Build(next);
        var removed = new HashSet<(string Workflow, string State)>();
        foreach (var workflow in current.Workflows)
        {
            if (nextIndex.StatesOfWorkflow(workflow.Key) is { } remaining)
            {
                removed.UnionWith(workflow.States.Where(state => !remaining.Contains(state.Key)).Select(state => (workflow.Key, state.Key)));
            }
        }

        return removed;
    }

    /// <param name="workflow">已經加上引號的流程名稱。</param>
    /// <param name="from">已經加上引號的狀態名稱。</param>
    private static ValidationIssue Missing(string workflow, string from) =>
        new(
            IssueCodes.PlanStateMoveRequired,
            HintsPath,
            $"狀態{from}不在新藍圖的流程{workflow}裡了，但沒有寫明停在這個狀態的項目要搬去哪裡；"
            + "請在 migrations.moveStates 為這個流程與狀態加一條搬移指示。");

    private static ValidationIssue Unused(int index, StateMove hint) =>
        new(
            IssueCodes.PlanUnusedMigration,
            $"{HintsPath}/{index}",
            $"這條搬移指示對不到任何修改：狀態{Quote(hint.From)}不是從流程{Quote(hint.Workflow)}拿掉的狀態，"
            + "也沒有既有的類型需要把項目從它搬走。可能是 key 寫錯，或前面已經有一條相同的指示。");
}
