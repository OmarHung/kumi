using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>不選專案的組織對話的工具額度與工作範圍：沒有專案代號時工作的對話也要沒有專案，反之亦然。</summary>
[TestFixture]
public sealed class AgentToolBudgetOrganizationTests
{
    private LocalAgentHarness _h = null!;
    private AiConnectionOption _engine = null!;
    private IAgentToolBudget Budget => _h.Host.Get<IAgentToolBudget>();

    [SetUp]
    public async Task StartAsync()
    {
        _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        _h.AgentVersion = "0.12.0";
        _engine = await _h.RegisterAsync();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private async Task<(LocalAgentJob Job, Guid ConversationId)> ClaimedOrganizationJobAsync()
    {
        var conversation = await _h.Assistant.NewOrganizationConversationAsync();
        var run = await _h.Assistant.StartOrganizationAsync(conversation.Id, "開專案", selection: LocalAgentHarness.Select(_engine));
        _ = run;
        return (await _h.ClaimAsync(), conversation.Id);
    }

    private async Task<(LocalAgentJob Job, Guid ConversationId)> ClaimedProjectJobAsync()
    {
        var run = await _h.SendAsync(_engine);
        return (await _h.ClaimAsync(), run.ConversationId);
    }

    [Test]
    public async Task UseLocal_NoProjectKeyOnAnOrganizationJob_CountsTheCalls()
    {
        // Arrange
        var (job, _) = await ClaimedOrganizationJobAsync();

        // Act
        var outcomes = new List<AgentToolBudgetOutcome>();
        for (var i = 0; i < AiRules.MaxAgentToolCallsPerJob + 1; i++)
        {
            outcomes.Add(await Budget.UseLocalAsync(_h.Me, job.JobId, null, isProposal: false, CancellationToken.None));
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcomes.Take(AiRules.MaxAgentToolCallsPerJob), Has.All.EqualTo(AgentToolBudgetOutcome.Allowed));
            Assert.That(outcomes[^1], Is.EqualTo(AgentToolBudgetOutcome.ToolCallsExhausted));
            Assert.That(_h.Assistant.Projects.Gets, Is.Empty, "沒有專案代號，不去問工作引擎");
        }
    }

    [Test]
    public async Task UseLocal_OrganizationJobUsedWithAProjectKey_IsNotFound()
    {
        // Arrange
        var (job, _) = await ClaimedOrganizationJobAsync();

        // Act
        var outcome = await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: false, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.JobNotFound));
            Assert.That((await _h.JobRowAsync(job.JobId)).ToolCalls, Is.Zero);
        }
    }

    [Test]
    public async Task UseLocal_ProjectJobUsedWithoutAProjectKey_IsNotFound()
    {
        // Arrange
        var (job, _) = await ClaimedProjectJobAsync();

        // Act
        var outcome = await Budget.UseLocalAsync(_h.Me, job.JobId, null, isProposal: false, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.JobNotFound));
            Assert.That((await _h.JobRowAsync(job.JobId)).ToolCalls, Is.Zero);
        }
    }

    [Test]
    public async Task FindJobScope_LocalOrganizationJob_HasTheConversationAndNoProject()
    {
        // Arrange
        var (job, conversationId) = await ClaimedOrganizationJobAsync();

        // Act
        var scope = await Budget.FindJobScopeAsync(AgentJobKind.Local, _h.Me, job.JobId, CancellationToken.None);

        // Assert
        Assert.That(scope, Is.EqualTo(new AgentJobScope(conversationId, null)));
    }

    [Test]
    public async Task FindJobScope_LocalProjectJob_HasTheProjectKey()
    {
        // Arrange
        _h.Assistant.Projects.OnList = _ => [WorkSamples.Detail().Project];
        var (job, conversationId) = await ClaimedProjectJobAsync();

        // Act
        var scope = await Budget.FindJobScopeAsync(AgentJobKind.Local, _h.Me, job.JobId, CancellationToken.None);

        // Assert
        Assert.That(scope, Is.EqualTo(new AgentJobScope(conversationId, "WEB")));
    }

    [Test]
    public async Task FindJobScope_LocalProjectTheCallerCanNoLongerSee_IsNull()
    {
        // Arrange
        _h.Assistant.Projects.OnList = _ => [];
        var (job, _) = await ClaimedProjectJobAsync();

        // Act
        var scope = await Budget.FindJobScopeAsync(AgentJobKind.Local, _h.Me, job.JobId, CancellationToken.None);

        // Assert
        Assert.That(scope, Is.Null);
    }

    [Test]
    public async Task FindJobScope_AnotherPersonUnclaimedOrFinishedJob_IsNull()
    {
        // Arrange
        var conversation = await _h.Assistant.NewOrganizationConversationAsync();
        await _h.Assistant.StartOrganizationAsync(conversation.Id, "開專案", selection: LocalAgentHarness.Select(_engine));
        await _h.WaitForJobRowsAsync(1);
        await using var db = await _h.Host.CreateDbContextAsync();
        var waiting = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.LocalJobs);
        var beforeClaim = await Budget.FindJobScopeAsync(AgentJobKind.Local, _h.Me, waiting.Id, CancellationToken.None);
        var job = await _h.ClaimAsync();
        var stranger = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);

        // Act
        var asStranger = await Budget.FindJobScopeAsync(AgentJobKind.Local, stranger, job.JobId, CancellationToken.None);
        var withoutActor = await Budget.FindJobScopeAsync(AgentJobKind.Local, null, job.JobId, CancellationToken.None);
        await _h.FinishAsync(job.JobId);
        var afterFinish = await Budget.FindJobScopeAsync(AgentJobKind.Local, _h.Me, job.JobId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(beforeClaim, Is.Null);
            Assert.That(asStranger, Is.Null);
            Assert.That(withoutActor, Is.Null);
            Assert.That(afterFinish, Is.Null);
        }
    }
}
