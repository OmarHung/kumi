using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Planning;

/// <summary>
/// 護欄：狀態被拿掉時，必須寫明停在那個狀態的項目搬去哪裡。
/// 項目屬於類型，類型決定用哪個流程，所以「哪些狀態被拿掉」是以類型為單位來看的。
/// </summary>
[TestFixture]
public sealed class PlanStateMoveTests
{
    [Test]
    public void Plan_RemovedStateWithAMoveHint_BecomesAStateMove()
    {
        // Act：範例變更集把 qa 拆成 internal_qa 與 client_qa，並寫明 qa 的項目搬到 internal_qa。
        var plan = Planner.Plan(WebsiteVersion12(), CurrentVersion, SplitAcceptance());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new StateMoveImpact("main", "qa", "internal_qa") }));
            Assert.That(plan.DataImpacts[0].AffectedItems, Is.Null, "還沒有資料庫，筆數先留空");
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_RemovedStateWithoutAMoveHint_IsBlockedAndNamesTheWorkflowAndTheStateByTheirNames()
    {
        // Arrange
        var changeSet = SplitAcceptance() with { Migrations = null };

        // Act
        var plan = Planner.Plan(WebsiteVersion12(), CurrentVersion, changeSet);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanStateMoveRequired, "/migrations/moveStates"),
        }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Report.Issues[0].Message,
                Is.EqualTo("狀態「驗收」不在新藍圖的流程「主要流程」裡了，但沒有寫明停在這個狀態的項目要搬去哪裡；請在 migrations.moveStates 為這個流程與狀態加一條搬移指示。"));
            Assert.That(plan.DataImpacts, Is.Empty);
        }
    }

    [Test]
    public void Plan_TwoItemTypesShareTheWorkflow_AsksForTheMoveOnce()
    {
        // Arrange：requirement 與 change_request 都用 delivery。
        var next = Agency().WithWorkflow(Delivery, workflow => workflow with { States = workflow.States.WithoutAt(1) });

        // Act
        var withoutHint = PlanFrom(Agency(), next);
        var withHint = PlanFrom(Agency(), next, Moves(("delivery", "quoting", "confirming")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withoutHint.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.PlanStateMoveRequired, "/migrations/moveStates"),
            }));
            Assert.That(withHint.Report.Issues, Is.Empty);
            Assert.That(withHint.DataImpacts, Is.EqualTo(new DataImpact[]
            {
                new StateMoveImpact("delivery", "quoting", "confirming"),
            }));
        }
    }

    [Test]
    public void Plan_StateRemovedFromAWorkflowNoItemTypeUses_NeedsNoMove()
    {
        // Arrange：沒有類型在用的流程，底下不會有項目。
        var spare = SimpleWorkflow("spare") with { States = [State("draft", StateCategory.NotStarted), State("done", StateCategory.Done)] };
        var current = Agency() with { Workflows = [.. Agency().Workflows, spare] };
        var next = Agency() with { Workflows = [.. Agency().Workflows, SimpleWorkflow("spare")] };

        // Act
        var plan = PlanFrom(current, next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Changes.Describe(), Is.EqualTo(new[] { "Removed State spare/draft" }));
            Assert.That(plan.DataImpacts, Is.Empty);
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_ItemTypeMovesToAnotherWorkflow_NeedsAMoveForEveryStateTheNewWorkflowLacks()
    {
        // Arrange：task 原本用 task_flow（todo、doing、done），改用 delivery，三個狀態 delivery 都沒有。
        var next = Agency().WithItemType(TaskType, type => type with { Workflow = "delivery" });
        var hints = Moves(("delivery", "todo", "confirming"), ("delivery", "doing", "developing"), ("delivery", "done", "closed"));

        // Act
        var withoutHints = PlanFrom(Agency(), next);
        var withHints = PlanFrom(Agency(), next, hints);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withoutHints.Report.Issues.Select(issue => issue.Code), Is.EqualTo(new[]
            {
                IssueCodes.PlanStateMoveRequired, IssueCodes.PlanStateMoveRequired, IssueCodes.PlanStateMoveRequired,
            }));
            Assert.That(withoutHints.Report.Issues.Select(issue => issue.Message), Has.All.Contains("「交付流程」"));
            Assert.That(withoutHints.Report.Issues.Select(issue => issue.Message), Has.Some.Contains("「進行中」"), "狀態的名稱取原本的流程裡的");
            Assert.That(withHints.Report.Issues, Is.Empty);
            Assert.That(withHints.DataImpacts, Is.EqualTo(new DataImpact[]
            {
                new StateMoveImpact("delivery", "todo", "confirming"),
                new StateMoveImpact("delivery", "doing", "developing"),
                new StateMoveImpact("delivery", "done", "closed"),
            }));
        }
    }

    [Test]
    public void Plan_ItemTypeMovesToAWorkflowThatHasTheSameStates_NeedsNoMove()
    {
        // Arrange：新流程有同樣 key 的狀態，項目留在原地就好。
        var copy = Agency().Workflows[TaskFlow] with { Key = "task_flow_v2" };
        var next = (Agency() with { Workflows = [.. Agency().Workflows, copy] })
            .WithItemType(TaskType, type => type with { Workflow = "task_flow_v2" });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.Empty);
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_NewItemTypeOnAWorkflowThatLostAState_OnlyCountsTheTypesThatAlreadyExisted()
    {
        // Arrange：task_flow 拿掉 doing；同時新增一個也用 task_flow 的類型。新類型底下沒有項目。
        var next = (Agency() with { ItemTypes = [.. Agency().ItemTypes, ItemTypeOf("bug", "task_flow")] })
            .WithWorkflow(TaskFlow, workflow => workflow with { States = workflow.States.WithoutAt(1) });

        // Act
        var plan = PlanFrom(Agency(), next, Moves(("task_flow", "doing", "todo")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new StateMoveImpact("task_flow", "doing", "todo") }));
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_MoveHintForAStateRemovedFromAWorkflowNoItemTypeUses_IsAcceptedWithoutAMove()
    {
        // Arrange：狀態真的被拿掉了，作者也照規矩寫了搬移指示；只是剛好沒有類型在用這個流程，沒有項目要搬。
        var spare = SimpleWorkflow("spare") with { States = [State("draft", StateCategory.NotStarted), State("done", StateCategory.Done)] };
        var current = Agency() with { Workflows = [.. Agency().Workflows, spare] };
        var next = Agency() with { Workflows = [.. Agency().Workflows, SimpleWorkflow("spare")] };

        // Act
        var plan = PlanFrom(current, next, Moves(("spare", "draft", "done")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues, Is.Empty);
            Assert.That(plan.DataImpacts, Is.Empty);
        }
    }

    [Test]
    public void Plan_MoveHintForAStateWhoseOnlyItemTypeIsRemovedToo_IsAcceptedWithoutAMove()
    {
        // Arrange：task 是唯一用 task_flow 的類型。task 被拿掉，task_flow 同時少了 doing。
        var next = (Agency() with { ItemTypes = Agency().ItemTypes.WithoutAt(TaskType) })
            .WithWorkflow(TaskFlow, workflow => workflow with { States = workflow.States.WithoutAt(1) });

        // Act
        var plan = PlanFrom(Agency(), next, Moves(("task_flow", "doing", "todo")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues, Is.Empty);
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new ItemTypeRemovalImpact("task") }));
        }
    }

    [Test]
    public void Plan_MoveHintForAStateThatWasNeverInThatWorkflow_IsBlocked()
    {
        // Arrange：quoting 是 delivery 的狀態，不是 task_flow 的；task_flow 也沒有少任何狀態。
        var hints = Moves(("task_flow", "quoting", "todo"));

        // Act
        var plan = PlanFrom(Agency(), Agency(), hints);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanUnusedMigration, "/migrations/moveStates/0"),
        }));
    }

    [Test]
    public void Plan_MoveHintForAStateThatNeedsNoMove_IsBlocked()
    {
        // Arrange：藍圖沒變，卻寫了一條搬移指示。多半是寫錯狀態的名字，不能當作沒看到。
        var hints = Moves(("delivery", "ghost", "closed"));

        // Act
        var plan = PlanFrom(Agency(), Agency(), hints);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanUnusedMigration, "/migrations/moveStates/0"),
        }));
        Assert.That(plan.Report.Issues[0].Message, Does.Contain("「delivery」").And.Contain("「ghost」"));
    }

    [Test]
    public void Plan_TwoMoveHintsForTheSameState_UsesTheFirstAndBlocksTheSecond()
    {
        // Arrange
        var changeSet = SplitAcceptance() with { Migrations = Moves(("main", "qa", "internal_qa"), ("main", "qa", "client_qa")) };

        // Act
        var plan = Planner.Plan(WebsiteVersion12(), CurrentVersion, changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new StateMoveImpact("main", "qa", "internal_qa") }));
            Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.PlanUnusedMigration, "/migrations/moveStates/1"),
            }));
        }
    }

    [Test]
    public void Plan_NewProjectWithMoveHints_IsBlockedBecauseThereIsNothingToMove()
    {
        // Act
        var plan = Planner.Plan(
            current: null, currentVersion: 0, ChangeSetFor(Agency(), Moves(("delivery", "old", "closed")), baseVersion: 0));

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanUnusedMigration, "/migrations/moveStates/0"),
        }));
    }
}
