using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class TransitionRulesTests
{
    private const int ToQuoting = 0;
    private const int ToClientQa = 5;
    private const int ToClosed = 7;

    [Test]
    public void Validate_TransitionFromUnknownState_ReportsTransitionUnknownStateAtFrom()
    {
        // Arrange
        var blueprint = WithExtraDeliveryTransition(Transition("ghost", "closed"));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.TransitionUnknownState, "/workflows/0/transitions/8/from") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
            Assert.That(issues[0].Message, Does.Contain("「delivery」"));
        }
    }

    [Test]
    public void Validate_TransitionToStateOfAnotherWorkflow_ReportsTransitionUnknownStateAtTo()
    {
        // Arrange：done 是 task_flow 的狀態，不是 delivery 的。
        var blueprint = WithExtraDeliveryTransition(Transition("closed", "done"));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.TransitionUnknownState, "/workflows/0/transitions/8/to") }));
    }

    [Test]
    public void Validate_TransitionToItself_ReportsTransitionSelf()
    {
        // Arrange
        var blueprint = WithExtraDeliveryTransition(Transition("closed", "closed"));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.TransitionSelf, "/workflows/0/transitions/8") }));
            Assert.That(issues[0].Message, Does.Contain("「closed」"));
        }
    }

    [Test]
    public void Validate_SamePairListedTwice_ReportsTransitionDuplicateAtTheLaterOne()
    {
        // Arrange：角色不同也算重複，重複看的是起點與終點。
        var blueprint = WithExtraDeliveryTransition(Transition("confirming", "quoting") with { AllowedRoles = ["admin"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.TransitionDuplicate, "/workflows/0/transitions/8") }));
            Assert.That(issues[0].Message, Does.Contain("「confirming」"));
            Assert.That(issues[0].Message, Does.Contain("「quoting」"));
        }
    }

    [Test]
    public void Validate_UnknownSelfTransitionListedTwice_ReportsEachProblemInOrder()
    {
        // Arrange
        var blueprint = WithExtraDeliveryTransition(Transition("ghost", "ghost"), Transition("ghost", "ghost"));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.TransitionUnknownState, "/workflows/0/transitions/8/from"),
            (IssueCodes.TransitionUnknownState, "/workflows/0/transitions/8/to"),
            (IssueCodes.TransitionSelf, "/workflows/0/transitions/8"),
            (IssueCodes.TransitionUnknownState, "/workflows/0/transitions/9/from"),
            (IssueCodes.TransitionUnknownState, "/workflows/0/transitions/9/to"),
            (IssueCodes.TransitionSelf, "/workflows/0/transitions/9"),
            (IssueCodes.TransitionDuplicate, "/workflows/0/transitions/9"),
        }));
    }

    [Test]
    public void Validate_AllowedRoleThatDoesNotExist_ReportsTransitionUnknownRole()
    {
        // Arrange
        var blueprint = Agency().WithWorkflow(
            Delivery, w => w.WithTransition(ToClientQa, t => t with { AllowedRoles = ["project_manager", "ghost"] }));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.TransitionUnknownRole, "/workflows/0/transitions/5/allowedRoles/1") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void Validate_BuiltInAllowedRoles_ReportsNoIssues()
    {
        // Arrange
        var blueprint = Agency().WithWorkflow(
            Delivery, w => w.WithTransition(ToClientQa, t => t with { AllowedRoles = ["admin", "editor", "commenter", "viewer"] }));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_ProjectManagerRoleRemoved_ReportsEveryPlaceThatUsesIt()
    {
        // Arrange：交接文件整合案例 9。
        var blueprint = Agency() with { Roles = Agency().Roles.WithoutAt(0) };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.TransitionUnknownRole, "/workflows/0/transitions/5/allowedRoles/0"),
            (IssueCodes.TransitionUnknownRole, "/workflows/0/transitions/7/allowedRoles/0"),
            (IssueCodes.ViewUnknownRole, "/views/2/visibleTo/1"),
        }));
    }

    [Test]
    public void Validate_RequiredFieldThatDoesNotExist_ReportsTransitionUnknownField()
    {
        // Arrange
        var blueprint = Agency().WithWorkflow(
            Delivery, w => w.WithTransition(ToClosed, t => t with { RequireFields = ["client_signoff_date", "ghost"] }));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.TransitionUnknownField, "/workflows/0/transitions/7/requireFields/1") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [TestCase("assignee", "due_date")]
    [TestCase("title", "description")]
    public void Validate_RequiredCoreFields_ReportsNoIssues(string first, string second)
    {
        // Arrange：上層項目以外的核心欄位每種類型都有，可以要求先填。
        var blueprint = Agency().WithWorkflow(
            Delivery, w => w.WithTransition(ToClosed, t => t with { RequireFields = [first, second] }));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_ParentRequiredByAWorkflowWhoseTypesCannotHaveAParent_ReportsEachSuchType()
    {
        // Arrange：requirement 與 change_request 的 parents 是空的，要求上層項目的轉換它們永遠過不去。
        var blueprint = Agency().WithWorkflow(
            Delivery, w => w.WithTransition(ToQuoting, t => t with { RequireFields = ["description", "parent"] }));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/0/requireFields/1"),
                (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/0/requireFields/1"),
            }));
            Assert.That(issues[0].Message, Is.EqualTo("類型「requirement」使用流程「delivery」，但這種類型不能有上層項目，轉換要求先填的「parent」永遠填不了。"));
            Assert.That(issues[1].Message, Does.Contain("「change_request」"));
        }
    }

    [Test]
    public void Validate_ParentRequiredWhenOnlySomeTypesCannotHaveAParent_ReportsOnlyThoseTypes()
    {
        // Arrange：需求變更單改成可以掛在需求底下，只剩需求沒有上層。
        var blueprint = Agency()
            .WithItemType(ChangeRequest, t => t with { Parents = ["requirement"] })
            .WithWorkflow(Delivery, w => w.WithTransition(ToClosed, t => t with { RequireFields = ["parent"] }));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/7/requireFields/0"),
            }));
            Assert.That(issues[0].Message, Does.Contain("「requirement」"));
        }
    }

    [Test]
    public void Validate_ParentRequiredWhereEveryTypeCanHaveAParent_ReportsNoIssues()
    {
        // Arrange：用任務流程的只有任務，任務可以掛在需求或需求變更單底下。
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with
        {
            Transitions = [Transition("todo", "doing") with { RequireFields = ["parent"] }, Transition("doing", "done")],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_ParentRequiredByManyTransitionsOfOneWorkflow_ReportsEveryTransition()
    {
        // Arrange：缺上層的類型每個（流程, 欄位）只算一次，但每個轉換都要回報。
        var blueprint = Agency().WithWorkflow(Delivery, w => w
            .WithTransition(ToQuoting, t => t with { RequireFields = ["parent"] })
            .WithTransition(ToClosed, t => t with { RequireFields = ["client_signoff_date", "parent"] }));

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/0/requireFields/0"),
            (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/0/requireFields/0"),
            (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/7/requireFields/1"),
            (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/7/requireFields/1"),
        }));
    }

    [Test]
    public void Validate_RequiredFieldMissingOnATypeThatUsesTheWorkflow_ReportsTransitionFieldMissingOnType()
    {
        // Arrange：交接文件整合案例 7。
        var blueprint = Agency().WithItemType(
            ChangeRequest, t => t with { Fields = ["change_reason", "impact", "quote_amount"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.TransitionFieldMissingOnType, "/workflows/0/transitions/7/requireFields/0"),
            }));
            Assert.That(issues[0].Message, Does.Contain("「client_signoff_date」"));
            Assert.That(issues[0].Message, Does.Contain("「change_request」"));
        }
    }

    [Test]
    public void Validate_RequiredFieldMissingOnSeveralTypes_ReportsOneIssuePerTypeInDocumentOrder()
    {
        // Arrange
        var blueprint = Agency()
            .WithItemType(Requirement, t => t with { Fields = ["quote_amount"] })
            .WithItemType(ChangeRequest, t => t with { Fields = ["quote_amount"] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.Select(i => i.Code), Is.All.EqualTo(IssueCodes.TransitionFieldMissingOnType));
            Assert.That(issues, Has.Count.EqualTo(2));
            Assert.That(issues[0].Message, Does.Contain("「requirement」"));
            Assert.That(issues[1].Message, Does.Contain("「change_request」"));
        }
    }

    [Test]
    public void Validate_TypesOfOtherWorkflowsLackTheRequiredField_ReportsNoIssues()
    {
        // Arrange：task 用的是 task_flow，不受 delivery 的要求影響。
        var blueprint = Agency().WithItemType(TaskType, t => t with { Fields = [] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_DuplicatedWorkflowRequiresAField_DoesNotCheckTypesAgainstIt()
    {
        // Arrange：類型依 key 引用流程，重複的 key 指到第一個；第二個只回報重複。
        var duplicate = new Workflow
        {
            Key = "delivery",
            Name = "重複的交付流程",
            States = [State("open", StateCategory.NotStarted), State("closed", StateCategory.Done)],
            Transitions = [Transition("open", "closed") with { RequireFields = ["change_reason"] }],
        };
        var blueprint = Agency() with { Workflows = [.. Agency().Workflows, duplicate] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.KeyDuplicate, "/workflows/2/key") }));
    }

    private static ProjectBlueprint WithExtraDeliveryTransition(params WorkflowTransition[] extra) =>
        Agency().WithWorkflow(Delivery, w => w with { Transitions = [.. w.Transitions!, .. extra] });
}
