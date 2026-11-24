using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class ChangeSetRulesTests
{
    [TestCase(-1L)]
    [TestCase(long.MinValue)]
    public void ValidateChangeSet_NegativeBaseVersion_ReportsChangeSetInvalidBaseVersion(long baseVersion)
    {
        // Arrange
        var changeSet = SplitAcceptance() with { BaseVersion = baseVersion };

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ChangeSetInvalidBaseVersion, "/baseVersion") }));
            Assert.That(issues[0].Message, Does.Contain(baseVersion.ToString()));
        }
    }

    [Test]
    public void ValidateChangeSet_BaseVersionZeroForANewProject_ReportsNoIssues()
    {
        // Arrange
        var changeSet = SplitAcceptance() with { BaseVersion = 0, Migrations = null };

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ValidateChangeSet_BlueprintProblem_PrefixesThePathWithBlueprint()
    {
        // Arrange
        var changeSet = SplitAcceptance().WithBlueprint(b => b with { Metadata = b.Metadata with { Key = "Bad" } });

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.KeyFormat, "/blueprint/metadata/key") }));
    }

    [Test]
    public void ValidateChangeSet_MoveInUnknownWorkflow_ReportsOnlyChangeSetMoveUnknownWorkflow()
    {
        // Arrange：流程不存在時，起點與終點無從比對，不另外回報。
        var changeSet = WithMove("ghost", from: "doing", to: "qa");

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ChangeSetMoveUnknownWorkflow, "/migrations/moveStates/0/workflow") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void ValidateChangeSet_MoveToStateThatIsNotInTheNewBlueprint_ReportsChangeSetMoveUnknownTarget()
    {
        // Arrange：交接文件整合案例 12。
        var changeSet = WithMove("main", from: "qa", to: "qa");

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ChangeSetMoveUnknownTarget, "/migrations/moveStates/0/to") }));
            Assert.That(issues[0].Message, Does.Contain("「qa」"));
            Assert.That(issues[0].Message, Does.Contain("「main」"));
        }
    }

    [Test]
    public void ValidateChangeSet_MoveToStateOfAnotherWorkflow_ReportsChangeSetMoveUnknownTarget()
    {
        // Arrange：搬移的終點要是同一個流程的狀態。
        var archive = new Workflow
        {
            Key = "archive",
            Name = "封存",
            States = [State("archived", StateCategory.Done)],
        };
        var changeSet = WithMove("main", from: "qa", to: "archived")
            .WithBlueprint(b => b with { Workflows = [.. b.Workflows, archive] });

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ChangeSetMoveUnknownTarget, "/migrations/moveStates/0/to") }));
    }

    [Test]
    public void ValidateChangeSet_MoveFromStateThatStillExists_ReportsChangeSetMoveSourceStillExists()
    {
        // Arrange
        var changeSet = WithMove("main", from: "doing", to: "internal_qa");

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ChangeSetMoveSourceStillExists, "/migrations/moveStates/0/from") }));
            Assert.That(issues[0].Message, Does.Contain("「doing」"));
        }
    }

    [Test]
    public void ValidateChangeSet_MoveWithBothEndsWrong_ReportsFromBeforeTo()
    {
        // Arrange
        var changeSet = WithMove("main", from: "doing", to: "qa");

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.ChangeSetMoveSourceStillExists, "/migrations/moveStates/0/from"),
            (IssueCodes.ChangeSetMoveUnknownTarget, "/migrations/moveStates/0/to"),
        }));
    }

    [TestCase("budget")]
    [TestCase("title")]
    public void ValidateChangeSet_DropFieldThatStillExists_ReportsChangeSetDropFieldStillExists(string field)
    {
        // Arrange：核心欄位永遠存在，不能封存。
        var changeSet = WithBudgetField() with { Migrations = new MigrationHints { DropFields = ["old_notes", field] } };

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ChangeSetDropFieldStillExists, "/migrations/dropFields/1") }));
            Assert.That(issues[0].Message, Does.Contain($"「{field}」"));
        }
    }

    [TestCase("ghost")]
    [TestCase("estimate")]
    public void ValidateChangeSet_ConvertFieldThatIsNotACustomField_ReportsChangeSetConvertUnknownField(string field)
    {
        // Arrange：核心欄位的型別不能改，所以只有自訂欄位能轉換。
        var conversions = new[]
        {
            new FieldConversion { Field = "budget", OnFailure = ConversionFailurePolicy.Abort },
            new FieldConversion { Field = field, OnFailure = ConversionFailurePolicy.LeaveEmpty },
        };
        var changeSet = WithBudgetField() with { Migrations = new MigrationHints { ConvertFields = conversions } };

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ChangeSetConvertUnknownField, "/migrations/convertFields/1/field") }));
            Assert.That(issues[0].Message, Does.Contain($"「{field}」"));
        }
    }

    [Test]
    public void ValidateChangeSet_ProblemsEverywhere_ReportsBaseVersionThenBlueprintThenMigrations()
    {
        // Arrange
        var changeSet = WithBudgetField() with
        {
            BaseVersion = -3,
            Migrations = new MigrationHints
            {
                MoveStates = [new StateMove { Workflow = "ghost", From = "a", To = "b" }],
                DropFields = ["budget"],
                ConvertFields = [new FieldConversion { Field = "ghost", OnFailure = ConversionFailurePolicy.Abort }],
            },
        };
        changeSet = changeSet.WithBlueprint(b => b with { Roles = [.. b.Roles, Role("admin")] });

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.ChangeSetInvalidBaseVersion, "/baseVersion"),
            (IssueCodes.RoleBuiltInConflict, "/blueprint/roles/1/key"),
            (IssueCodes.ChangeSetMoveUnknownWorkflow, "/migrations/moveStates/0/workflow"),
            (IssueCodes.ChangeSetDropFieldStillExists, "/migrations/dropFields/0"),
            (IssueCodes.ChangeSetConvertUnknownField, "/migrations/convertFields/0/field"),
        }));
    }

    private static ChangeSetRequest WithMove(string workflow, string from, string to) =>
        SplitAcceptance() with
        {
            Migrations = new MigrationHints { MoveStates = [new StateMove { Workflow = workflow, From = from, To = to }] },
        };

    private static ChangeSetRequest WithBudgetField() =>
        SplitAcceptance().WithBlueprint(b => b with
        {
            Fields = [new FieldDefinition { Key = "budget", Name = "預算", Type = FieldType.Number }],
        });
}
