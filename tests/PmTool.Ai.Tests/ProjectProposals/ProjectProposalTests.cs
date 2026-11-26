using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ProjectProposals;

/// <summary>新專案建議：只有組織管理員提得出來、檢查照工作引擎、按「建立專案」才以套用的人的身分建立。</summary>
[TestFixture]
public sealed class ProjectProposalTests
{
    private AssistantHarness _h = null!;

    private IProjectProposalCreator Creator => _h.Host.Get<IProjectProposalCreator>();

    private IProjectProposalService Proposals => _h.Host.Get<IProjectProposalService>();

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private static ProjectProposalDraft Draft(string key = "NEWP") => new(key, "新專案", "simple_tasks", IncludeExamples: false, "## 目的");

    private async Task<ProjectProposal> CreatedAsync()
    {
        var created = await Creator.CreateAsync(_h.Admin, Draft(), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }

    [Test]
    public async Task Create_ByAnAdmin_StoresAPendingProposalWithTheTemplateName()
    {
        // Act
        var created = await Creator.CreateAsync(_h.Admin, Draft() with { IncludeExamples = true }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
            Assert.That(created.Value.Status, Is.EqualTo(ProjectProposalStatus.Pending));
            Assert.That(created.Value.Key, Is.EqualTo("NEWP"));
            Assert.That(created.Value.TemplateName, Is.EqualTo("簡單任務"));
            Assert.That(created.Value.IncludeExamples, Is.True);
            Assert.That(created.Value.Description, Is.EqualTo("## 目的"));
            Assert.That(created.Value.CreatedProjectKey, Is.Null);
        }
    }

    [Test]
    public async Task Create_ByANonAdmin_IsForbiddenAndStoresNothing()
    {
        // Act
        var created = await Creator.CreateAsync(_h.Actor, Draft(), CancellationToken.None);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.AssistantForbidden));
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(await db.ProjectProposals.CountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Create_WhenTheWorkEngineRejects_ReturnsItsFailureAsIs()
    {
        // Arrange
        var rejection = WorkSamples.Failure(
            FailureKind.Conflict, WorkErrorCodes.ProjectKeyTaken, "專案代號 NEWP 已經有人用了。");
        _h.Projects.OnCheckNewProject = (_, _) => rejection;

        // Act
        var created = await Creator.CreateAsync(_h.Admin, Draft(), CancellationToken.None);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure, Is.EqualTo(rejection));
            Assert.That(await db.ProjectProposals.CountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Create_WithControlCharacters_IsRejectedBeforeTheDatabase()
    {
        // Act
        var created = await Creator.CreateAsync(_h.Admin, Draft() with { Name = "壞\0名稱" }, CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
    }

    [Test]
    public async Task Apply_CreatesTheProjectAsTheApplyingPersonAndRemembersTheKey()
    {
        // Arrange
        var proposal = await CreatedAsync();
        (Actor Actor, NewProject Project)? call = null;
        _h.Projects.OnCreate = (actor, project) =>
        {
            call = (actor, project);
            return new ProjectSummary(Guid.NewGuid(), project.Key, project.Name, 1, "admin", DateTimeOffset.UnixEpoch);
        };

        // Act
        var applied = await Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ProjectProposalStatus.Applied));
            Assert.That(applied.Value.CreatedProjectKey, Is.EqualTo("NEWP"));
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(call!.Value.Actor, Is.EqualTo(_h.Admin));
            Assert.That(call.Value.Project, Is.EqualTo(new NewProject("NEWP", "新專案", "simple_tasks") { Description = "## 目的" }));
        }
    }

    [Test]
    public async Task Apply_WhenCreationFails_StaysPendingWithTheEnginesFailure()
    {
        // Arrange
        var proposal = await CreatedAsync();
        var failure = WorkSamples.Failure(FailureKind.Conflict, WorkErrorCodes.ProjectKeyTaken, "專案代號 NEWP 已經有人用了。");
        _h.Projects.OnCreate = (_, _) => failure;

        // Act
        var applied = await Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(_h.Admin, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure, Is.EqualTo(failure));
            Assert.That(read.Value.Status, Is.EqualTo(ProjectProposalStatus.Pending));
            Assert.That(read.Value.CreatedProjectKey, Is.Null);
        }
    }

    [Test]
    public async Task Apply_ByAPersonWhoIsNoLongerAnAdmin_ReturnsTheEnginesForbidden()
    {
        // Arrange
        var proposal = await CreatedAsync();
        var demoted = _h.Admin with { IsOrganizationAdmin = false };
        _h.Projects.OnCreate = (actor, _) => actor.IsOrganizationAdmin
            ? WorkSamples.Failure(FailureKind.Invalid, "x", "不該建立")
            : WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "只有組織管理員可以開新專案。");

        // Act
        var applied = await Proposals.ApplyAsync(demoted, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(applied.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task Apply_SomeoneElsesProposal_IsNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync();
        var other = new Actor(_h.OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: true);

        // Act
        var applied = await Proposals.ApplyAsync(other, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(other, proposal.Id, CancellationToken.None);
        var dismissed = await Proposals.DismissAsync(other, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectProposalNotFound));
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectProposalNotFound));
            Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectProposalNotFound));
        }
    }

    [Test]
    public async Task Apply_AnAlreadyDecidedProposal_IsConflict()
    {
        // Arrange
        var proposal = await CreatedAsync();
        _h.Projects.OnCreate = (_, project) => new ProjectSummary(Guid.NewGuid(), project.Key, project.Name, 1, "admin", DateTimeOffset.UnixEpoch);
        await Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None);

        // Act
        var again = await Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None);
        var dismissed = await Proposals.DismissAsync(_h.Admin, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectProposalClosed));
            Assert.That(again.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectProposalClosed));
        }
    }

    [Test]
    public async Task Dismiss_MarksItDismissedAndCreatesNothing()
    {
        // Arrange
        var proposal = await CreatedAsync();
        _h.Projects.OnCreate = (_, _) => throw new InvalidOperationException("不該建立專案");

        // Act
        var dismissed = await Proposals.DismissAsync(_h.Admin, proposal.Id, CancellationToken.None);
        var apply = await Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(ProjectProposalStatus.Dismissed));
            Assert.That(dismissed.Value.DecidedAt, Is.Not.Null);
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectProposalClosed));
        }
    }

    [Test]
    public async Task Apply_WhenTheProjectWasAlreadyCreatedByThisProposal_MarksItAppliedInsteadOfKeyTaken()
    {
        // Arrange：專案建好了、存 applied 失敗過，再按時工作引擎回 key_taken。
        var proposal = await CreatedAsync();
        _h.Projects.OnCreate = (_, _) => WorkSamples.Failure(FailureKind.Conflict, WorkErrorCodes.ProjectKeyTaken, "專案代號 NEWP 已經有人用了。");
        _h.Projects.OnGet = (_, key) => WorkSamples.Detail() with
        {
            Project = WorkSamples.Detail().Project with { Key = key, CreatedAt = proposal.CreatedAt.AddSeconds(1) },
            Members = [new ProjectMember(_h.Admin.PersonId, "admin")],
        };

        // Act
        var applied = await Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ProjectProposalStatus.Applied));
            Assert.That(applied.Value.CreatedProjectKey, Is.EqualTo("NEWP"));
        }
    }

    [Test]
    public async Task Apply_KeyTakenByAnOlderOrSomeoneElsesProject_StaysPendingWithKeyTaken()
    {
        // Arrange
        var proposal = await CreatedAsync();
        _h.Projects.OnCreate = (_, _) => WorkSamples.Failure(FailureKind.Conflict, WorkErrorCodes.ProjectKeyTaken, "專案代號 NEWP 已經有人用了。");
        _h.Projects.OnGet = (_, key) => WorkSamples.Detail() with
        {
            Project = WorkSamples.Detail().Project with { Key = key, CreatedAt = proposal.CreatedAt.AddDays(-1) },
            Members = [new ProjectMember(_h.Admin.PersonId, "admin")],
        };

        // Act
        var older = await Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None);
        _h.Projects.OnGet = (_, key) => WorkSamples.Detail() with
        {
            Project = WorkSamples.Detail().Project with { Key = key, CreatedAt = proposal.CreatedAt.AddSeconds(1) },
            Members = [new ProjectMember(Guid.NewGuid(), "admin")],
        };
        var someoneElses = await Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(older.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectKeyTaken));
            Assert.That(someoneElses.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectKeyTaken));
            Assert.That((await Proposals.GetAsync(_h.Admin, proposal.Id, CancellationToken.None)).Value.Status, Is.EqualTo(ProjectProposalStatus.Pending));
        }
    }

    [Test]
    public async Task Apply_TwoAtTheSameTime_CreatesTheProjectOnlyOnce()
    {
        // Arrange
        var proposal = await CreatedAsync();
        var created = 0;
        _h.Projects.OnCreate = (_, project) =>
        {
            Interlocked.Increment(ref created);
            return new ProjectSummary(Guid.NewGuid(), project.Key, project.Name, 1, "admin", DateTimeOffset.UnixEpoch);
        };

        // Act
        var results = await Task.WhenAll(
            Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None),
            Proposals.ApplyAsync(_h.Admin, proposal.Id, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.EqualTo(1));
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Single(r => !r.Succeeded).Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectProposalClosed));
        }
    }

    [Test]
    public async Task Create_SecondProposalInTheSameReply_IsRefusedForEveryPathAndAllowedAfterTheNextMessage()
    {
        // Arrange：引擎（本機與主機經 MCP）直接走 creator，帶對話編號。
        var conversation = await _h.NewOrganizationConversationAsync(_h.Admin);
        _h.Script.Reply("好");
        await _h.SendOrganizationAsync(conversation.Id, "開專案", _h.Admin);
        var first = await Creator.CreateAsync(_h.Admin, Draft("ONE") with { ConversationId = conversation.Id }, CancellationToken.None);

        // Act
        var second = await Creator.CreateAsync(_h.Admin, Draft("TWO") with { ConversationId = conversation.Id }, CancellationToken.None);
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        _h.Script.Reply("好");
        await _h.SendOrganizationAsync(conversation.Id, "再開一個", _h.Admin);
        var afterNextMessage = await Creator.CreateAsync(_h.Admin, Draft("TWO") with { ConversationId = conversation.Id }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Succeeded, Is.True, () => first.Failure?.Message ?? string.Empty);
            Assert.That(second.Failure?.Message, Does.Contain("一則回覆最多只能提出 1 份"));
            Assert.That(afterNextMessage.Succeeded, Is.True, () => afterNextMessage.Failure?.Message ?? string.Empty);
        }
    }

    [Test]
    public async Task Create_WithoutAConversation_IsNotLimitedPerReply()
    {
        // Act
        var first = await Creator.CreateAsync(_h.Admin, Draft("ONE"), CancellationToken.None);
        var second = await Creator.CreateAsync(_h.Admin, Draft("TWO"), CancellationToken.None);

        // Assert
        Assert.That(new[] { first.Succeeded, second.Succeeded }, Has.All.True);
    }
}
