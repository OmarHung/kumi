using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Items;

/// <summary>套用與作廢：以套用的人的身分、依序寫入，失敗時的狀態，別人的建議一律找不到。</summary>
[TestFixture]
public sealed class ItemProposalApplyTests : ItemProposalTestBase
{
    [Test]
    public async Task Apply_FieldsStatusAndComment_WritesInOrderAsTheApplyingPerson()
    {
        // Arrange
        var proposal = await CreatedAsync(
            [Change("title", "新標題"), Change("assignee", ItemSamples.MemberId), Change("priority", "high"), Change("dueDate", "2026-12-01"),
             Change("description", "新說明"), Change("status", "done")],
            "做完了");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        var update = H.WorkItems.Updates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(update.Actor, Is.EqualTo(Me));
            Assert.That(update.Patch.Title.Value, Is.EqualTo("新標題"));
            Assert.That(update.Patch.AssigneeId.Value, Is.EqualTo(ItemSamples.MemberId));
            Assert.That(update.Patch.Priority.Value, Is.EqualTo(Priority.High));
            Assert.That(update.Patch.DueDate.Value, Is.EqualTo(new DateOnly(2026, 12, 1)));
            Assert.That(update.Patch.Description.Value, Is.EqualTo("新說明"));
            Assert.That(update.Patch.ExpectedDescriptionRevision, Is.EqualTo(DescriptionRevisions.Of("舊的說明")));
            Assert.That(H.WorkItems.Transitions.Single(), Is.EqualTo((Me, "WEB-12", "done")));
            Assert.That(H.Activity.Comments.Single(), Is.EqualTo((Me, "WEB-12", "做完了")));
        }
    }

    [Test]
    public async Task Apply_OnlyStatus_DoesNotCallUpdate()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("status", "done")]);

        // Act
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(H.WorkItems.Updates, Is.Empty);
            Assert.That(H.WorkItems.Transitions, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_CustomFieldsAndClearedValues_GoIntoThePatch()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember() with { Blueprint = WithField() };
        var proposal = await CreatedAsync([Change("fields.category", "a"), Change("assignee", null)]);

        // Act
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        var patch = H.WorkItems.Updates.Single().Patch;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Fields["category"].GetString(), Is.EqualTo("a"));
            Assert.That(patch.AssigneeId.HasValue, Is.True);
            Assert.That(patch.AssigneeId.Value, Is.Null);
        }
    }

    [Test]
    public async Task Apply_DescriptionConflict_ChangesNothingAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("description", "新說明"), Change("status", "done")], "留言");
        H.WorkItems.OnUpdate = (_, _) => WorkSamples.Failure(FailureKind.Conflict, WorkErrorCodes.ItemDescriptionConflict, "說明被改過了。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemDescriptionConflict));
            Assert.That(applied.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(H.WorkItems.Transitions, Is.Empty);
            Assert.That(H.Activity.Comments, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_DescriptionCheckUsesTheRevisionTheModelReadNotTheOneAtCreation()
    {
        // Arrange
        var created = await CreateAsync([Change("description", "新說明")], revision: "revision-the-model-read");

        // Act
        await Proposals.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(H.WorkItems.Updates.Single().Patch.ExpectedDescriptionRevision, Is.EqualTo("revision-the-model-read"));
    }

    [Test]
    public async Task Apply_StatusFailsAfterFieldsWereWritten_IsAppliedWithAnErrorAndSkipsTheComment()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題"), Change("status", "done")], "留言");
        H.WorkItems.OnTransition = (_, _) => WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.TransitionNotAllowed, "不能這樣轉換。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("狀態").And.Contain("不能這樣轉換").And.Contain("留言"));
            Assert.That(H.Activity.Comments, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_FirstStepIsTheOnlyOneThatFails_ReturnsItsFailureAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("status", "done")], "留言");
        H.WorkItems.OnTransition = (_, _) => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(H.Activity.Comments, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_CommentFailsAfterFields_IsAppliedWithAnError()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題")], "留言");
        H.Activity.OnComment = _ => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "不能留言。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("留言"));
        }
    }

    [Test]
    public async Task Apply_LaterStepThrowsAfterFieldsWereWritten_IsAppliedWithAnErrorNotA500()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題"), Change("status", "done")]);
        H.WorkItems.OnTransition = (_, _) => throw new InvalidOperationException("連線字串 secret");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("狀態").And.Not.Contain("secret"));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemProposalClosed));
            Assert.That(H.WorkItems.Updates, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_FirstStepThrows_ChangesNothingAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("status", "done")]);
        H.WorkItems.OnTransition = (_, _) => throw new InvalidOperationException("壞掉了");

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None));
        Assert.That((await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None)).Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
    }

    [Test]
    public async Task Apply_Twice_SecondIsClosed()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題")]);
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Act
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemProposalClosed));
            Assert.That(H.WorkItems.Updates, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_SimultaneousRequests_OnlyOneWrites()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題")]);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(H.WorkItems.Updates, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task OtherPersonsProposal_IsNotFoundForGetApplyAndDismiss()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題")]);
        var other = new Actor(Me.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);
        var stranger = new Actor(Guid.NewGuid(), Me.PersonId, IsOrganizationAdmin: false);

        // Act
        var results = new[]
        {
            await Proposals.GetAsync(other, proposal.Id, CancellationToken.None),
            await Proposals.ApplyAsync(other, proposal.Id, CancellationToken.None),
            await Proposals.DismissAsync(other, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(stranger, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(Me, Guid.NewGuid(), CancellationToken.None),
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.ItemProposalNotFound));
            Assert.That(results.Select(r => r.Failure?.Kind), Is.All.EqualTo(FailureKind.NotFound));
            Assert.That(H.WorkItems.Updates, Is.Empty);
        }
    }

    [Test]
    public async Task Dismiss_Pending_MarksItDismissedAndItCanNoLongerBeApplied()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題")]);

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var dismissedAgain = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(ItemProposalStatus.Dismissed));
            Assert.That(dismissed.Value.DecidedAt, Is.Not.Null);
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemProposalClosed));
            Assert.That(dismissedAgain.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemProposalClosed));
            Assert.That(H.WorkItems.Updates, Is.Empty);
        }
    }

    private static PmTool.Blueprint.Model.ProjectBlueprint WithField() => WorkSamples.Blueprint with
    {
        Fields = [new PmTool.Blueprint.Model.FieldDefinition { Key = "category", Name = "分類", Type = PmTool.Blueprint.Model.FieldType.Text }],
        ItemTypes = [new PmTool.Blueprint.Model.ItemType { Key = "task", Name = "任務", Workflow = "flow", Fields = ["category"] }],
    };
}
