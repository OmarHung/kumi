using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class WorkflowRulesTests
{
    [Test]
    public void Validate_WorkflowWithoutStates_ReportsWorkflowNoStatesOnly()
    {
        // Arrange：沒有狀態時，「沒有完成類狀態」與可達性都沒有意義，不另外回報。
        var empty = new Workflow { Key = "empty_flow", Name = "空流程", States = [], Transitions = [] };
        var blueprint = Agency() with { Workflows = [.. Agency().Workflows, empty] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.WorkflowNoStates, "/workflows/2/states") }));
            Assert.That(issues[0].Message, Does.Contain("「empty_flow」"));
        }
    }

    [Test]
    public void Validate_WorkflowWithoutDoneState_ReportsWorkflowNoDoneState()
    {
        // Arrange：取消類也是結束類，但不是完成類。
        var blueprint = Agency().WithWorkflow(
            TaskFlow, w => w with { States = w.States.ReplaceAt(2, State("done", StateCategory.Canceled)) });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.WorkflowNoDoneState, "/workflows/1/states") }));
            Assert.That(issues[0].Message, Does.Contain("「task_flow」"));
        }
    }

    [Test]
    public void Validate_DeliveryWithOnlyItsFirstFiveTransitions_ReportsUnreachableAndDeadEndStates()
    {
        // Arrange：交接文件整合案例 8。client_qa 與 closed 走不到，client_qa 也沒有出路。
        var blueprint = Agency().WithWorkflow(Delivery, w => w with { Transitions = [.. w.Transitions!.Take(5)] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.WorkflowUnreachableState, "/workflows/0/states/5"),
                (IssueCodes.WorkflowDeadEndState, "/workflows/0/states/5"),
                (IssueCodes.WorkflowUnreachableState, "/workflows/0/states/6"),
            }));
            Assert.That(issues[0].Message, Does.Contain("「client_qa」"));
            Assert.That(issues[2].Message, Does.Contain("「closed」"));
        }
    }

    [Test]
    public void Validate_EmptyTransitionList_MeansNoTransitionsAtAll()
    {
        // Arrange：空清單和沒寫不同，空清單表示完全不能轉。
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with { Transitions = [] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.WorkflowDeadEndState, "/workflows/1/states/0"),
            (IssueCodes.WorkflowUnreachableState, "/workflows/1/states/1"),
            (IssueCodes.WorkflowDeadEndState, "/workflows/1/states/1"),
            (IssueCodes.WorkflowUnreachableState, "/workflows/1/states/2"),
        }));
    }

    [Test]
    public void Validate_NonTerminalStateWithoutExit_ReportsWorkflowDeadEndState()
    {
        // Arrange
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with
        {
            States = [.. w.States, State("on_hold", StateCategory.InProgress)],
            Transitions = [Transition("todo", "doing"), Transition("doing", "done"), Transition("todo", "on_hold")],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.WorkflowDeadEndState, "/workflows/1/states/3") }));
            Assert.That(issues[0].Message, Does.Contain("「on_hold」"));
        }
    }

    [Test]
    public void Validate_TerminalStatesWithoutExit_ReportsNoIssues()
    {
        // Arrange：完成類與取消類都是結束類，不需要出路。
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with
        {
            States = [.. w.States, State("dropped", StateCategory.Canceled)],
            Transitions = [Transition("todo", "doing"), Transition("doing", "done"), Transition("doing", "dropped")],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_SelfTransitionIsTheOnlyWayOut_StillReportsDeadEnd()
    {
        // Arrange：不合規的轉換不算出路。
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with
        {
            Transitions = [Transition("todo", "doing"), Transition("doing", "doing"), Transition("todo", "done")],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.TransitionSelf, "/workflows/1/transitions/1"),
            (IssueCodes.WorkflowDeadEndState, "/workflows/1/states/1"),
        }));
    }

    [Test]
    public void Validate_TransitionToUnknownState_DoesNotCountAsAnEdge()
    {
        // Arrange：doing 唯一的出路指到不存在的狀態，done 因此走不到。
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with
        {
            Transitions = [Transition("todo", "doing"), Transition("doing", "ghost")],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.TransitionUnknownState, "/workflows/1/transitions/1/to"),
            (IssueCodes.WorkflowDeadEndState, "/workflows/1/states/1"),
            (IssueCodes.WorkflowUnreachableState, "/workflows/1/states/2"),
        }));
    }

    [Test]
    public void Validate_CyclesAndDuplicateEdges_TerminatesAndReportsOnlyTheDuplicate()
    {
        // Arrange
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with
        {
            Transitions =
            [
                Transition("todo", "doing"), Transition("doing", "todo"),
                Transition("todo", "doing"), Transition("doing", "done"),
            ],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.TransitionDuplicate, "/workflows/1/transitions/2") }));
    }

    [Test]
    public void Validate_IslandThatLoopsOnItself_ReportsItsStatesAsUnreachable()
    {
        // Arrange：island_a 與 island_b 互相連通，但從起始狀態走不到。
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with
        {
            States = [.. w.States, State("island_a", StateCategory.InProgress), State("island_b", StateCategory.InProgress)],
            Transitions =
            [
                Transition("todo", "doing"), Transition("doing", "done"),
                Transition("island_a", "island_b"), Transition("island_b", "island_a"),
            ],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.WorkflowUnreachableState, "/workflows/1/states/3"),
            (IssueCodes.WorkflowUnreachableState, "/workflows/1/states/4"),
        }));
    }

    [Test]
    public void Validate_DuplicatedStateKey_ReportsReachabilityOnlyForTheFirstOne()
    {
        // Arrange：重複的狀態由規則 1 回報，可達性依 key 判斷，不重複回報第二個。
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with
        {
            States = [.. w.States, State("on_hold", StateCategory.InProgress), State("on_hold", StateCategory.InProgress)],
            Transitions = [Transition("todo", "doing"), Transition("doing", "done")],
        });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.KeyDuplicate, "/workflows/1/states/4/key"),
            (IssueCodes.WorkflowUnreachableState, "/workflows/1/states/3"),
            (IssueCodes.WorkflowDeadEndState, "/workflows/1/states/3"),
        }));
    }

    [Test]
    public void Validate_WorkflowWithoutTransitionList_SkipsReachabilityRules()
    {
        // Arrange：沒有列轉換表示任何狀態之間都能互轉，孤立的狀態也走得到。
        var blueprint = Agency().WithWorkflow(
            TaskFlow, w => w with { States = [.. w.States, State("on_hold", StateCategory.InProgress)], Transitions = null });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }
}
