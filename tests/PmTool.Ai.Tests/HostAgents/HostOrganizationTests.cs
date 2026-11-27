using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.LocalAgents;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>主機代理與不選專案的組織對話：工作與用量沒有專案、工具授權的專案是 null、worker 太舊送出就擋下。</summary>
[TestFixture]
public sealed class HostOrganizationTests
{
    private HostHarness _h = null!;
    private HostSetup _setup = null!;

    [SetUp]
    public async Task SetUp()
    {
        _h = await HostHarness.StartAsync();
        _setup = await _h.SetUpAsync();
        _setup.Worker.AgentVersion = "0.12.0";
        await _h.CheckInAsync(_setup.Worker);
    }

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    [Test]
    public async Task Job_OfAnOrganizationChat_HasNoProjectTheOrganizationToolsAndPrompt()
    {
        // Arrange
        await _h.SendAsync(_h.Member, _setup.Source, projectKey: null);

        // Act
        var job = await _h.ClaimAsync(_setup.Worker);

        // Assert
        var row = await _h.JobRowAsync(job.JobId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.ProjectKey, Is.Null);
            Assert.That(row.ProjectKey, Is.Null);
            Assert.That(job.AllowedTools, Is.EqualTo(LocalAgentTools.AllowedForOrganization.Where(t => t is not ("withdraw_proposal" or "my_work" or "find_items" or "list_people"))));
            Assert.That(job.SystemPrompt, Does.StartWith(AssistantPrompt.OrganizationSystemPrompt));
            Assert.That(job.SystemPrompt, Does.Contain("使用者不能開新專案").And.Contain("你執行在 Kumi 的主機上"));
        }
    }

    [Test]
    public async Task Grant_OfAnOrganizationChatJob_HasANullProjectAndTheOrganizationTools()
    {
        // Arrange
        await _h.SendAsync(_h.Member, _setup.Source, projectKey: null);
        var job = await _h.ClaimAsync(_setup.Worker);

        // Act
        var grant = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(grant, Is.Not.Null);
            Assert.That(grant!.ProjectKey, Is.Null);
            Assert.That(grant.AllowedTools, Is.EqualTo(LocalAgentTools.AllowedForOrganization.Where(t => t is not ("withdraw_proposal" or "my_work" or "find_items" or "list_people"))));
        }
    }

    [Test]
    public async Task Usage_OfAnOrganizationChat_IsItsOwnGroupWithANullProject()
    {
        // Arrange
        await _h.SendAsync(_h.Member, _setup.Source, projectKey: null);
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, job);
        await _h.SendAsync(_h.Member2, _setup.Source);
        var second = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, second);

        // Act
        var usage = await _h.Service.GetUsageAsync(_h.Owner, _setup.Source.Id, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(usage.Succeeded, Is.True, () => usage.Failure?.Message ?? string.Empty);
            Assert.That(usage.Value.Projects.Select(p => p.ProjectKey), Is.EquivalentTo(new string?[] { null, "WEB" }));
            Assert.That(usage.Value.Projects.Single(p => p.ProjectKey is null).Totals.Requests, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Start_WithAnOldWorker_IsRefusedAndReservesNothing()
    {
        // Arrange
        _setup.Worker.AgentVersion = "0.11.0";
        await _h.CheckInAsync(_setup.Worker);

        // Act
        var started = await _h.TrySendAsync(_h.Member, _setup.Source, projectKey: null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentUpdateRequired));
            Assert.That(started.Failure?.Message, Does.Contain("ai-worker").And.Contain("0.12.0"));
            Assert.That(await _h.JobRowCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Start_ProjectChatWithAnOldWorker_IsStillAllowed()
    {
        // Arrange
        _setup.Worker.AgentVersion = "0.9.0";
        await _h.CheckInAsync(_setup.Worker);

        // Act
        var started = await _h.TrySendAsync(_h.Member, _setup.Source);

        // Assert
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task FindJobScope_HostJob_HasTheConversationAndTheJobsProjectOrNone()
    {
        // Arrange：一個來源同時只跑一件工作，所以第一件結束才送第二件。
        var budget = _h.Host.Get<IAgentToolBudget>();
        await _h.SendAsync(_h.Member, _setup.Source, projectKey: null);
        var organizationJob = await _h.ClaimAsync(_setup.Worker);

        // Act
        var organization = await budget.FindJobScopeAsync(AgentJobKind.Host, null, organizationJob.JobId, CancellationToken.None);
        await _h.FinishAsync(_setup.Worker, organizationJob);
        var finished = await budget.FindJobScopeAsync(AgentJobKind.Host, null, organizationJob.JobId, CancellationToken.None);
        await _h.SendAsync(_h.Member2, _setup.Source);
        var projectJob = await _h.ClaimAsync(_setup.Worker);
        var project = await budget.FindJobScopeAsync(AgentJobKind.Host, null, projectJob.JobId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(organization!.ProjectKey, Is.Null);
            Assert.That(organization.ConversationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(project!.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(finished, Is.Null);
        }
    }

    [Test]
    public async Task UseHost_OrganizationJob_CountsTheCalls()
    {
        // Arrange
        await _h.SendAsync(_h.Member, _setup.Source, projectKey: null);
        var job = await _h.ClaimAsync(_setup.Worker);

        // Act
        var outcome = await _h.Host.Get<IAgentToolBudget>().UseHostAsync(job.JobId, isProposal: true, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.Allowed));
            Assert.That((await _h.JobRowAsync(job.JobId)).ToolCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Report_ProjectProposalIdThatIsNotTheAskers_IsDropped()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source, projectKey: null);
        var job = await _h.ClaimAsync(_setup.Worker);

        // Act
        await _h.ReportAsync(
            _setup.Worker, job, 1,
            new LocalAgentTool(new AssistantToolPart("c1", "propose_project", AssistantToolStatus.Done, null, null) { ProjectProposalId = Guid.NewGuid() }));
        await _h.FinishAsync(_setup.Worker, job);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(events.OfType<AssistantToolUpdate>().Single().Tool.ProjectProposalId, Is.Null);
    }
}
