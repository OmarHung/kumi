using PmTool.Blueprint.Model;
using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Planning;

/// <summary>每一類資源各改一處，看差異怎麼描述它。預期的寫法見 PlanTestSupport.Describe。</summary>
[TestFixture]
public sealed class PlanDiffByResourceTests
{
    private static IEnumerable<TestCaseData> MetadataWorkflowAndItemTypeEdits()
    {
        yield return Edit(
            "metadata name, description and template version",
            blueprint => blueprint with
            {
                Metadata = blueprint.Metadata with
                {
                    Name = "接案",
                    Description = null,
                    BasedOn = blueprint.Metadata.BasedOn! with { Version = "1.1.0" },
                },
            },
            "Modified Metadata agency_project [name,description,basedOn]");

        yield return Edit(
            "workflow renamed",
            blueprint => blueprint.WithWorkflow(TaskFlow, workflow => workflow with { Name = "任務" }),
            "Modified Workflow task_flow [name]");

        yield return Edit(
            "item type moves to another workflow and loses its fields",
            blueprint => blueprint.WithItemType(Requirement, type => type with { Workflow = "task_flow", Fields = [] }),
            "Modified ItemType requirement [workflow,fields]");

        yield return Edit(
            "item type parents and icon",
            blueprint => blueprint.WithItemType(TaskType, type => type with { Parents = ["requirement"], Icon = "check" }),
            "Modified ItemType task [parents,icon]");

    }

    private static IEnumerable<TestCaseData> ViewAndAutomationEdits()
    {
        yield return Edit(
            "view type and grouping",
            blueprint => blueprint.WithView(ProgressBoard, view => view with { Type = ViewType.Table, GroupBy = "assignee" }),
            "Modified View progress_board [type,groupBy]");

        yield return Edit(
            "view columns, sort, filter and an explicit empty visibleTo",
            blueprint => blueprint.WithView(
                RequirementTable, view => view with { Columns = ["title"], Sort = [], Filter = null, VisibleTo = [] }),
            "Modified View requirement_table [columns,sort,filter,visibleTo]");

        yield return Edit(
            "view filter content",
            blueprint => blueprint.WithView(
                ClientView, view => view with { Filter = view.Filter! with { ItemTypes = ["requirement"] } }),
            "Modified View client_view [filter]");

        yield return Edit(
            "automation name, switch, trigger, conditions and actions",
            blueprint => blueprint.WithAutomation(0, automation => automation with
            {
                Name = "合併後通知",
                Enabled = false,
                Trigger = automation.Trigger with { Event = "merge_request.opened" },
                Conditions = [],
                Actions = [new AutomationAction { Type = ActionType.Notify, Message = "已合併" }],
            }),
            "Modified Automation mr_merged_to_internal_qa [name,enabled,trigger,conditions,actions]");

    }

    private static IEnumerable<TestCaseData> RoleBindingAndWholeResourceEdits()
    {
        yield return Edit(
            "role permissions",
            blueprint => blueprint with
            {
                Roles = blueprint.Roles.ReplaceAt(1, blueprint.Roles[1] with { Permissions = ["item.read"] }),
            },
            "Modified Role client [permissions]");

        yield return Edit(
            "role added and another removed",
            blueprint => blueprint with { Roles = [blueprint.Roles[0], Role("qa_lead")] },
            "Added Role qa_lead",
            "Removed Role client");

        yield return Edit(
            "binding points to another resource",
            blueprint => blueprint with
            {
                Bindings = [new IntegrationBinding { Connector = ConnectorType.Gitlab, Resource = "example-group/other" }],
            },
            "Added Binding gitlab:example-group/other",
            "Removed Binding gitlab:example-group/web-app");

        yield return Edit(
            "view added and the automation removed",
            blueprint => blueprint with
            {
                Views = [.. blueprint.Views, new ViewDefinition { Key = "my_board", Name = "我的看板", Type = ViewType.Board }],
                Automations = [],
            },
            "Added View my_board",
            "Removed Automation mr_merged_to_internal_qa");
    }

    [TestCaseSource(nameof(MetadataWorkflowAndItemTypeEdits))]
    [TestCaseSource(nameof(ViewAndAutomationEdits))]
    [TestCaseSource(nameof(RoleBindingAndWholeResourceEdits))]
    public void Plan_OneEditToTheAgencyBlueprint_IsDescribedAs(Func<ProjectBlueprint, ProjectBlueprint> edit, string[] expected)
    {
        // Arrange
        var next = edit(Agency());

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        Assert.That(plan.Changes.Describe(), Is.EqualTo(expected));
    }

    [Test]
    public void Plan_EditsToEveryKindOfResource_AreListedInTheOrderOfTheDocument()
    {
        // Arrange：故意從文件的最後面改到最前面。
        var next = Agency() with
        {
            Bindings = [],
            Roles = [.. Agency().Roles, Role("qa_lead")],
            Automations = [],
            Views = Agency().Views.WithoutAt(ClientView),
            Metadata = Agency().Metadata with { Name = "接案" },
        };
        next = next
            .WithItemType(TaskType, type => type with { Icon = "check" })
            .WithWorkflow(TaskFlow, workflow => workflow with { Name = "任務" })
            .WithField(QuoteAmount, field => field with { Name = "報價" });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        Assert.That(plan.Changes.Describe(), Is.EqualTo(new[]
        {
            "Modified Metadata agency_project [name]",
            "Modified Field quote_amount [name]",
            "Modified Workflow task_flow [name]",
            "Modified ItemType task [icon]",
            "Removed View client_view",
            "Removed Automation mr_merged_to_internal_qa",
            "Added Role qa_lead",
            "Removed Binding gitlab:example-group/web-app",
        }));
    }

    [Test]
    public void Plan_MetadataModified_PointsAtTheMetadataObjectInBothBlueprints()
    {
        // Arrange
        var next = Agency() with { Metadata = Agency().Metadata with { Name = "接案" } };

        // Act
        var change = PlanFrom(Agency(), next).Changes.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.CurrentPath, Is.EqualTo("/metadata"));
            Assert.That(change.NewPath, Is.EqualTo("/metadata"));
        }
    }

    private static TestCaseData Edit(string description, Func<ProjectBlueprint, ProjectBlueprint> edit, params string[] expected) =>
        new TestCaseData(edit, expected).SetArgDisplayNames(description);
}
