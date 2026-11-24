using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Blueprint.Tests.Integration;

namespace PmTool.Blueprint.Tests.Planning;

/// <summary>計畫的測試都從 BlueprintPlanner.Plan 這個對外入口進去；這裡只是少打一些字。</summary>
internal static class PlanTestSupport
{
    public const long CurrentVersion = 12;

    public static readonly BlueprintPlanner Planner = new();

    public static ChangeSetRequest ChangeSetFor(
        ProjectBlueprint next, MigrationHints? migrations = null, long baseVersion = CurrentVersion) =>
        new() { BaseVersion = baseVersion, Blueprint = next, Migrations = migrations };

    public static BlueprintPlan PlanFrom(ProjectBlueprint current, ProjectBlueprint next, MigrationHints? migrations = null) =>
        Planner.Plan(current, CurrentVersion, ChangeSetFor(next, migrations));

    /// <summary>
    /// split-acceptance 變更集的基準：「官網改版」的第 12 版，只有一個 qa 狀態，還沒拆成內部驗收與客戶驗收。
    /// 直接讀範例檔，同一份藍圖才不會有兩個可能不一致的來源。
    /// </summary>
    public static ProjectBlueprint WebsiteVersion12()
    {
        var result = BlueprintValidator.CreateDefault().ValidateBlueprint(
            ExampleDocuments.ReadYaml(ExampleDocuments.CurrentBlueprintFile), DocumentFormat.Yaml);
        Assert.That(result.Report.Issues, Is.Empty, $"{ExampleDocuments.CurrentBlueprintFile} 本身要通過驗證");
        return result.Blueprint!;
    }

    public static MigrationHints Moves(params (string Workflow, string From, string To)[] moves) =>
        new() { MoveStates = [.. moves.Select(move => new StateMove { Workflow = move.Workflow, From = move.From, To = move.To })] };

    public static MigrationHints Drops(params string[] fields) => new() { DropFields = fields };

    public static MigrationHints Converts(params (string Field, ConversionFailurePolicy OnFailure)[] conversions) =>
        new()
        {
            ConvertFields = [.. conversions.Select(c => new FieldConversion { Field = c.Field, OnFailure = c.OnFailure })],
        };

    /// <summary>把差異寫成一行一個的文字，例如「Modified State delivery/closed [name]」，斷言才好讀。</summary>
    public static string[] Describe(this IEnumerable<BlueprintChange> changes) => [.. changes.Select(Describe)];

    /// <summary>同上，後面加上位置：新增與修改是新藍圖裡的位置（at），移除是目前藍圖裡的位置（from）。</summary>
    public static string[] DescribeWithPaths(this IEnumerable<BlueprintChange> changes) =>
        [.. changes.Select(change =>
            Describe(change) + (change.Kind == ChangeKind.Removed ? $" from {change.CurrentPath}" : $" at {change.NewPath}"))];

    /// <summary>整份計畫的每一個細節，用來比較兩次算出來的結果是不是一模一樣。</summary>
    public static string Print(this BlueprintPlan plan) =>
        string.Join('\n', plan.Changes.Select(change => $"{Describe(change)} {change.CurrentPath} -> {change.NewPath}")
            .Concat(plan.DataImpacts.Select(impact => impact.ToString()))
            .Concat(plan.Report.Issues.Select(issue => $"{issue.Code} {issue.Path} {issue.Message}")));

    private static string Describe(BlueprintChange change)
    {
        var key = change.ParentKey is null ? change.Key : $"{change.ParentKey}/{change.Key}";
        var properties = change.ChangedProperties.Count == 0 ? string.Empty : $" [{string.Join(",", change.ChangedProperties)}]";
        return $"{change.Kind} {change.Resource} {key}{properties}";
    }
}
