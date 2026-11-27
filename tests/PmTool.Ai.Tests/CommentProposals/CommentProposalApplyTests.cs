using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.CommentProposals;

/// <summary>留言建議的讀取、套用與作廢：以套用的人的身分依序寫入、部分或全部失敗的狀態、逾時、別人的一律找不到。</summary>
[TestFixture]
public sealed class CommentProposalApplyTests : CommentTestBase
{
    private static Failure Refused(string message) => new(FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor, message);

    [Test]
    public async Task Apply_WritesEveryChangeInOrderAsTheApplyingPerson()
    {
        // Arrange
        Permissions(manage: true);
        var edit = AddComment();
        var delete = AddComment(author: Other);
        var restore = AddComment(author: Other, deletedAt: DateTimeOffset.UnixEpoch, deletedBy: Other);
        var proposal = await CreatedAsync(Edit("WEB-12", edit.Id, "改好了"), Delete("WEB-12", delete.Id), Restore("WEB-12", restore.Id));

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(applied.Value.Changes.All(c => c.Error is null), Is.True);
            Assert.That(Comments.Writes.Select(w => w.Action), Is.EqualTo(new[] { "edit", "delete", "restore" }));
            Assert.That(Comments.Writes.All(w => w.Actor == Me), Is.True);
            Assert.That(Comments.Writes[0].Body, Is.EqualTo("改好了"));
        }
    }

    [Test]
    public async Task Apply_SomeFail_IsAppliedWithEachErrorOnItsOwnChange()
    {
        // Arrange
        var first = AddComment();
        var second = AddComment();
        var proposal = await CreatedAsync(Delete("WEB-12", first.Id), Delete("WEB-12", second.Id));
        Comments.OnWrite = (_, id) => id == first.Id ? (Result<ItemActivity>?)Refused("這則留言已經被別人處理了。") : null;

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Changes[0].Error, Is.EqualTo("這則留言已經被別人處理了。"));
            Assert.That(applied.Value.Changes[1].Error, Is.Null);
            Assert.That(applied.Value.Error, Does.Contain("1 筆沒成功"));
            Assert.That(read.Value.Changes[0].Error, Is.EqualTo("這則留言已經被別人處理了。"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
        }
    }

    [Test]
    public async Task Apply_AllFail_ReturnsTheFirstFailureAndStaysPending()
    {
        // Arrange
        var first = AddComment();
        var second = AddComment();
        var proposal = await CreatedAsync(Delete("WEB-12", first.Id), Delete("WEB-12", second.Id));
        Comments.OnWrite = (_, id) => Refused(id == first.Id ? "第一則不行。" : "第二則不行。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.CommentNotAuthor));
            Assert.That(applied.Failure?.Message, Is.EqualTo("第一則不行。"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(read.Value.Changes.All(c => c.Error is null), Is.True);
        }
    }

    [Test]
    public async Task Apply_TimesOut_IsAClearFailureAndStaysPendingSoPressingAgainIsSafe()
    {
        // Arrange
        var comment = AddComment();
        var proposal = await CreatedAsync(Delete("WEB-12", comment.Id));
        Comments.OnWrite = (_, _) => throw new OperationCanceledException();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        Comments.OnWrite = null;
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ApplyTimedOut));
            Assert.That(again.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
        }
    }

    [Test]
    public async Task Apply_AfterItWasAppliedOrDismissed_IsClosed()
    {
        // Arrange
        var comment = AddComment();
        var applied = await CreatedAsync(Delete("WEB-12", comment.Id));
        var dismissed = await CreatedAsync(Delete("WEB-12", comment.Id));
        await Proposals.ApplyAsync(Me, applied.Id, CancellationToken.None);
        await Proposals.DismissAsync(Me, dismissed.Id, CancellationToken.None);

        // Act
        var applyAgain = await Proposals.ApplyAsync(Me, applied.Id, CancellationToken.None);
        var applyDismissed = await Proposals.ApplyAsync(Me, dismissed.Id, CancellationToken.None);
        var dismissApplied = await Proposals.DismissAsync(Me, applied.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applyAgain.Failure?.Code, Is.EqualTo(AiErrorCodes.CommentProposalClosed));
            Assert.That(applyDismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.CommentProposalClosed));
            Assert.That(dismissApplied.Failure?.Code, Is.EqualTo(AiErrorCodes.CommentProposalClosed));
            Assert.That(Comments.Writes, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Dismiss_MarksItDismissedWithoutWriting()
    {
        // Arrange
        var comment = AddComment();
        var proposal = await CreatedAsync(Delete("WEB-12", comment.Id));

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(ItemProposalStatus.Dismissed));
            Assert.That(dismissed.Value.DecidedAt, Is.Not.Null);
            Assert.That(Comments.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task AnotherPersonOrAnUnknownId_IsNotFoundForEveryOperation()
    {
        // Arrange
        var comment = AddComment();
        var proposal = await CreatedAsync(Delete("WEB-12", comment.Id));
        var stranger = new Actor(Me.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);
        var otherOrganization = new Actor(Guid.NewGuid(), Me.PersonId, IsOrganizationAdmin: false);

        // Act
        var results = new[]
        {
            await Proposals.GetAsync(stranger, proposal.Id, CancellationToken.None),
            await Proposals.ApplyAsync(stranger, proposal.Id, CancellationToken.None),
            await Proposals.DismissAsync(stranger, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(otherOrganization, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(Me, Guid.NewGuid(), CancellationToken.None),
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.CommentProposalNotFound));
            Assert.That(Comments.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task WhenTheProjectIsNoLongerVisible_TheProposalIsNotFound()
    {
        // Arrange
        var comment = AddComment();
        var proposal = await CreatedAsync(Delete("WEB-12", comment.Id));
        H.Projects.OnGet = (_, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        var apply = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.CommentProposalNotFound));
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.CommentProposalNotFound));
        }
    }
}
