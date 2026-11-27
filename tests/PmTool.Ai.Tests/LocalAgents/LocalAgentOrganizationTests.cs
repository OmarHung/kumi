using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>本機代理與不選專案的組織對話：工作沒有專案、只有四個工具、組織版的系統提示；kumi 太舊送出就擋下。</summary>
[TestFixture]
public sealed class LocalAgentOrganizationTests
{
    private LocalAgentHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private async Task<AiConnectionOption> RegisterAsync(string version)
    {
        _h.AgentVersion = version;
        return await _h.RegisterAsync();
    }

    private async Task<LocalRun> SendOrganizationAsync(AiConnectionOption engine, Actor? actor = null)
    {
        var conversation = await _h.Assistant.NewOrganizationConversationAsync(actor);
        var run = await _h.Assistant.StartOrganizationAsync(conversation.Id, "幫我開專案", actor, LocalAgentHarness.Select(engine));
        return new LocalRun(conversation.Id, run);
    }

    [Test]
    public async Task Job_OfAnOrganizationChat_HasNoProjectTheOrganizationToolsAndPrompt()
    {
        // Arrange
        var engine = await RegisterAsync("0.12.0");
        var run = await SendOrganizationAsync(engine);

        // Act
        var job = await _h.ClaimAsync();
        await _h.FinishAsync(job.JobId);
        await run.ReadAllAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.ProjectKey, Is.Null);
            Assert.That(job.AllowedTools, Is.EqualTo(LocalAgentTools.AllowedForOrganization.Where(t => t != "withdraw_proposal")));
            Assert.That(job.SystemPrompt, Does.StartWith(AssistantPrompt.OrganizationSystemPrompt));
            Assert.That(job.SystemPrompt, Does.Contain("使用者不能開新專案").And.Contain("你只能用下面列的這些"));
            Assert.That(job.SystemPrompt, Does.Not.Contain("preview_changeset"));
            Assert.That(job.SystemPrompt, Does.Not.Contain("- withdraw_proposal："));
            Assert.That(job.SystemPrompt, Does.Contain("這一次沒有 withdraw_proposal 這 1 個工具").And.Contain("kumi update"));
        }
    }

    [Test]
    public async Task Job_OfAnOrganizationChatOnAKumiWithRevisionTools_GetsWithdrawProposal()
    {
        // Arrange
        var engine = await RegisterAsync(AiRules.MinAgentVersionForRevisionTools);
        var run = await SendOrganizationAsync(engine);

        // Act
        var job = await _h.ClaimAsync();
        await _h.FinishAsync(job.JobId);
        await run.ReadAllAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.AllowedTools, Is.EqualTo(LocalAgentTools.AllowedForOrganization));
            Assert.That(job.SystemPrompt, Does.Contain("- withdraw_proposal："));
            Assert.That(job.SystemPrompt, Does.Not.Contain("這一次沒有 withdraw_proposal"));
        }
    }

    [Test]
    public async Task Job_OfAnAdminsOrganizationChat_TellsTheModelTheyMayOpenProjects()
    {
        // Arrange
        var engine = await RegisterAsync("0.12.0");
        var run = await SendOrganizationAsync(engine, _h.Me with { IsOrganizationAdmin = true });

        // Act
        var job = await _h.ClaimAsync(_h.Me with { IsOrganizationAdmin = true });

        // Assert
        Assert.That(job.SystemPrompt, Does.Contain("使用者能開新專案"));
        await run.StopAsync();
    }

    [Test]
    public async Task Start_WithAnOldKumi_IsRefusedAndStoresNothing()
    {
        // Arrange
        var engine = await RegisterAsync("0.11.0");
        var conversation = await _h.Assistant.NewOrganizationConversationAsync();

        // Act
        var started = await _h.Assistant.Service.StartAsync(
            _h.Me, null, conversation.Id, new AssistantMessageInput("幫我開專案", LocalAgentHarness.Select(engine)), CancellationToken.None);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentUpdateRequired));
            Assert.That(started.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(started.Failure?.Message, Does.Contain("kumi update").And.Contain("0.12.0"));
            Assert.That((await _h.Assistant.ConversationRowAsync(conversation.Id)).MessageCount, Is.Zero);
            Assert.That(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.LocalJobs), Is.Zero);
        }
    }

    [Test]
    public async Task Start_ProjectChatWithAnOldKumi_IsStillAllowed()
    {
        // Arrange
        var engine = await RegisterAsync("0.9.0");

        // Act
        var run = await _h.SendAsync(engine);

        // Assert
        Assert.That(run.Run.UserMessage, Is.Not.Null);
    }

    [Test]
    public async Task Report_ProjectProposalId_IsKeptOnlyWhenItIsTheCallersOwn()
    {
        // Arrange
        var engine = await RegisterAsync("0.12.0");
        var admin = _h.Me with { IsOrganizationAdmin = true };
        var mine = await ProposalOfAsync(admin);
        var others = await ProposalOfAsync(new Actor(admin.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true));
        var run = await SendOrganizationAsync(engine, admin);
        var job = await _h.ClaimAsync(admin);

        // Act
        await _h.Agents.ReportAsync(
            admin,
            job.JobId,
            [
                new LocalAgentTool(new AssistantToolPart("t1", "propose_project", AssistantToolStatus.Done, null, null) { ProjectProposalId = mine }),
                new LocalAgentTool(new AssistantToolPart("t2", "propose_project", AssistantToolStatus.Done, null, null) { ProjectProposalId = others }),
                new LocalAgentTool(new AssistantToolPart("t3", "propose_project", AssistantToolStatus.Done, null, null) { ProjectProposalId = Guid.NewGuid() }),
            ],
            CancellationToken.None);
        await _h.Agents.FinishAsync(admin, job.JobId, new LocalAgentFinish(AssistantMessageStatus.Complete, null, null), CancellationToken.None);
        var events = await run.ReadAllAsync();

        // Assert
        var tools = events.OfType<AssistantRunCompleted>().Single().Message.Parts.OfType<AssistantToolPart>().ToList();
        var stored = (await _h.Assistant.OrganizationMessagesAsync(run.ConversationId, admin)).Last().Parts.OfType<AssistantToolPart>().ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(t => t.ProjectProposalId), Is.EqualTo(new Guid?[] { mine, null, null }));
            Assert.That(stored.Select(t => t.ProjectProposalId), Is.EqualTo(new Guid?[] { mine, null, null }));
        }
    }

    private async Task<Guid> ProposalOfAsync(Actor admin)
    {
        var created = await _h.Host.Get<IProjectProposalCreator>().CreateAsync(
            admin, new ProjectProposalDraft("NEWP", "新專案", "simple_tasks", false, null), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value.Id;
    }
}
