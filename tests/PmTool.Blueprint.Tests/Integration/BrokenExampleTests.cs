using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Tests.Integration;

/// <summary>
/// 交接文件第 4 節列的 12 個改壞案例。每個案例從範例檔改一處，用 JSON 與 YAML 各送一次：
/// 兩種格式要回報完全相同的問題，而且有問題時不回傳模型。
/// 預期值列的是完整的問題清單，不只是表格裡的那一個代碼，多冒出來的問題也會讓測試失敗。
/// </summary>
[TestFixture]
public sealed class BrokenExampleTests
{
    private static readonly BlueprintValidator Validator = BlueprintValidator.CreateDefault();

    private static IEnumerable<TestCaseData> StructurallyBrokenBlueprints()
    {
        yield return Case(
            "case 01: first field gets an extra colour property",
            tree => tree["fields"]![0]!["colour"] = "red",
            (IssueCodes.StructureAdditionalProperty, "/fields/0/colour"));

        yield return Case(
            "case 02: a state category becomes waiting",
            tree => tree["workflows"]![0]!["states"]![0]!["category"] = "waiting",
            (IssueCodes.StructureEnum, "/workflows/0/states/0/category"));

        yield return Case(
            "case 03: first item type loses its workflow",
            tree => tree["itemTypes"]![0]!.AsObject().Remove("workflow"),
            (IssueCodes.StructureRequired, "/itemTypes/0"));

        yield return Case(
            "case 04: apiVersion becomes blueprint/v9",
            tree => tree["apiVersion"] = "blueprint/v9",
            (IssueCodes.StructureConst, "/apiVersion"));

    }

    private static IEnumerable<TestCaseData> SemanticallyBrokenBlueprints()
    {
        yield return Case(
            "case 07: change_request no longer has client_signoff_date",
            tree =>
            {
                var fields = ByKey(tree["itemTypes"], "change_request")["fields"]!.AsArray();
                fields.Remove(fields.Single(field => field!.GetValue<string>() == "client_signoff_date"));
            },
            (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/7/requireFields/0"));

        yield return Case(
            "case 08: delivery keeps only its first five transitions",
            tree =>
            {
                var transitions = ByKey(tree["workflows"], "delivery")["transitions"]!.AsArray();
                while (transitions.Count > 5)
                {
                    transitions.RemoveAt(transitions.Count - 1);
                }
            },
            // client_qa（第 5 個）走不到、也沒有出路；closed（第 6 個）走不到，但它是結束類，不需要出路。
            (IssueCodes.WorkflowUnreachableState, "/workflows/0/states/5"),
            (IssueCodes.WorkflowDeadEndState, "/workflows/0/states/5"),
            (IssueCodes.WorkflowUnreachableState, "/workflows/0/states/6"));

        yield return Case(
            "case 09: project_manager role is removed",
            tree =>
            {
                var roles = tree["roles"]!.AsArray();
                roles.Remove(ByKey(roles, "project_manager"));
            },
            (IssueCodes.TransitionUnknownRole, "/workflows/0/transitions/5/allowedRoles/0"),
            (IssueCodes.TransitionUnknownRole, "/workflows/0/transitions/7/allowedRoles/0"),
            (IssueCodes.ViewUnknownRole, "/views/2/visibleTo/1"));

        yield return Case(
            "case 10: bindings becomes an empty list",
            tree => tree["bindings"] = new JsonArray(),
            (IssueCodes.AutomationMissingBinding, "/automations/0/trigger/source"));

        yield return Case(
            "case 11: a custom field is named due_date",
            tree => tree["fields"]!.AsArray().Add(new JsonObject { ["key"] = "due_date", ["name"] = "到期日", ["type"] = "date" }),
            (IssueCodes.FieldCoreNameConflict, "/fields/4/key"));
    }

    private static IEnumerable<TestCaseData> BrokenChangeSets()
    {
        yield return Case(
            "case 05: baseVersion is removed",
            tree => tree.Remove("baseVersion"),
            (IssueCodes.StructureRequired, string.Empty));

        yield return Case(
            "case 06: a state category inside the blueprint becomes waiting",
            tree => tree["blueprint"]!["workflows"]![0]!["states"]![0]!["category"] = "waiting",
            (IssueCodes.StructureEnum, "/blueprint/workflows/0/states/0/category"));

        yield return Case(
            "case 12: the migration target becomes qa",
            tree => tree["migrations"]!["moveStates"]![0]!["to"] = "qa",
            (IssueCodes.ChangeSetMoveUnknownTarget, "/migrations/moveStates/0/to"));
    }

    [TestCaseSource(nameof(StructurallyBrokenBlueprints))]
    [TestCaseSource(nameof(SemanticallyBrokenBlueprints))]
    public void ValidateBlueprint_BrokenExample_ReportsTheSameIssuesInBothFormats(
        Action<JsonObject> breakIt, (string Code, string Path)[] expected)
    {
        // Arrange
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.BlueprintFile);
        breakIt(tree);

        // Act
        var fromJson = Validator.ValidateBlueprint(ExampleDocuments.ToJson(tree), DocumentFormat.Json);
        var fromYaml = Validator.ValidateBlueprint(ExampleDocuments.ToYaml(tree), DocumentFormat.Yaml);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CodesAndPaths(fromJson.Report), Is.EqualTo(expected));
            Assert.That(fromYaml.Report.Issues, Is.EqualTo(fromJson.Report.Issues));
            Assert.That(fromJson.Report.IsValid, Is.False);
            Assert.That(fromJson.Blueprint, Is.Null);
            Assert.That(fromYaml.Blueprint, Is.Null);
        }
    }

    [TestCaseSource(nameof(BrokenChangeSets))]
    public void ValidateChangeSet_BrokenExample_ReportsTheSameIssuesInBothFormats(
        Action<JsonObject> breakIt, (string Code, string Path)[] expected)
    {
        // Arrange
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.ChangeSetFile);
        breakIt(tree);

        // Act
        var fromJson = Validator.ValidateChangeSet(ExampleDocuments.ToJson(tree), DocumentFormat.Json);
        var fromYaml = Validator.ValidateChangeSet(ExampleDocuments.ToYaml(tree), DocumentFormat.Yaml);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CodesAndPaths(fromJson.Report), Is.EqualTo(expected));
            Assert.That(fromYaml.Report.Issues, Is.EqualTo(fromJson.Report.Issues));
            Assert.That(fromJson.Report.IsValid, Is.False);
            Assert.That(fromJson.ChangeSet, Is.Null);
            Assert.That(fromYaml.ChangeSet, Is.Null);
        }
    }

    [Test]
    public void BrokenExamples_CoverAllTwelveCasesOfTheHandoff()
    {
        // Assert：案例是用產生器列出來的，少列一個不會有任何測試失敗，所以在這裡數一次。
        Assert.That(
            StructurallyBrokenBlueprints().Count() + SemanticallyBrokenBlueprints().Count() + BrokenChangeSets().Count(),
            Is.EqualTo(12));
    }

    private static TestCaseData Case(string description, Action<JsonObject> breakIt, params (string Code, string Path)[] expected) =>
        new TestCaseData(breakIt, expected).SetArgDisplayNames(description);

    private static JsonObject ByKey(JsonNode? list, string key) =>
        list!.AsArray().Single(item => item!["key"]!.GetValue<string>() == key)!.AsObject();

    private static (string Code, string Path)[] CodesAndPaths(ValidationReport report) =>
        [.. report.Issues.Select(issue => (issue.Code, issue.Path))];
}
