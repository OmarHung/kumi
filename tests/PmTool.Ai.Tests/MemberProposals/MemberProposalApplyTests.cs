using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.MemberProposals;

/// <summary>套用、作廢與讀取：照順序以套用的人的身分寫入、失敗時的狀態、不會寫兩次、別人看不到。</summary>
[TestFixture]
public sealed class MemberProposalApplyTests : MemberProposalTestBase
{
    [Test]
    public async Task Apply_AddChangeAndRemove_WritesInOrderAsTheApplyingPerson()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"), new MemberChangeDraft(Contributor, "viewer"), new MemberChangeDraft(Editor, null));

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => Describe(applied.Failure));
            Assert.That(applied.Value.Status, Is.EqualTo(MemberProposalStatus.Applied));
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(H.Projects.MemberWrites.Select(w => (w.PersonId, w.Role)), Is.EqualTo(new (Guid, string?)[]
            {
                (Outsider, "editor"), (Contributor, "viewer"), (Editor, null),
            }));
            Assert.That(H.Projects.MemberWrites.Select(w => w.Actor), Is.All.EqualTo(Me));
            Assert.That(H.Projects.MemberWrites.Select(w => w.Key), Is.All.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task Apply_FirstChangeFails_WritesNothingMoreStaysPendingAndReturnsTheEngineFailure()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Manager, null), new MemberChangeDraft(Outsider, "editor"));
        H.Projects.OnMemberWrite = (_, _, _) => WorkSamples.Failure(FailureKind.Conflict, WorkErrorCodes.LastAdmin, "專案至少要有一位管理員。");

        // Act
        var failed = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        H.Projects.OnMemberWrite = null;
        var retried = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Failure?.Code, Is.EqualTo(WorkErrorCodes.LastAdmin));
            Assert.That(failed.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(read.Value.Status, Is.EqualTo(MemberProposalStatus.Pending));
            Assert.That(read.Value.DecidedAt, Is.Null);
            Assert.That(retried.Value.Status, Is.EqualTo(MemberProposalStatus.Applied));
            Assert.That(H.Projects.MemberWrites, Has.Count.EqualTo(3), "第一次只試了第一筆");
        }
    }

    [Test]
    public async Task Apply_MiddleChangeFails_IsAppliedWithAnErrorNamingThePerson()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"), new MemberChangeDraft(OtherOutsider, "viewer"), new MemberChangeDraft(Contributor, "viewer"));
        H.Projects.OnMemberWrite = (index, id, role) => index == 1
            ? WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.Forbidden, "不能這樣做。")
            : new ProjectMember(id, role ?? "x");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(MemberProposalStatus.Applied));
            Assert.That(applied.Value.Error, Is.EqualTo("有幾筆沒有套用：戊（不能這樣做。）。其餘已經套用。"));
            Assert.That(H.Projects.MemberWrites, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task Apply_LaterChangeThrows_IsAppliedWithAnErrorAndDoesNotLeakTheMessage()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"), new MemberChangeDraft(OtherOutsider, "viewer"));
        H.Projects.OnMemberWrite = (index, id, role) => index == 1 ? throw new InvalidOperationException("連線字串 secret") : new ProjectMember(id, role ?? "x");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(MemberProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("戊").And.Not.Contain("secret"));
        }
    }

    [Test]
    public async Task Apply_LongError_IsCutToTheMaximumLength()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"), new MemberChangeDraft(OtherOutsider, "viewer"));
        H.Projects.OnMemberWrite = (index, id, role) => index == 1
            ? WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.Forbidden, new string('字', 2000))
            : new ProjectMember(id, role ?? "x");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(applied.Value.Error!.Length, Is.EqualTo(AiRules.MaxErrorLength));
    }

    [Test]
    public async Task Apply_Twice_SecondIsClosedAndNothingIsWrittenAgain()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"));
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Act
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.MemberProposalClosed));
            Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.MemberProposalClosed));
            Assert.That(H.Projects.MemberWrites, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_PressedTwiceAtTheSameTime_WritesOnlyOnce()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"), new MemberChangeDraft(OtherOutsider, "viewer"));

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Single(r => !r.Succeeded).Failure?.Code, Is.EqualTo(AiErrorCodes.MemberProposalClosed));
            Assert.That(H.Projects.MemberWrites, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Dismiss_MarksItDismissedAndWritesNothing_AndItCannotBeAppliedAfterwards()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"));

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(MemberProposalStatus.Dismissed));
            Assert.That(dismissed.Value.DecidedAt, Is.Not.Null);
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.MemberProposalClosed));
            Assert.That(H.Projects.MemberWrites, Is.Empty);
        }
    }

    [Test]
    public async Task OtherPeopleAndUnknownIds_AreNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"));
        var other = new Actor(Me.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);
        var otherOrganization = new Actor(Guid.NewGuid(), Me.PersonId, IsOrganizationAdmin: false);

        // Act
        var results = new[]
        {
            await Proposals.GetAsync(other, proposal.Id, CancellationToken.None),
            await Proposals.ApplyAsync(other, proposal.Id, CancellationToken.None),
            await Proposals.DismissAsync(other, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(otherOrganization, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(Me, Guid.NewGuid(), CancellationToken.None),
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.MemberProposalNotFound));
            Assert.That(results.Select(r => r.Failure?.Kind), Is.All.EqualTo(FailureKind.NotFound));
            Assert.That(H.Projects.MemberWrites, Is.Empty);
        }
    }

    [Test]
    public async Task ProjectNoLongerVisible_MakesTheProposalNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"));
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.MemberProposalNotFound));
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.MemberProposalNotFound));
        }
    }
}
