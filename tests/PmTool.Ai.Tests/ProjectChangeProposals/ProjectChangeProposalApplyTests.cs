using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.PlanningProposals;
using PmTool.Work;

namespace PmTool.Ai.Tests.ProjectChangeProposals;

/// <summary>套用、作廢與讀取：順序是說明、名稱、封存；說明被別人改過就整份不做；第一步失敗什麼都沒改。</summary>
[TestFixture]
public sealed class ProjectChangeProposalApplyTests : ProjectWriteTestBase
{
    private static readonly DateTimeOffset DescriptionTime = new(2027, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private async Task<ProjectChangeProposal> CreatedAsync(string? name = null, bool? archived = null, Optional<string?> description = default)
    {
        var created = await ChangeCreator.CreateAsync(Me, new ProjectChangeProposalDraft("WEB", name, archived, description), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
        return created.Value;
    }

    [Test]
    public async Task Apply_Everything_WritesDescriptionThenNameThenArchive()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("舊", DescriptionTime, Manager);
        var proposal = await CreatedAsync("新名稱", archived: true, description: "新說明");

        // Act
        var applied = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(applied.Succeeded, Is.True, () => Describe(applied.Failure));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(Descriptions.Sets, Is.EqualTo(new[] { "新說明" }));
            Assert.That(Projects.Writes, Is.EqualTo(new[] { "rename:新名稱", "archive" }));
        }
    }

    [Test]
    public async Task Apply_UnarchiveOnly_CallsUnarchive()
    {
        // Arrange
        UseProject(archived: true);
        var proposal = await CreatedAsync(archived: false);

        // Act
        var applied = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(Projects.Writes, Is.EqualTo(new[] { "unarchive" }));
            Assert.That(Descriptions.Sets, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_ClearingTheDescription_SendsNull()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("舊", DescriptionTime, Manager);
        var proposal = await CreatedAsync(description: new Optional<string?>(null));

        // Act
        var applied = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(Descriptions.Sets, Is.EqualTo(new string?[] { null }));
        }
    }

    [Test]
    public async Task Apply_TheDescriptionWasEditedByAnotherPersonMeanwhile_DoesNothingStaysPendingAndReportsAConflict()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("舊", DescriptionTime, Manager);
        var proposal = await CreatedAsync("新名稱", description: "新說明");
        Descriptions.Current = new ProjectDescription("別人改的", DescriptionTime.AddMinutes(3), Editor);

        // Act
        var conflict = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await ChangeService.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(conflict.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectChangeConflict));
            Assert.That(conflict.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(Descriptions.Sets, Is.Empty);
            Assert.That(Projects.Writes, Is.Empty, "說明衝突時名稱也不改");
        }
    }

    [Test]
    public async Task Apply_NoDescriptionChangeAndTheDescriptionWasEdited_StillApplies()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("舊", DescriptionTime, Manager);
        var proposal = await CreatedAsync("新名稱");
        Descriptions.Current = new ProjectDescription("別人改的", DescriptionTime.AddMinutes(3), Editor);

        // Act
        var applied = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
    }

    [Test]
    public async Task Apply_TheDescriptionNeverWrittenBefore_AppliesWhenStillUnwritten()
    {
        // Arrange
        var proposal = await CreatedAsync(description: "第一份說明");

        // Act
        var applied = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(Descriptions.Sets, Is.EqualTo(new[] { "第一份說明" }));
        }
    }

    [Test]
    public async Task Apply_TheFirstStepIsRefused_ChangesNothingElseAndStaysPending()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("舊", DescriptionTime, Manager);
        var proposal = await CreatedAsync("新名稱", archived: true, description: "新說明");
        Descriptions.OnSet = _ => new Failure(FailureKind.Invalid, WorkErrorCodes.ProjectDescriptionSecret, "有金鑰。");

        // Act
        var failed = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await ChangeService.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectDescriptionSecret));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(Projects.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_ALaterStepIsRefused_IsAppliedWithAnErrorNamingTheStep()
    {
        // Arrange
        var proposal = await CreatedAsync("新名稱", archived: true);
        Projects.OnWrite = text => text == "archive" ? new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "不能封存。") : null;

        // Act
        var applied = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("封存（不能封存。）").And.Contain("其餘已經套用"));
            Assert.That(Projects.Writes, Is.EqualTo(new[] { "rename:新名稱", "archive" }));
        }
    }

    [Test]
    public async Task Apply_ALaterStepThrows_IsAppliedWithAGenericError()
    {
        // Arrange
        var proposal = await CreatedAsync("新名稱", archived: true);
        Projects.OnThrow = text => text == "archive" ? new InvalidOperationException("連線字串在這") : null;

        // Act
        var applied = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("發生未預期的錯誤").And.Not.Contain("連線字串"));
        }
    }

    [Test]
    public async Task Apply_TwiceDismissedOrAnotherPerson_IsClosedOrNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync("新名稱");
        var other = new Actor(H.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

        // Act
        var byOther = await ChangeService.ApplyAsync(other, proposal.Id, CancellationToken.None);
        var first = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var second = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var dismiss = await ChangeService.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byOther.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectChangeProposalNotFound));
            Assert.That(first.Succeeded, Is.True);
            Assert.That(second.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectChangeProposalClosed));
            Assert.That(dismiss.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectChangeProposalClosed));
            Assert.That(Projects.Writes, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Dismiss_WritesNothingAndApplyIsThenClosed()
    {
        // Arrange
        var proposal = await CreatedAsync("新名稱");

        // Act
        var dismissed = await ChangeService.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var apply = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(ItemProposalStatus.Dismissed));
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectChangeProposalClosed));
            Assert.That(Projects.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_TheProjectIsNoLongerVisible_IsNotFoundAndWritesNothing()
    {
        // Arrange
        var proposal = await CreatedAsync("新名稱");
        Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var read = await ChangeService.GetAsync(Me, proposal.Id, CancellationToken.None);
        var apply = await ChangeService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectChangeProposalNotFound));
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectChangeProposalNotFound));
            Assert.That(Projects.Writes, Is.Empty);
        }
    }
}
