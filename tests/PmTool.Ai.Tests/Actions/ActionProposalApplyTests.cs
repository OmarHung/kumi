using PmTool.Ai.Actions;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.Actions;

/// <summary>讀、套用、作廢：權限範圍、applying、每步記結果、第一步失敗回待決定、例外維持建立中、卡片輸入。</summary>
[TestFixture]
public sealed class ActionProposalApplyTests : ActionProposalTestBase
{
    private static readonly Failure Refused = new(FailureKind.Forbidden, "item.forbidden", "你沒有權限。");

    [Test]
    public async Task Apply_AllStepsSucceed_RunsInOrderAsTheActorAndRecordsEachResult()
    {
        // Arrange
        var proposal = await CreatedAsync();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(applied.Value.Steps.Select(s => s.Done), Is.All.True);
            Assert.That(applied.Value.Steps.Select(s => s.Link), Is.EqualTo(new[] { "/knowledge/HR/1", "/knowledge/HR/2", "/knowledge/HR/3" }));
            Assert.That(Handler.Applied.Select(a => a.Index), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(Handler.Applied.Select(a => a.Context.Actor), Is.All.EqualTo(Me));
        }
    }

    [Test]
    public async Task Apply_ThenGet_ReadsBackAppliedWithStepResults()
    {
        // Arrange
        var proposal = await CreatedAsync();
        await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);

        // Act
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(read.Value.Steps.Select(s => s.Done), Is.All.True);
            Assert.That(read.Value.DecidedAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task Apply_WhenFirstStepIsRefused_ReturnsItsFailureAndGoesBackToPending()
    {
        // Arrange
        Handler.OnStep = _ => ActionStepResult.Failed(Refused);
        var proposal = await CreatedAsync();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo("item.forbidden"));
            Assert.That(Handler.Applied, Has.Count.EqualTo(1));
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(read.Value.DecidedAt, Is.Null);
        }
    }

    [Test]
    public async Task Apply_WhenALaterStepIsRefused_KeepsGoingMarksItAndStaysApplied()
    {
        // Arrange
        Handler.OnStep = index => index == 1 ? ActionStepResult.Failed(Refused) : ActionStepResult.Done();
        var proposal = await CreatedAsync();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Steps.Select(s => s.Done), Is.EqualTo(new[] { true, false, true }));
            Assert.That(applied.Value.Steps[1].Error, Is.EqualTo("你沒有權限。"));
            Assert.That(applied.Value.Error, Does.Contain("1 步沒成功"));
        }
    }

    [Test]
    public async Task Apply_WhenAStepThrows_StaysApplyingAndCannotBeAppliedAgain()
    {
        // Arrange
        Handler.OnStep = index => index == 1 ? throw new TimeoutException() : ActionStepResult.Done();
        var proposal = await CreatedAsync();

        // Act
        Assert.ThrowsAsync<TimeoutException>(() => Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None));
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        var again = await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Applying));
            Assert.That(read.Value.Steps.Select(s => s.Done), Is.EqualTo(new[] { true, false, false }), "做完的那一步已經記下");
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ActionProposalClosed));
            Assert.That(Handler.Applied, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Get_WhenApplyingForLongerThanTheLimit_ReadsAsAppliedWithInterruptedNote()
    {
        // Arrange
        Handler.OnStep = index => index == 1 ? throw new TimeoutException() : ActionStepResult.Done();
        var proposal = await CreatedAsync();
        Assert.ThrowsAsync<TimeoutException>(() => Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None));
        H.Clock.Advance(AiRules.NewItemProposalApplyStale + TimeSpan.FromSeconds(1));

        // Act
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(read.Value.Error, Does.Contain("中斷"));
        }
    }

    [Test]
    public async Task Apply_Twice_SecondIsClosedAndNothingRunsAgain()
    {
        // Arrange
        var proposal = await CreatedAsync();
        await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);

        // Act
        var again = await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ActionProposalClosed));
            Assert.That(Handler.Applied, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task Dismiss_Pending_MarksDismissedAndBlocksApply()
    {
        // Arrange
        var proposal = await CreatedAsync();

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var apply = await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);
        var again = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(NewItemProposalStatus.Dismissed));
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.ActionProposalClosed));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ActionProposalClosed));
            Assert.That(Handler.Applied, Is.Empty);
        }
    }

    [Test]
    public async Task Dismiss_AfterApplied_IsClosed()
    {
        // Arrange
        var proposal = await CreatedAsync();
        await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.ActionProposalClosed));
    }

    [Test]
    public async Task GetApplyDismiss_ForSomeoneElseOrMissingOrOtherOrganization_AreNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync();
        var other = H.Admin;
        var otherOrganization = new Actor(Guid.CreateVersion7(), Me.PersonId, IsOrganizationAdmin: false);

        // Act
        var results = new[]
        {
            await Proposals.GetAsync(other, proposal.Id, CancellationToken.None),
            await Proposals.ApplyAsync(other, proposal.Id, NoInputs, CancellationToken.None),
            await Proposals.DismissAsync(other, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(otherOrganization, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(Me, Guid.NewGuid(), CancellationToken.None),
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.ActionProposalNotFound));
            Assert.That(Handler.Applied, Is.Empty);
        }
    }

    [Test]
    public async Task Get_WhenTheProjectIsNoLongerVisible_IsNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync(Draft(projectKey: "WEB"));
        H.Projects.OnGet = (_, _) => new Failure(FailureKind.NotFound, "project.not_found", "找不到專案。");

        // Act
        var results = new[]
        {
            await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None),
            await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None),
            await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None),
        };

        // Assert
        Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.ActionProposalNotFound));
    }
}
