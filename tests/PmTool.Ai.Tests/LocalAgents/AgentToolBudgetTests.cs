using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>本機代理一件工作的工具額度（AG-07）：伺服器端計，工作與專案都要對得上，提出類的另外有名額。</summary>
[TestFixture]
public sealed class AgentToolBudgetTests
{
    private LocalAgentHarness _h = null!;
    private AiConnectionOption _engine = null!;
    private IAgentToolBudget Budget => _h.Host.Get<IAgentToolBudget>();

    [SetUp]
    public async Task StartAsync()
    {
        _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        _engine = await _h.RegisterAsync();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task UseLocal_ThirteenCalls_AreAllowedAndTheFourteenthIsRefused()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var outcomes = new List<AgentToolBudgetOutcome>();
        for (var i = 0; i < AiRules.MaxAgentToolCallsPerJob + 1; i++)
        {
            outcomes.Add(await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: false, CancellationToken.None));
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcomes.Take(AiRules.MaxAgentToolCallsPerJob), Has.All.EqualTo(AgentToolBudgetOutcome.Allowed));
            Assert.That(outcomes[^1], Is.EqualTo(AgentToolBudgetOutcome.ToolCallsExhausted));
            Assert.That((await _h.JobRowAsync(job.JobId)).ToolCalls, Is.EqualTo(AiRules.MaxAgentToolCallsPerJob));
        }
    }

    [Test]
    public async Task UseLocal_ConcurrentCalls_NeverExceedTheLimit()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ =>
            Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: false, CancellationToken.None)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcomes.Count(o => o == AgentToolBudgetOutcome.Allowed), Is.EqualTo(AiRules.MaxAgentToolCallsPerJob));
            Assert.That((await _h.JobRowAsync(job.JobId)).ToolCalls, Is.EqualTo(AiRules.MaxAgentToolCallsPerJob));
        }
    }

    [Test]
    public async Task UseLocal_ThirdProposal_IsRefusedButOtherToolsStillWork()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var first = await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: true, CancellationToken.None);
        var second = await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: true, CancellationToken.None);
        var third = await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: true, CancellationToken.None);
        var read = await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: false, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new[] { first, second }, Has.All.EqualTo(AgentToolBudgetOutcome.Allowed));
            Assert.That(third, Is.EqualTo(AgentToolBudgetOutcome.ProposalsExhausted));
            Assert.That(read, Is.EqualTo(AgentToolBudgetOutcome.Allowed));
            Assert.That((await _h.JobRowAsync(job.JobId)).Proposals, Is.EqualTo(AiRules.MaxAgentProposalsPerJob));
        }
    }

    [Test]
    public async Task RefundProposal_GivesTheSlotBackButNotTheCall()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: true, CancellationToken.None);
        await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: true, CancellationToken.None);

        // Act
        await Budget.RefundProposalAsync(AgentJobKind.Local, job.JobId);
        var again = await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: true, CancellationToken.None);
        var row = await _h.JobRowAsync(job.JobId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again, Is.EqualTo(AgentToolBudgetOutcome.Allowed));
            Assert.That(row.Proposals, Is.EqualTo(2));
            Assert.That(row.ToolCalls, Is.EqualTo(3), "呼叫的次數不退");
        }
    }

    [Test]
    public async Task UseLocal_JobNotClaimedYet_IsNotFound()
    {
        // Arrange
        await _h.SendAsync(_engine);
        await _h.WaitForJobRowsAsync(1);
        await using var db = await _h.Host.CreateDbContextAsync();
        var waiting = await db.LocalJobs.AsNoTracking().SingleAsync();

        // Act
        var outcome = await Budget.UseLocalAsync(_h.Me, waiting.Id, "WEB", isProposal: false, CancellationToken.None);

        // Assert
        Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.JobNotFound));
    }

    [Test]
    public async Task UseLocal_AnotherPersonsJob_IsNotFound()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);

        // Act
        var outcome = await Budget.UseLocalAsync(other, job.JobId, "WEB", isProposal: false, CancellationToken.None);

        // Assert
        Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.JobNotFound));
    }

    [Test]
    public async Task UseLocal_ProjectOfTheHeaderIsNotTheJobsProject_IsNotFound()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        _h.Assistant.Projects.OnGet = (_, key) => key == "OTHER"
            ? WorkSamples.Detail() with
            {
                Project = WorkSamples.Detail().Project with { Id = Guid.NewGuid(), Key = "OTHER" },
            }
            : WorkSamples.Detail();

        // Act
        var outcome = await Budget.UseLocalAsync(_h.Me, job.JobId, "OTHER", isProposal: false, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.JobNotFound));
            Assert.That((await _h.JobRowAsync(job.JobId)).ToolCalls, Is.Zero);
        }
    }

    [Test]
    public async Task UseLocal_ProjectTheCallerCannotSee_IsNotFound()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        _h.Assistant.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, "project.not_found", "找不到專案。");

        // Act
        var outcome = await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: false, CancellationToken.None);

        // Assert
        Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.JobNotFound));
    }

    [Test]
    public async Task UseLocal_FinishedJob_IsNotFound()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        await _h.FinishAsync(job.JobId);

        // Act
        var outcome = await Budget.UseLocalAsync(_h.Me, job.JobId, "WEB", isProposal: false, CancellationToken.None);

        // Assert
        Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.JobNotFound));
    }

    [Test]
    public async Task UseLocal_UnknownJob_IsNotFound()
    {
        // Act
        var outcome = await Budget.UseLocalAsync(_h.Me, Guid.NewGuid(), "WEB", isProposal: false, CancellationToken.None);

        // Assert
        Assert.That(outcome, Is.EqualTo(AgentToolBudgetOutcome.JobNotFound));
    }

    private async Task<LocalAgentJob> ClaimedJobAsync()
    {
        await _h.SendAsync(_engine);
        return await _h.ClaimAsync();
    }
}
