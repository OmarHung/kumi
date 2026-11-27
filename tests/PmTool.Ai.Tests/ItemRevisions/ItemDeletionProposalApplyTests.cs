using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRevisions;

/// <summary>刪除建議的套用與作廢：以套用的人的身分批次刪除、失敗時的狀態、別人的一律找不到。</summary>
[TestFixture]
public sealed class ItemDeletionProposalApplyTests : ItemRevisionTestBase
{
    private IItemDeletionProposalService Proposals => H.Host.Get<IItemDeletionProposalService>();

    private async Task<ItemDeletionProposal> CreatedAsync(params string[] keys)
    {
        var created = await H.Host.Get<IItemDeletionProposalCreator>().CreateAsync(
            Me, new ItemDeletionProposalDraft("WEB", keys), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }

    private static BulkItemFailure Refused(string key, string message) =>
        new(key, new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, message));

    [Test]
    public async Task Apply_DeletesAllItemsAsTheApplyingPerson()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12", "WEB-13");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(applied.Value.DeletedKeys, Is.EqualTo(new[] { "WEB-12", "WEB-13" }));
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(Bulk.Deletes.Single().Actor, Is.EqualTo(Me));
            Assert.That(Bulk.Deletes.Single().ProjectKey, Is.EqualTo("WEB"));
            Assert.That(Bulk.Deletes.Single().Keys, Is.EqualTo(new[] { "WEB-12", "WEB-13" }));
        }
    }

    [Test]
    public async Task Apply_EverythingRefused_ReturnsTheFirstFailureAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12", "WEB-13");
        Bulk.OnDelete = _ => new BulkDeleteResult([], [Refused("WEB-12", "沒有權限。"), Refused("WEB-13", "別的原因。")]);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(applied.Failure?.Message, Is.EqualTo("沒有權限。"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(read.Value.DeletedKeys, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_TimesOut_IsAClearFailureAndStaysPendingSoPressingAgainIsSafe()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12", "WEB-13");
        Bulk.OnDelete = _ => throw new OperationCanceledException();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ApplyTimedOut));
            Assert.That(applied.Failure?.Message, Does.Contain("逾時"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task Apply_RequestRefusedAsAWhole_ReturnsTheFailureAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12");
        Bulk.OnDelete = _ => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "你沒有刪除的權限。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Message, Is.EqualTo("你沒有刪除的權限。"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task Apply_SomeRefused_IsAppliedWithAnErrorListingThemAndTheRestDeleted()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12", "WEB-13", "WEB-14");
        Bulk.OnDelete = _ => new BulkDeleteResult(
            [new DeletedWorkItem("WEB-12", DateTimeOffset.UnixEpoch), new DeletedWorkItem("WEB-14", DateTimeOffset.UnixEpoch)],
            [Refused("WEB-13", "還有下層。")]);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.DeletedKeys, Is.EqualTo(new[] { "WEB-12", "WEB-14" }));
            Assert.That(applied.Value.Error, Is.EqualTo("有幾筆沒有刪除：WEB-13（還有下層。）。其餘已經刪除，可以在「已刪除」復原。"));
            Assert.That(read.Value.DeletedKeys, Is.EqualTo(applied.Value.DeletedKeys));
            Assert.That(read.Value.Error, Is.EqualTo(applied.Value.Error));
        }
    }

    [Test]
    public async Task Apply_Twice_SecondIsClosedAndDeletesNothingMore()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12");
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Act
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalClosed));
            Assert.That(Bulk.Deletes, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_TwoAtTheSameTime_DeletesOnlyOnce()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12");

        // Act
        var results = await Task.WhenAll(
            Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None), Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Single(r => !r.Succeeded).Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalClosed));
            Assert.That(Bulk.Deletes, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Dismiss_PendingProposal_IsDismissedAndCannotBeAppliedAfterwards()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12");

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var dismissedAgain = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(ItemProposalStatus.Dismissed));
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalClosed));
            Assert.That(dismissedAgain.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalClosed));
            Assert.That(Bulk.Deletes, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_AfterBeingApplied_CannotBeDismissed()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12");
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalClosed));
    }

    [Test]
    public async Task ApplyAndDismiss_ProposalOfAnotherPersonOrUnknown_AreNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12");
        var other = new Actor(Me.OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: true);
        var otherOrganization = new Actor(Guid.CreateVersion7(), Me.PersonId, IsOrganizationAdmin: false);

        // Act
        var byOther = await Proposals.ApplyAsync(other, proposal.Id, CancellationToken.None);
        var dismissByOther = await Proposals.DismissAsync(other, proposal.Id, CancellationToken.None);
        var otherOrg = await Proposals.GetAsync(otherOrganization, proposal.Id, CancellationToken.None);
        var unknown = await Proposals.ApplyAsync(Me, Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byOther.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalNotFound));
            Assert.That(dismissByOther.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalNotFound));
            Assert.That(otherOrg.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(unknown.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalNotFound));
            Assert.That(Bulk.Deletes, Is.Empty);
        }
    }

    [Test]
    public async Task ApplyGetAndDismiss_ProjectNoLongerVisible_AreNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync("WEB-12");
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalNotFound));
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalNotFound));
            Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalNotFound));
            Assert.That(Bulk.Deletes, Is.Empty);
        }
    }
}
