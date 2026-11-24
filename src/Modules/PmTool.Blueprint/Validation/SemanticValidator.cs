using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation.Rules;

namespace PmTool.Blueprint.Validation;

/// <summary>
/// 結構檢查通過之後的語意檢查：引用是否存在、流程走不走得通等。規格見交接文件的工作包 C。
/// <para>
/// 回傳順序固定：規則依下面 <see cref="RulesFor"/> 的順序執行，每條規則內依文件順序。
/// 變更集是 baseVersion、藍圖（路徑加上 /blueprint）、搬移指示。
/// </para>
/// <para>
/// 所有規則都是延遲執行的序列，收集到 <see cref="ValidationReport.MaxIssues"/> + 1 個就停，後面的規則不會執行。
/// 多收的那一個讓 <see cref="ValidationReport.From"/> 知道有沒有截斷；停下來的位置固定，前面的問題與順序不受影響。
/// </para>
/// </summary>
internal static class SemanticValidator
{
    private const string BlueprintPathPrefix = "/blueprint";

    // 上限放在最前面：問題多到被截斷時，「數量超過上限」最不該被藏起來。其餘依規則編號。
    private static Func<ProjectBlueprint, BlueprintIndex, IEnumerable<ValidationIssue>>[] RulesFor(ValidationMode mode) =>
    [
        (blueprint, _) => LimitRules.Check(blueprint),
        (blueprint, _) => KeyRules.Check(blueprint),
        (blueprint, _) => FieldRules.CheckCoreNames(blueprint, mode),
        (blueprint, _) => FieldRules.CheckShapes(blueprint),
        (blueprint, _) => RoleRules.Check(blueprint),
        (blueprint, _) => WorkflowRules.CheckStates(blueprint),
        TransitionRules.CheckEndpoints,
        WorkflowRules.CheckReachability,
        TransitionRules.CheckRoles,
        TransitionRules.CheckRequiredFields,
        ItemTypeRules.Check,
        ViewRules.Check,
        AutomationRules.CheckTriggers,
        AutomationRules.CheckConditionsAndActions,
    ];

    public static IReadOnlyList<ValidationIssue> Validate(ProjectBlueprint blueprint, ValidationMode mode = ValidationMode.Authoring) =>
        Collect(CheckBlueprint(blueprint, BlueprintIndex.Build(blueprint), mode));

    public static IReadOnlyList<ValidationIssue> ValidateChangeSet(ChangeSetRequest changeSet, ValidationMode mode = ValidationMode.Authoring)
    {
        var index = BlueprintIndex.Build(changeSet.Blueprint);
        var issues = ChangeSetRules.CheckBaseVersion(changeSet)
            .Concat(CheckBlueprint(changeSet.Blueprint, index, mode)
                .Select(issue => issue with { Path = BlueprintPathPrefix + issue.Path }))
            .Concat(ChangeSetRules.CheckMigrations(changeSet, index));
        return Collect(issues);
    }

    private static IEnumerable<ValidationIssue> CheckBlueprint(ProjectBlueprint blueprint, BlueprintIndex index, ValidationMode mode) =>
        RulesFor(mode).SelectMany(rule => rule(blueprint, index));

    private static ValidationIssue[] Collect(IEnumerable<ValidationIssue> issues) =>
        issues.Take(ValidationReport.MaxIssues + 1).ToArray();
}
