using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class AutomationRulesTests
{
    [Test]
    public void Validate_GitlabTriggerWithoutBindings_ReportsAutomationMissingBinding()
    {
        // Arrange：交接文件整合案例 10。
        var blueprint = Agency() with { Bindings = [] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationMissingBinding, "/automations/0/trigger/source") }));
            Assert.That(issues[0].Message, Does.Contain("「mr_merged_to_internal_qa」"));
        }
    }

    [TestCase(ConnectorType.Github)]
    [TestCase(ConnectorType.Webhook)]
    public void Validate_GitlabTriggerWithOtherConnectorOnly_ReportsAutomationMissingBinding(ConnectorType connector)
    {
        // Arrange
        var blueprint = Agency() with { Bindings = [new IntegrationBinding { Connector = connector, Resource = "example" }] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationMissingBinding, "/automations/0/trigger/source") }));
    }

    [Test]
    public void Validate_GithubTriggerWithGitlabBindingOnly_ReportsAutomationMissingBinding()
    {
        // Arrange
        var blueprint = WithTrigger(TriggerSource.Github, "pull_request.merged");

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationMissingBinding, "/automations/0/trigger/source") }));
    }

    [Test]
    public void Validate_GithubTriggerWithGithubBinding_ReportsNoIssues()
    {
        // Arrange
        var blueprint = WithTrigger(TriggerSource.Github, "pull_request.merged") with
        {
            Bindings = [new IntegrationBinding { Connector = ConnectorType.Github, Resource = "example-org/web-app" }],
        };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [TestCase(TriggerSource.Schedule, "every_monday_morning")]
    [TestCase(TriggerSource.Webhook, "anything.at.all")]
    [TestCase(TriggerSource.Item, "item.created")]
    [TestCase(TriggerSource.Item, "item.transitioned")]
    public void Validate_TriggerThatNeedsNoBinding_ReportsNoIssues(TriggerSource source, string eventName)
    {
        // Arrange：Schedule 與 Webhook 的事件名稱不限制。
        var blueprint = WithTrigger(source, eventName) with { Bindings = [] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [TestCase(TriggerSource.Gitlab, "merge_request.approved")]
    [TestCase(TriggerSource.Gitlab, "pull_request.merged")]
    [TestCase(TriggerSource.Gitlab, "merge_request.merged\n")]
    [TestCase(TriggerSource.Item, "item.deleted")]
    public void Validate_UnknownEventOfAKnownSource_ReportsAutomationUnknownEvent(TriggerSource source, string eventName)
    {
        // Arrange
        var blueprint = WithTrigger(source, eventName);

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationUnknownEvent, "/automations/0/trigger/event") }));
    }

    [Test]
    public void Validate_UnknownEvent_MessageQuotesTheEvent()
    {
        // Arrange
        var blueprint = WithTrigger(TriggerSource.Gitlab, "merge_request.approved");

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.Single().Message, Does.Contain("「merge_request.approved」"));
    }

    [Test]
    public void Validate_AutomationWithoutActions_ReportsAutomationNoActions()
    {
        // Arrange
        var blueprint = Agency().WithAutomation(0, a => a with { Actions = [] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationNoActions, "/automations/0/actions") }));
            Assert.That(issues[0].Message, Does.Contain("「mr_merged_to_internal_qa」"));
        }
    }

    [TestCase("ghost")]
    [TestCase("priority")]
    [TestCase("quote_amount")]
    [TestCase("assignee")]
    public void Validate_ConditionOnFieldThatIsNotAllowed_ReportsAutomationInvalidConditionField(string field)
    {
        // Arrange：只能用 state、item_type 或選項型欄位。
        var blueprint = WithCondition(field, "anything");

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationInvalidConditionField, "/automations/0/conditions/0/field") }));
            Assert.That(issues[0].Message, Does.Contain($"「{field}」"));
        }
    }

    [Test]
    public void Validate_ConditionOnSelectFields_ReportsNoIssues()
    {
        // Arrange
        var tags = new FieldDefinition { Key = "tags", Name = "標籤", Type = FieldType.MultiSelect, Options = [Option("urgent")] };
        var blueprint = (Agency() with { Fields = [.. Agency().Fields, tags] }).WithAutomation(0, a => a with
        {
            Conditions =
            [
                new AutomationCondition { Field = "impact", In = ["high"] },
                new AutomationCondition { Field = "tags", In = ["urgent"] },
            ],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_StateConditionWithUnknownValue_ReportsAutomationUnknownState()
    {
        // Arrange：doing 是 task_flow 的狀態，任何流程的狀態都可以。
        var blueprint = WithCondition("state", "developing", "doing", "ghost");

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationUnknownState, "/automations/0/conditions/0/in/2") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void Validate_ItemTypeConditionWithUnknownValue_ReportsAutomationUnknownItemType()
    {
        // Arrange
        var blueprint = WithCondition("item_type", "ghost", "requirement");

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationUnknownItemType, "/automations/0/conditions/0/in/0") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [Test]
    public void Validate_TriggerProblemsAndConditionProblems_ReportsRuleTwelveBeforeRuleThirteen()
    {
        // Arrange
        var blueprint = WithCondition("ghost", "x") with { Bindings = [] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.AutomationMissingBinding, "/automations/0/trigger/source"),
            (IssueCodes.AutomationInvalidConditionField, "/automations/0/conditions/0/field"),
        }));
    }

    private static ProjectBlueprint WithTrigger(TriggerSource source, string eventName) =>
        Agency().WithAutomation(0, a => a with { Trigger = new AutomationTrigger { Source = source, Event = eventName } });

    private static ProjectBlueprint WithCondition(string field, params string[] values) =>
        Agency().WithAutomation(0, a => a with { Conditions = [new AutomationCondition { Field = field, In = values }] });
}
