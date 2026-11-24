using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Blueprint;

/// <summary>
/// 計畫的三個部分各自獨立：基準版本對不對、兩版藍圖差在哪裡、會動到既有資料的修改有沒有交代清楚。
/// 版本不對時差異照樣算出來，方便對照；但報告裡有問題，這份計畫就不能送去核准。
/// </summary>
public sealed class BlueprintPlanner : IBlueprintPlanner
{
    private const long NewProjectVersion = 0;

    public BlueprintPlan Plan(ProjectBlueprint? current, long currentVersion, ChangeSetRequest changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        // 還沒有藍圖的專案版本是 0，套用過一次之後版本從 1 起算。兩者對不上是呼叫端傳錯，不是變更集的問題。
        if (current is null ? currentVersion != NewProjectVersion : currentVersion <= NewProjectVersion)
        {
            throw new ArgumentException(
                $"沒有藍圖的專案版本必須是 {NewProjectVersion}，有藍圖的專案版本必須大於 {NewProjectVersion}；收到的版本是 {currentVersion}。",
                nameof(currentVersion));
        }

        var migrations = MigrationOutcome.Combine(
            StateMoveRules.Check(current, changeSet),
            FieldMigrationRules.Check(current, changeSet),
            FieldDetachRules.Check(current, changeSet),
            ApplyTimeCheckRules.Check(current, changeSet));
        return new BlueprintPlan(
            [.. BlueprintDiffer.Diff(current, changeSet.Blueprint)],
            migrations.Impacts,
            ValidationReport.From(CheckBaseVersion(currentVersion, changeSet).Concat(migrations.Issues)));
    }

    private static IEnumerable<ValidationIssue> CheckBaseVersion(long currentVersion, ChangeSetRequest changeSet)
    {
        if (changeSet.BaseVersion != currentVersion)
        {
            yield return new ValidationIssue(
                IssueCodes.PlanBaseVersionMismatch,
                "/baseVersion",
                $"這份變更集是照第 {changeSet.BaseVersion} 版寫的，但目前是第 {currentVersion} 版；請依目前的版本重新產生。");
        }
    }
}
