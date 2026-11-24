using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>規則 14：變更集本身。藍圖裡的問題由藍圖的規則負責，這裡只看 baseVersion 與搬移指示。</summary>
internal static class ChangeSetRules
{
    public static IEnumerable<ValidationIssue> CheckBaseVersion(ChangeSetRequest changeSet)
    {
        if (changeSet.BaseVersion < 0)
        {
            yield return new ValidationIssue(
                IssueCodes.ChangeSetInvalidBaseVersion,
                "/baseVersion",
                $"baseVersion 不能是負數，收到的是 {changeSet.BaseVersion}；新專案填 0。");
        }
    }

    /// <summary>依 Schema 的屬性順序：moveStates、dropFields、convertFields，各依列出的順序。</summary>
    public static IEnumerable<ValidationIssue> CheckMigrations(ChangeSetRequest changeSet, BlueprintIndex index)
    {
        var migrations = changeSet.Migrations ?? new MigrationHints();
        var moves = migrations.MoveStates.SelectMany((move, m) => Move(move, $"/migrations/moveStates/{m}", index));
        foreach (var issue in moves.Concat(DropFields(migrations, index)).Concat(ConvertFields(migrations, index)))
        {
            yield return issue;
        }
    }

    // 狀態的 key 只在流程裡唯一，所以起點與終點都對照指定的那個流程（重複的流程 key 取第一個）。
    // 流程不存在時沒有東西可以對照，只回報流程這一件。起點先於終點，和 Schema 的屬性順序一致。
    private static IEnumerable<ValidationIssue> Move(StateMove move, string path, BlueprintIndex index)
    {
        if (index.StatesOfWorkflow(move.Workflow) is not { } states)
        {
            yield return new ValidationIssue(
                IssueCodes.ChangeSetMoveUnknownWorkflow,
                $"{path}/workflow",
                $"搬移指示的流程{Quote(move.Workflow)}不在新的藍圖裡。");
            yield break;
        }

        if (states.Contains(move.From))
        {
            yield return new ValidationIssue(
                IssueCodes.ChangeSetMoveSourceStillExists,
                $"{path}/from",
                $"狀態{Quote(move.From)}還在新藍圖的流程{Quote(move.Workflow)}裡，不需要搬移；只有被刪除的狀態才要寫搬移指示。");
        }

        if (!states.Contains(move.To))
        {
            yield return new ValidationIssue(
                IssueCodes.ChangeSetMoveUnknownTarget,
                $"{path}/to",
                $"搬移的目的狀態{Quote(move.To)}不在新藍圖的流程{Quote(move.Workflow)}裡。");
        }
    }

    // 核心欄位永遠存在，不可能被刪除，所以和還在的自訂欄位一樣回報。
    private static IEnumerable<ValidationIssue> DropFields(MigrationHints migrations, BlueprintIndex index)
    {
        for (var d = 0; d < migrations.DropFields.Count; d++)
        {
            var field = migrations.DropFields[d];
            if (index.IsField(field))
            {
                yield return new ValidationIssue(
                    IssueCodes.ChangeSetDropFieldStillExists,
                    $"/migrations/dropFields/{d}",
                    $"欄位{Quote(field)}還在新的藍圖裡，不能封存；只有被刪除的欄位才列在 dropFields。");
            }
        }
    }

    // 核心欄位的型別不能改，只有新藍圖裡的自訂欄位才談得上轉換。
    private static IEnumerable<ValidationIssue> ConvertFields(MigrationHints migrations, BlueprintIndex index)
    {
        for (var c = 0; c < migrations.ConvertFields.Count; c++)
        {
            var field = migrations.ConvertFields[c].Field;
            if (!index.Fields.ContainsKey(field))
            {
                yield return new ValidationIssue(
                    IssueCodes.ChangeSetConvertUnknownField,
                    $"/migrations/convertFields/{c}/field",
                    $"要轉換型別的欄位{Quote(field)}不是新藍圖裡的自訂欄位。");
            }
        }
    }
}
