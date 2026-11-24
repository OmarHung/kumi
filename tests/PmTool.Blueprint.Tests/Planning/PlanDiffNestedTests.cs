using PmTool.Blueprint.Model;
using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Planning;

/// <summary>
/// 欄位的選項、流程的狀態與轉換是資源底下的清單：裡面的每一項各有自己的差異；
/// 清單本身出現在上一層的 ChangedProperties，表示順序變了，或是從「沒寫」變成「有寫」。
/// </summary>
[TestFixture]
public sealed class PlanDiffNestedTests
{
    private static IEnumerable<TestCaseData> StateAndTransitionEdits()
    {
        yield return Edit(
            "state added, renamed, recategorised and removed",
            blueprint => blueprint.WithWorkflow(Delivery, workflow => workflow with
            {
                States =
                [
                    workflow.States[0],
                    workflow.States[2] with { Name = "視覺設計" },
                    .. workflow.States.Skip(3).Take(3),
                    workflow.States[6] with { Category = StateCategory.Canceled },
                    State("archived", StateCategory.Done),
                ],
            }),
            "Modified State delivery/designing [name]",
            "Modified State delivery/closed [category]",
            "Added State delivery/archived",
            "Removed State delivery/quoting");

        yield return Edit(
            "transition gets other roles and fields, one is added, one is removed",
            blueprint => blueprint.WithWorkflow(Delivery, workflow => workflow with
            {
                Transitions =
                [
                    .. workflow.Transitions!.Skip(1).Take(4),
                    workflow.Transitions![5] with { AllowedRoles = [], RequireFields = ["quote_amount"] },
                    .. workflow.Transitions!.Skip(6),
                    Transition("closed", "developing"),
                ],
            }),
            "Modified Transition delivery/internal_qa->client_qa [allowedRoles,requireFields]",
            "Added Transition delivery/closed->developing",
            "Removed Transition delivery/confirming->quoting");

    }

    private static IEnumerable<TestCaseData> TransitionListPresenceEdits()
    {
        yield return Edit(
            "workflow without a transition list gets one",
            blueprint => blueprint.WithWorkflow(TaskFlow, workflow => workflow with { Transitions = [Transition("todo", "doing")] }),
            "Modified Workflow task_flow [transitions]");

        yield return Edit(
            "workflow without a transition list gets an empty one",
            blueprint => blueprint.WithWorkflow(TaskFlow, workflow => workflow with { Transitions = [] }),
            "Modified Workflow task_flow [transitions]");

        yield return Edit(
            "workflow drops its transition list",
            blueprint => blueprint.WithWorkflow(Delivery, workflow => workflow with { Transitions = null }),
            "Modified Workflow delivery [transitions]");

    }

    private static IEnumerable<TestCaseData> OptionAndWholeWorkflowEdits()
    {
        yield return Edit(
            "option added, recoloured and removed",
            blueprint => blueprint.WithField(Impact, field => field with
            {
                Options = [field.Options![1], field.Options![2] with { Color = OptionColor.Purple }, Option("critical")],
            }),
            "Modified FieldOption impact/high [color]",
            "Added FieldOption impact/critical",
            "Removed FieldOption impact/low");

        yield return Edit(
            "field without options gets a list",
            blueprint => blueprint.WithField(ChangeReason, field => field with { Options = [] }),
            "Modified Field change_reason [options]");

        yield return Edit(
            "renamed workflow with a new state lists the workflow first",
            blueprint => blueprint.WithWorkflow(TaskFlow, workflow => workflow with
            {
                Name = "任務",
                States = [.. workflow.States, State("dropped", StateCategory.Canceled)],
            }),
            "Modified Workflow task_flow [name]",
            "Added State task_flow/dropped");

        yield return Edit(
            "whole workflow removed and another added are one entry each",
            blueprint => blueprint with { Workflows = [blueprint.Workflows[Delivery], SimpleWorkflow("simple")] },
            "Added Workflow simple",
            "Removed Workflow task_flow");
    }

    private static IEnumerable<TestCaseData> Reorderings()
    {
        yield return Edit(
            "first two states swapped, which changes the starting state",
            blueprint => blueprint.WithWorkflow(Delivery, workflow => workflow with
            {
                States = [workflow.States[1], workflow.States[0], .. workflow.States.Skip(2)],
            }),
            "Modified Workflow delivery [states]");

        yield return Edit(
            "transitions listed in another order",
            blueprint => blueprint.WithWorkflow(Delivery, workflow => workflow with
            {
                Transitions = [.. workflow.Transitions!.Reverse()],
            }),
            "Modified Workflow delivery [transitions]");

        yield return Edit(
            "options listed in another order",
            blueprint => blueprint.WithField(Impact, field => field with { Options = [.. field.Options!.Reverse()] }),
            "Modified Field impact [options]");

        yield return Edit(
            "fields and views listed in another order",
            blueprint => blueprint with { Fields = [.. blueprint.Fields.Reverse()], Views = [.. blueprint.Views.Reverse()] },
            "Modified Blueprint agency_project [fields,views]");

        yield return Edit(
            "state inserted in the middle keeps the order of the others",
            blueprint => blueprint.WithWorkflow(TaskFlow, workflow => workflow with
            {
                States = [workflow.States[0], State("review", StateCategory.InProgress), .. workflow.States.Skip(1)],
            }),
            "Added State task_flow/review");
    }

    [TestCaseSource(nameof(StateAndTransitionEdits))]
    [TestCaseSource(nameof(TransitionListPresenceEdits))]
    [TestCaseSource(nameof(OptionAndWholeWorkflowEdits))]
    [TestCaseSource(nameof(Reorderings))]
    public void Plan_OneEditInsideAResource_IsDescribedAs(Func<ProjectBlueprint, ProjectBlueprint> edit, string[] expected)
    {
        // Arrange
        var next = edit(Agency());

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        Assert.That(plan.Changes.Describe(), Is.EqualTo(expected));
    }

    [Test]
    public void Plan_NestedChanges_PointInsideTheirParentInBothBlueprints()
    {
        // Arrange：前面多一個流程，delivery 在新藍圖裡往後移一格；它的最後一個狀態改名、再加一個狀態。
        var next = Agency() with { Workflows = [SimpleWorkflow("simple"), .. Agency().Workflows] };
        next = next.WithWorkflow(Delivery + 1, workflow => workflow with
        {
            States = [.. workflow.States.Take(6), workflow.States[6] with { Name = "已結案" }, State("archived", StateCategory.Done)],
        });

        // Act
        var changes = PlanFrom(Agency(), next).Changes;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changes.Describe(), Is.EqualTo(new[]
            {
                "Added Workflow simple", "Modified State delivery/closed [name]", "Added State delivery/archived",
            }));
            Assert.That(changes[1].CurrentPath, Is.EqualTo("/workflows/0/states/6"));
            Assert.That(changes[1].NewPath, Is.EqualTo("/workflows/1/states/6"));
            Assert.That(changes[2].CurrentPath, Is.Null);
            Assert.That(changes[2].NewPath, Is.EqualTo("/workflows/1/states/7"));
        }
    }

    [Test]
    public void Plan_TopLevelListsReordered_PointsAtTheRootOfBothBlueprints()
    {
        // Arrange
        var next = Agency() with { Roles = [.. Agency().Roles.Reverse()] };

        // Act
        var change = PlanFrom(Agency(), next).Changes.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.CurrentPath, Is.Empty);
            Assert.That(change.NewPath, Is.Empty);
            Assert.That(change.ChangedProperties, Is.EqualTo(new[] { "roles" }));
        }
    }

    private static TestCaseData Edit(string description, Func<ProjectBlueprint, ProjectBlueprint> edit, params string[] expected) =>
        new TestCaseData(edit, expected).SetArgDisplayNames(description);
}
