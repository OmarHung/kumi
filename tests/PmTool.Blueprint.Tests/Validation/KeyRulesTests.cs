using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class KeyRulesTests
{
    private static readonly string[] MalformedKeys =
    [
        "", "a", "1abc", "_abc", "Abc", "abC", "ab-c", "ab c", "ab.c", "abé", "ａｂｃ",
        "abc\n", "abc\r\n", "\nabc", " abc", "abc ", new string('a', 41),
    ];

    private static readonly string[] WellFormedKeys = ["ab", "a1", "a_", "a_b_c", "quote_amount", new string('a', 40)];

    private static IEnumerable<TestCaseData> MalformedKeyInEachScope()
    {
        yield return Case("metadata", b => b with { Metadata = b.Metadata with { Key = "Bad" } }, "/metadata/key");
        yield return Case(
            "template",
            b => b with { Metadata = b.Metadata with { BasedOn = b.Metadata.BasedOn! with { Template = "Bad" } } },
            "/metadata/basedOn/template");
        yield return Case("field", b => b with { Fields = [.. b.Fields, TextField("Bad")] }, "/fields/4/key");
        yield return Case(
            "option",
            b => b.WithField(Impact, f => f with { Options = f.Options!.ReplaceAt(0, Option("Bad")) }),
            "/fields/3/options/0/key");
        yield return Case("workflow", b => b with { Workflows = [.. b.Workflows, SimpleWorkflow("Bad")] }, "/workflows/2/key");
        yield return Case(
            "state",
            b => b.WithWorkflow(TaskFlow, w => w with { States = w.States.ReplaceAt(1, State("Bad", StateCategory.InProgress)) }),
            "/workflows/1/states/1/key");
        yield return Case("item type", b => b.WithItemType(TaskType, t => t with { Key = "Bad" }), "/itemTypes/2/key");
        yield return Case("view", b => b.WithView(ProgressBoard, v => v with { Key = "Bad" }), "/views/0/key");
        yield return Case("automation", b => b.WithAutomation(0, a => a with { Key = "Bad" }), "/automations/0/key");
        yield return Case("role", b => b with { Roles = [.. b.Roles, Role("Bad")] }, "/roles/2/key");
    }

    private static IEnumerable<TestCaseData> DuplicatedKeyInEachScope()
    {
        yield return Case("field", b => b with { Fields = [.. b.Fields, TextField("change_reason")] }, "/fields/4/key");
        yield return Case(
            "option",
            b => b.WithField(Impact, f => f with { Options = [.. f.Options!, Option("low")] }),
            "/fields/3/options/3/key");
        yield return Case("workflow", b => b with { Workflows = [.. b.Workflows, SimpleWorkflow("task_flow")] }, "/workflows/2/key");
        yield return Case(
            "state",
            b => b.WithWorkflow(TaskFlow, w => w with { States = [.. w.States, State("doing", StateCategory.InProgress)] }),
            "/workflows/1/states/3/key");
        yield return Case("item type", b => b with { ItemTypes = [.. b.ItemTypes, ItemTypeOf("task", "task_flow")] }, "/itemTypes/3/key");
        yield return Case("view", b => b with { Views = [.. b.Views, b.Views[0]] }, "/views/3/key");
        yield return Case("automation", b => b with { Automations = [.. b.Automations, b.Automations[0]] }, "/automations/1/key");
        yield return Case("role", b => b with { Roles = [.. b.Roles, b.Roles[1]] }, "/roles/2/key");
    }

    [TestCaseSource(nameof(MalformedKeys))]
    public void Validate_MalformedMetadataKey_ReportsKeyFormat(string key)
    {
        // Arrange
        var blueprint = Agency() with { Metadata = Agency().Metadata with { Key = key } };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.KeyFormat, "/metadata/key") }));
    }

    [TestCaseSource(nameof(WellFormedKeys))]
    public void Validate_WellFormedMetadataKey_ReportsNoIssues(string key)
    {
        // Arrange
        var blueprint = Agency() with { Metadata = Agency().Metadata with { Key = key } };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [TestCaseSource(nameof(MalformedKeyInEachScope))]
    public void Validate_MalformedKeyInAnyScope_ReportsKeyFormatAtThatKey(
        Func<ProjectBlueprint, ProjectBlueprint> breakIt, string expectedPath)
    {
        // Arrange
        var blueprint = breakIt(Agency());

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.KeyFormat, expectedPath) }));
    }

    [TestCaseSource(nameof(DuplicatedKeyInEachScope))]
    public void Validate_DuplicatedKeyInAnyScope_ReportsKeyDuplicateAtTheLaterOne(
        Func<ProjectBlueprint, ProjectBlueprint> breakIt, string expectedPath)
    {
        // Arrange
        var blueprint = breakIt(Agency());

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.KeyDuplicate, expectedPath) }));
    }

    [Test]
    public void Validate_SameOptionKeyInTwoFields_ReportsNoIssues()
    {
        // Arrange：選項的 key 只在同一個欄位裡不能重複。
        var tags = new FieldDefinition { Key = "tags", Name = "標籤", Type = FieldType.MultiSelect, Options = [Option("low")] };
        var blueprint = Agency() with { Fields = [.. Agency().Fields, tags] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_SameStateKeyInTwoWorkflows_ReportsNoIssues()
    {
        // Arrange：狀態的 key 只在同一個流程裡不能重複；closed 也在 delivery 裡。
        var blueprint = Agency().WithWorkflow(
            TaskFlow, w => w with { States = [.. w.States, State("closed", StateCategory.Done)] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_KeyThatIsMalformedAndDuplicated_ReportsFormatForEachAndDuplicateForTheLaterOne()
    {
        // Arrange
        var blueprint = Agency() with { Fields = [.. Agency().Fields, TextField("Bad"), TextField("Bad")] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.KeyFormat, "/fields/4/key"),
            (IssueCodes.KeyFormat, "/fields/5/key"),
            (IssueCodes.KeyDuplicate, "/fields/5/key"),
        }));
    }

    [Test]
    public void Validate_MalformedAndDuplicatedKeys_MessagesQuoteTheKey()
    {
        // Arrange
        var blueprint = Agency() with { Fields = [.. Agency().Fields, TextField("Bad"), TextField("impact")] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(2));
            Assert.That(issues[0].Message, Does.Contain("「Bad」"));
            Assert.That(issues[1].Message, Does.Contain("「impact」"));
        }
    }

    [Test]
    public void Validate_VeryLongMalformedKey_CutsTheKeyInTheMessage()
    {
        // Arrange
        var key = new string('k', 100_000);
        var blueprint = Agency() with { Metadata = Agency().Metadata with { Key = key } };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.KeyFormat, "/metadata/key") }));
            Assert.That(issues[0].Message, Does.Contain("…」"));
            Assert.That(issues[0].Message, Has.Length.LessThan(IssueText.MaxQuotedLength * 4));
        }
    }

    private static TestCaseData Case(string scope, Func<ProjectBlueprint, ProjectBlueprint> breakIt, string path) =>
        new TestCaseData(breakIt, path).SetArgDisplayNames(scope);
}
