using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class ViewRulesTests
{
    [TestCase("state")]
    [TestCase("assignee")]
    [TestCase("item_type")]
    [TestCase("impact")]
    public void Validate_GroupByAllowedValue_ReportsNoIssues(string groupBy)
    {
        // Arrange：impact 是單選欄位。
        var blueprint = Agency().WithView(ProgressBoard, v => v with { GroupBy = groupBy });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [TestCase("ghost")]
    [TestCase("priority")]
    [TestCase("quote_amount")]
    [TestCase("State")]
    [TestCase("state\n")]
    [TestCase("")]
    public void Validate_GroupByOtherValue_ReportsViewInvalidGroupBy(string groupBy)
    {
        // Arrange：核心欄位 priority 與金額欄位都不能拿來分組。
        var blueprint = Agency().WithView(ProgressBoard, v => v with { GroupBy = groupBy });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ViewInvalidGroupBy, "/views/0/groupBy") }));
            Assert.That(issues[0].Message, Does.Contain("「progress_board」"));
        }
    }

    [Test]
    public void Validate_GroupByMultiSelectField_ReportsViewInvalidGroupBy()
    {
        // Arrange：只有單選欄位能分組，多選的項目會同時落在好幾組。
        var tags = new FieldDefinition { Key = "tags", Name = "標籤", Type = FieldType.MultiSelect, Options = [Option("urgent")] };
        var blueprint = (Agency() with { Fields = [.. Agency().Fields, tags] })
            .WithView(ProgressBoard, v => v with { GroupBy = "tags" });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ViewInvalidGroupBy, "/views/0/groupBy") }));
            Assert.That(issues[0].Message, Does.Contain("「tags」"));
        }
    }

    [Test]
    public void Validate_ColumnThatIsNotAField_ReportsViewUnknownField()
    {
        // Arrange
        var blueprint = Agency().WithView(RequirementTable, v => v with { Columns = [.. v.Columns, "ghost"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ViewUnknownField, "/views/1/columns/5") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void Validate_SortByFieldThatDoesNotExist_ReportsViewUnknownFieldAtTheSortField()
    {
        // Arrange
        var blueprint = Agency().WithView(
            RequirementTable,
            v => v with { Sort = [.. v.Sort, new ViewSort { Field = "ghost", Direction = SortDirection.Desc }] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ViewUnknownField, "/views/1/sort/1/field") }));
    }

    [Test]
    public void Validate_FilterOnUnknownItemType_ReportsViewUnknownItemType()
    {
        // Arrange
        var blueprint = Agency().WithView(
            RequirementTable, v => v with { Filter = v.Filter! with { ItemTypes = ["requirement", "ghost"] } });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ViewUnknownItemType, "/views/1/filter/itemTypes/1") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void Validate_FilterOnStatesOfAnyWorkflow_ReportsOnlyTheUnknownState()
    {
        // Arrange：developing 屬於 delivery、doing 屬於 task_flow，都存在。
        var blueprint = Agency().WithView(
            RequirementTable, v => v with { Filter = v.Filter! with { States = ["developing", "doing", "ghost"] } });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ViewUnknownState, "/views/1/filter/states/2") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void Validate_VisibleToUnknownRole_ReportsViewUnknownRole()
    {
        // Arrange：內建角色不必宣告。
        var blueprint = Agency().WithView(ClientView, v => v with { VisibleTo = [.. v.VisibleTo!, "viewer", "ghost"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ViewUnknownRole, "/views/2/visibleTo/3") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void Validate_ViewWithEveryKindOfProblem_ReportsThemInDocumentOrder()
    {
        // Arrange
        var broken = new ViewDefinition
        {
            Key = "broken_view",
            Name = "壞掉的檢視",
            Type = ViewType.Table,
            GroupBy = "ghost_group",
            Columns = ["ghost_column"],
            Sort = [new ViewSort { Field = "ghost_sort", Direction = SortDirection.Asc }],
            Filter = new ViewFilter { ItemTypes = ["ghost_type"], States = ["ghost_state"] },
            VisibleTo = ["ghost_role"],
        };
        var blueprint = Agency() with { Views = [.. Agency().Views, broken] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.ViewInvalidGroupBy, "/views/3/groupBy"),
            (IssueCodes.ViewUnknownField, "/views/3/columns/0"),
            (IssueCodes.ViewUnknownField, "/views/3/sort/0/field"),
            (IssueCodes.ViewUnknownItemType, "/views/3/filter/itemTypes/0"),
            (IssueCodes.ViewUnknownState, "/views/3/filter/states/0"),
            (IssueCodes.ViewUnknownRole, "/views/3/visibleTo/0"),
        }));
    }

    [Test]
    public void Validate_ViewVisibleToNobodyListed_ReportsNoIssues()
    {
        // Arrange：空清單與 null 意義不同，但兩者都沒有引用不存在的角色。
        var blueprint = Agency().WithView(ClientView, v => v with { VisibleTo = [] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }
}
