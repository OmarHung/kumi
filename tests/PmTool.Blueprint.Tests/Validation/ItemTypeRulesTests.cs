using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class ItemTypeRulesTests
{
    [Test]
    public void Validate_ItemTypeWithUnknownWorkflow_ReportsItemTypeUnknownWorkflow()
    {
        // Arrange
        var blueprint = Agency().WithItemType(TaskType, t => t with { Workflow = "ghost" });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ItemTypeUnknownWorkflow, "/itemTypes/2/workflow") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
            Assert.That(issues[0].Message, Does.Contain("「task」"));
        }
    }

    [TestCase("ghost")]
    [TestCase("due_date")]
    public void Validate_ItemTypeWithFieldThatIsNotACustomField_ReportsItemTypeUnknownField(string field)
    {
        // Arrange：類型的 fields 只列自訂欄位，核心欄位本來就有。
        var blueprint = Agency().WithItemType(Requirement, t => t with { Fields = [.. t.Fields, field] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ItemTypeUnknownField, "/itemTypes/0/fields/2") }));
            Assert.That(issues[0].Message, Does.Contain($"「{field}」"));
        }
    }

    [Test]
    public void Validate_ItemTypeListingAFieldTwice_ReportsItemTypeDuplicateField()
    {
        // Arrange
        var blueprint = Agency().WithItemType(Requirement, t => t with { Fields = [.. t.Fields, "quote_amount"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ItemTypeDuplicateField, "/itemTypes/0/fields/2") }));
            Assert.That(issues[0].Message, Does.Contain("「quote_amount」"));
        }
    }

    [Test]
    public void Validate_UnknownFieldListedTwice_ReportsUnknownThenDuplicate()
    {
        // Arrange
        var blueprint = Agency().WithItemType(TaskType, t => t with { Fields = ["ghost", "ghost"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.ItemTypeUnknownField, "/itemTypes/2/fields/0"),
            (IssueCodes.ItemTypeDuplicateField, "/itemTypes/2/fields/1"),
        }));
    }

    [Test]
    public void Validate_ItemTypeWithUnknownParent_ReportsItemTypeUnknownParent()
    {
        // Arrange
        var blueprint = Agency().WithItemType(TaskType, t => t with { Parents = [.. t.Parents, "ghost"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ItemTypeUnknownParent, "/itemTypes/2/parents/2") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void Validate_ItemTypeThatIsItsOwnParent_ReportsItemTypeParentCycle()
    {
        // Arrange
        var blueprint = Agency().WithItemType(TaskType, t => t with { Parents = ["task"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ItemTypeParentCycle, "/itemTypes/2/parents/0") }));
            Assert.That(issues[0].Message, Does.Contain("「task」"));
        }
    }

    [Test]
    public void Validate_TwoTypesThatAreEachOthersParent_ReportsTheEdgeThatClosesTheCycle()
    {
        // Arrange：requirement → task → requirement。從文件裡第一個類型開始走，task 的第一個上層把圈閉合。
        var blueprint = Agency().WithItemType(Requirement, t => t with { Parents = ["task"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ItemTypeParentCycle, "/itemTypes/2/parents/0") }));
    }

    [Test]
    public void Validate_LongerCycle_ReportsOneIssue()
    {
        // Arrange：a → b → c → a。
        var blueprint = Agency() with
        {
            ItemTypes =
            [
                .. Agency().ItemTypes,
                ItemTypeOf("type_a", "task_flow") with { Parents = ["type_b"] },
                ItemTypeOf("type_b", "task_flow") with { Parents = ["type_c"] },
                ItemTypeOf("type_c", "task_flow") with { Parents = ["type_a"] },
            ],
        };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ItemTypeParentCycle, "/itemTypes/5/parents/0") }));
    }

    [Test]
    public void Validate_TypeThatOnlyPointsIntoACycle_IsNotReported()
    {
        // Arrange：outer → inner_a ⇄ inner_b。outer 本身不在圈上。
        var blueprint = Agency() with
        {
            ItemTypes =
            [
                .. Agency().ItemTypes,
                ItemTypeOf("outer", "task_flow") with { Parents = ["inner_a"] },
                ItemTypeOf("inner_a", "task_flow") with { Parents = ["inner_b"] },
                ItemTypeOf("inner_b", "task_flow") with { Parents = ["inner_a"] },
            ],
        };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.ItemTypeParentCycle, "/itemTypes/5/parents/0") }));
    }

    [Test]
    public void Validate_SelfParentListedTwice_TerminatesAndReportsBothEntries()
    {
        // Arrange
        var blueprint = Agency().WithItemType(TaskType, t => t with { Parents = ["task", "task"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.ItemTypeParentCycle, "/itemTypes/2/parents/0"),
            (IssueCodes.ItemTypeParentCycle, "/itemTypes/2/parents/1"),
        }));
    }

    [Test]
    public void Validate_SharedParentWithoutCycle_ReportsNoIssues()
    {
        // Arrange：菱形 bottom → left、right → top，沒有繞圈。
        var blueprint = Agency() with
        {
            ItemTypes =
            [
                .. Agency().ItemTypes,
                ItemTypeOf("bottom", "task_flow") with { Parents = ["left", "right"] },
                ItemTypeOf("left", "task_flow") with { Parents = ["top"] },
                ItemTypeOf("right", "task_flow") with { Parents = ["top"] },
                ItemTypeOf("top", "task_flow"),
            ],
        };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_DuplicatedItemTypeKeys_DoesNotThrowAndReportsTheDuplicate()
    {
        // Arrange：重複的類型不進上層關係圖，由規則 1 回報。
        var blueprint = Agency() with
        {
            ItemTypes = [.. Agency().ItemTypes, ItemTypeOf("task", "task_flow") with { Parents = ["task"] }],
        };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.KeyDuplicate, "/itemTypes/3/key") }));
    }
}
