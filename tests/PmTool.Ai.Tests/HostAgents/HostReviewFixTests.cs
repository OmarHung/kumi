using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.HostAgents;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>審查後補的規則：沒開始執行的不算用量、結果不明只給回報過的、token 上限、結束清掉對話、停用代理照長輪詢、登入隨代理狀態作廢。</summary>
[TestFixture]
public sealed class HostReviewFixTests
{
    private HostHarness _h = null!;
    private HostSetup _setup = null!;

    [SetUp]
    public async Task SetUp()
    {
        _h = await HostHarness.StartAsync();
        _setup = await _h.SetUpAsync();
    }

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private async Task RestartAsync(HostTimings timings)
    {
        await _h.DisposeAsync();
        _h = await HostHarness.StartAsync(timings);
        _setup = await _h.SetUpAsync();
    }

    [Test]
    public async Task Silent_worker_that_reported_before_is_an_unknown_outcome_and_still_counts()
    {
        // Arrange
        await RestartAsync(HostHarness.FastTimings with { LeaseTimeout = TimeSpan.FromMilliseconds(500) });
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        await _h.ReportAsync(_setup.Worker, job, 1, new LocalAgentText("開始"));

        // Act
        var events = await run.ReadAllAsync();

        // Assert
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
        Assert.That(final.Error, Does.Contain("結果不明"));
        Assert.That((await _h.SourceAsync(_setup.Source.Id)).Usage.Requests, Is.EqualTo(1));
    }

    [Test]
    public async Task Failure_without_any_progress_or_usage_is_not_counted_but_with_progress_or_usage_it_is()
    {
        // Arrange
        var first = await _h.SendAsync(_h.Member, _setup.Source);
        var noProgress = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, noProgress, AssistantMessageStatus.Failed, "沒跑起來");
        await first.ReadAllAsync();
        var afterFirst = (await _h.SourceAsync(_setup.Source.Id)).Usage.Requests;
        var second = await _h.SendAsync(_h.Member, _setup.Source);
        var withUsage = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, withUsage, AssistantMessageStatus.Failed, "有用量", new AssistantUsage(1, 1));
        await second.ReadAllAsync();

        // Act
        var requests = (await _h.SourceAsync(_setup.Source.Id)).Usage.Requests;

        // Assert
        Assert.That(afterFirst, Is.EqualTo(0));
        Assert.That(requests, Is.EqualTo(1));
    }

    [TestCase(1_000_000_001L, 0L)]
    [TestCase(0L, 1_000_000_001L)]
    [TestCase(-1L, 0L)]
    public async Task Absurd_token_numbers_are_refused_as_invalid_and_the_job_stays_finishable(long input, long output)
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);

        // Act
        var refused = await _h.Worker.FinishAsync(
            _setup.Worker.Identity, job.JobId, job.Lease, new LocalAgentFinish(AssistantMessageStatus.Complete, null, new AssistantUsage(input, output)), default);
        await _h.FinishAsync(_setup.Worker, job, usage: new AssistantUsage(1_000_000_000, 1_000_000_000));
        await run.ReadAllAsync();

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentInvalid));
        Assert.That((await _h.SourceAsync(_setup.Source.Id)).Usage.InputTokens, Is.EqualTo(1_000_000_000L));
    }

    [Test]
    public async Task Finishing_clears_the_stored_conversation_from_the_job_row_at_once()
    {
        // Arrange
        await RestartAsync(HostHarness.FastTimings with { PollInterval = TimeSpan.FromSeconds(3) });
        var run = await _h.SendAsync(_h.Member, _setup.Source, "機密的對話");
        var job = await _h.ClaimAsync(_setup.Worker);
        var before = (await _h.JobRowAsync(job.JobId)).Payload;

        // Act
        await _h.FinishAsync(_setup.Worker, job);
        var after = await _h.JobRowAsync(job.JobId);

        // Assert
        Assert.That(before, Does.Contain("機密的對話"));
        Assert.That(after.Payload, Is.Null);
        Assert.That(after.State, Is.EqualTo(PmTool.Ai.Persistence.HostJobStates.Finished));
        await run.StopAsync();
    }

    [Test]
    public async Task Disabled_agent_check_in_waits_the_poll_time_before_saying_disabled_and_can_be_cancelled()
    {
        // Arrange
        await _h.Service.UpdateAgentAsync(_h.Owner, _setup.Worker.AgentId, new HostAgentInput("主機代理") { Enabled = false }, default);
        var started = DateTimeOffset.UtcNow;

        // Act
        var waited = await _h.CheckInAsync(_setup.Worker, capacity: 1);
        var elapsed = DateTimeOffset.UtcNow - started;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var cancelledAt = DateTimeOffset.UtcNow;
        var cancelled = await _h.CheckInAsync(_setup.Worker, capacity: 1, cancel.Token);
        var cancelledAfter = DateTimeOffset.UtcNow - cancelledAt;
        var noWait = DateTimeOffset.UtcNow;
        await _h.CheckInAsync(_setup.Worker, capacity: 0);

        // Assert
        Assert.That(waited.Disabled, Is.True);
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400)));
        Assert.That(cancelled.Disabled, Is.True);
        Assert.That(cancelledAfter, Is.LessThan(TimeSpan.FromMilliseconds(400)));
        Assert.That(DateTimeOffset.UtcNow - noWait, Is.LessThan(TimeSpan.FromMilliseconds(300)));
    }

    public static IEnumerable<TestCaseData> AgentChanges() =>
    [
        new TestCaseData("reset").SetArgDisplayNames("reset_pairing"),
        new TestCaseData("disable").SetArgDisplayNames("disable"),
    ];

    [TestCaseSource(nameof(AgentChanges))]
    public async Task Pending_and_claimed_logins_are_cancelled_when_the_agent_is_reset_or_disabled(string change)
    {
        // Arrange
        var pending = (await _h.Service.StartLoginAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.Codex, default)).Value;
        var claimed = (await _h.Service.StartLoginAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.ClaudeCode, default)).Value;
        await _h.Worker.ClaimLoginAsync(_setup.Worker.Identity, claimed.Code, default);

        // Act
        if (change == "reset")
        {
            await _h.Service.ResetPairingAsync(_h.Owner, _setup.Worker.AgentId, default);
        }
        else
        {
            await _h.Service.UpdateAgentAsync(_h.Owner, _setup.Worker.AgentId, new HostAgentInput("主機代理") { Enabled = false }, default);
        }

        var shown = (await _h.AgentAsync(_setup.Worker.AgentId)).Logins;
        if (change == "disable")
        {
            await _h.Service.UpdateAgentAsync(_h.Owner, _setup.Worker.AgentId, new HostAgentInput("主機代理") { Enabled = true }, default);
        }

        var claim = change == "disable"
            ? await _h.Worker.ClaimLoginAsync(_setup.Worker.Identity, pending.Code, default)
            : default;
        var complete = change == "disable"
            ? await _h.Worker.CompleteLoginAsync(_setup.Worker.Identity, claimed.Login.Id, new HostLoginReport(true, HostBilling.Api, null), default)
            : default;

        // Assert
        Assert.That(shown, Is.Empty);
        if (change == "disable")
        {
            Assert.That(claim.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
            Assert.That(complete.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
        }
    }

    [Test]
    public async Task Claim_and_complete_need_an_active_agent()
    {
        // Arrange
        var start = (await _h.Service.StartLoginAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.Codex, default)).Value;
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            await db.HostAgents.Where(a => a.Id == _setup.Worker.AgentId)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.State, PmTool.Ai.Persistence.HostAgentStates.Disabled));
        }

        // Act
        var claim = await _h.Worker.ClaimLoginAsync(_setup.Worker.Identity, start.Code, default);

        // Assert
        Assert.That(claim.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Removing_an_agent_with_a_pending_login_leaves_no_login_behind()
    {
        // Arrange
        var start = (await _h.Service.StartLoginAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.Codex, default)).Value;

        // Act
        await _h.Service.RemoveAgentAsync(_h.Owner, _setup.Worker.AgentId, default);
        var claim = await _h.Worker.ClaimLoginAsync(_setup.Worker.Identity, start.Code, default);

        // Assert
        Assert.That(claim.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Pairing_and_login_codes_are_hidden_in_text_output()
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync("藏碼");
        var login = (await _h.Service.StartLoginAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.Codex, default)).Value;

        // Act
        var texts = new[] { pairing.ToString(), login.ToString() };

        // Assert
        Assert.That(texts[0], Does.Not.Contain(pairing.PairingCode));
        Assert.That(texts[1], Does.Not.Contain(login.Code));
    }
}
