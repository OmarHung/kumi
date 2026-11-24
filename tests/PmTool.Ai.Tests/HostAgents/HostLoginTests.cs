using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>登入：網頁開始、主機認領、CLI 完成。每一步都重新確認開始的人還是擁有者；只有 API 計費的登入能成為授權。</summary>
[TestFixture]
public sealed class HostLoginTests
{
    private HostHarness _h = null!;
    private HostWorker _worker = null!;

    [SetUp]
    public async Task SetUp()
    {
        _h = await HostHarness.StartAsync();
        _worker = await _h.PairAsync();
    }

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private async Task<HostLoginStart> StartAsync(AiProviderKind engine = AiProviderKind.ClaudeCode)
    {
        var started = await _h.Service.StartLoginAsync(_h.Owner, _worker.AgentId, engine, default);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        return started.Value;
    }

    private Task<Result<HostLoginCompleted>> CompleteAsync(Guid loginId, bool loggedIn = true, HostBilling billing = HostBilling.Api) =>
        _h.Worker.CompleteLoginAsync(_worker.Identity, loginId, new HostLoginReport(loggedIn, billing, null), default);

    [Test]
    public async Task Full_login_makes_the_engine_ready_once_the_worker_reports_the_login_id()
    {
        // Arrange
        var start = await StartAsync();

        // Act
        var claimed = await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);
        var completed = await CompleteAsync(start.Login.Id);
        _worker.Reports = [HostHarness.Report(loginId: completed.Value.LoginId)];
        await _h.CheckInAsync(_worker);

        // Assert
        Assert.That(claimed.Value.LoginId, Is.EqualTo(start.Login.Id));
        Assert.That(claimed.Value.Engine, Is.EqualTo(AiProviderKind.ClaudeCode));
        var engine = (await _h.AgentAsync(_worker.AgentId)).Engines.Single();
        Assert.That(engine.Readiness, Is.EqualTo(HostEngineReadiness.Ready));
        Assert.That(engine.AuthorizedBy, Is.EqualTo(_h.Owner.PersonId));
        Assert.That(engine.AuthorizedAt, Is.Not.Null);
        Assert.That(engine.Billing, Is.EqualTo(HostBilling.Api));
    }

    [Test]
    public async Task Cli_logged_in_outside_kumi_is_not_authorized()
    {
        // Arrange
        _worker.Reports = [HostHarness.Report(loginId: Guid.NewGuid())];

        // Act
        await _h.CheckInAsync(_worker);

        // Assert
        var engine = (await _h.AgentAsync(_worker.AgentId)).Engines.Single();
        Assert.That(engine.Readiness, Is.EqualTo(HostEngineReadiness.NotAuthorized));
    }

    [Test]
    public async Task Login_code_cannot_be_claimed_twice()
    {
        // Arrange
        var start = await StartAsync();
        await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);

        // Act
        var replay = await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);

        // Assert
        Assert.That(replay.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Concurrent_claims_of_one_code_succeed_exactly_once()
    {
        // Arrange
        var start = await StartAsync();

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default)));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
    }

    [Test]
    public async Task Expired_login_code_cannot_be_claimed()
    {
        // Arrange
        var start = await StartAsync();
        _h.Clock.Advance(AiRules.HostLoginLifetime + TimeSpan.FromSeconds(1));

        // Act
        var claimed = await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);

        // Assert
        Assert.That(claimed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Login_that_expires_after_claiming_is_rejected_on_completion()
    {
        // Arrange
        var start = await StartAsync();
        await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);
        _h.Clock.Advance(AiRules.HostLoginLifetime + TimeSpan.FromSeconds(1));

        // Act
        var completed = await CompleteAsync(start.Login.Id);

        // Assert
        Assert.That(completed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Another_agents_code_is_rejected()
    {
        // Arrange
        var other = await _h.PairAsync("第二台");
        var start = await StartAsync();

        // Act
        var claimed = await _h.Worker.ClaimLoginAsync(other.Identity, start.Code, default);

        // Assert
        Assert.That(claimed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Claim_is_rejected_when_the_starter_was_demoted()
    {
        // Arrange
        var start = await StartAsync();
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Admin);

        // Act
        var claimed = await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);

        // Assert
        Assert.That(claimed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Completion_is_rejected_when_the_starter_was_demoted_after_claiming_and_the_login_does_not_count()
    {
        // Arrange
        var start = await StartAsync();
        await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Admin);

        // Act
        var completed = await CompleteAsync(start.Login.Id);

        // Assert
        Assert.That(completed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Owner);
        var agent = await _h.AgentAsync(_worker.AgentId);
        Assert.That(agent.Logins.Single().State, Is.EqualTo(HostLoginState.Failed));
        Assert.That(agent.Logins.Single().Error, Does.Contain("擁有者"));
    }

    [TestCase(HostBilling.Subscription)]
    [TestCase(HostBilling.Unknown)]
    public async Task Subscription_or_unknown_billing_is_rejected_and_never_becomes_the_authorization(HostBilling billing)
    {
        // Arrange
        var start = await StartAsync();
        await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);

        // Act
        var completed = await CompleteAsync(start.Login.Id, billing: billing);
        _worker.Reports = [HostHarness.Report(billing: billing, loginId: start.Login.Id)];
        await _h.CheckInAsync(_worker);

        // Assert
        Assert.That(completed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
        Assert.That(completed.Failure!.Message, Does.Contain("訂閱"));
        var engine = (await _h.AgentAsync(_worker.AgentId)).Engines.Single();
        Assert.That(engine.Readiness, Is.EqualTo(HostEngineReadiness.BillingNotShareable));
        Assert.That(engine.AuthorizedAt, Is.Null);
    }

    [Test]
    public async Task Completion_without_a_logged_in_cli_is_rejected()
    {
        // Arrange
        var start = await StartAsync();
        await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);

        // Act
        var completed = await CompleteAsync(start.Login.Id, loggedIn: false);

        // Assert
        Assert.That(completed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Completion_replay_is_rejected_and_keeps_the_first_authorization()
    {
        // Arrange
        var start = await StartAsync();
        await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);
        await CompleteAsync(start.Login.Id);

        // Act
        var replay = await CompleteAsync(start.Login.Id);
        _worker.Reports = [HostHarness.Report(loginId: start.Login.Id)];
        await _h.CheckInAsync(_worker);

        // Assert
        Assert.That(replay.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
        Assert.That((await _h.AgentAsync(_worker.AgentId)).Engines.Single().Readiness, Is.EqualTo(HostEngineReadiness.Ready));
    }

    [Test]
    public async Task Completing_a_login_that_was_never_claimed_is_rejected()
    {
        // Arrange
        var start = await StartAsync();

        // Act
        var completed = await CompleteAsync(start.Login.Id);

        // Assert
        Assert.That(completed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
    }

    [Test]
    public async Task Unknown_login_is_not_found_for_the_worker()
    {
        // Arrange
        // Act
        var completed = await CompleteAsync(Guid.NewGuid());

        // Assert
        Assert.That(completed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginNotFound));
    }

    [Test]
    public async Task Starting_a_new_login_cancels_the_pending_one_for_the_same_engine()
    {
        // Arrange
        var first = await StartAsync();

        // Act
        var second = await StartAsync();
        var oldClaim = await _h.Worker.ClaimLoginAsync(_worker.Identity, first.Code, default);

        // Assert
        Assert.That(oldClaim.Failure?.Code, Is.EqualTo(AiErrorCodes.HostLoginRejected));
        var logins = (await _h.AgentAsync(_worker.AgentId)).Logins;
        Assert.That(logins.Select(l => l.Id), Is.EqualTo(new[] { second.Login.Id }));
    }

    [Test]
    public async Task Different_engines_can_log_in_at_the_same_time()
    {
        // Arrange
        // Act
        var claude = await StartAsync(AiProviderKind.ClaudeCode);
        var codex = await StartAsync(AiProviderKind.Codex);

        // Assert
        Assert.That((await _h.AgentAsync(_worker.AgentId)).Logins, Has.Count.EqualTo(2));
        Assert.That((await _h.Worker.ClaimLoginAsync(_worker.Identity, claude.Code, default)).Succeeded, Is.True);
        Assert.That((await _h.Worker.ClaimLoginAsync(_worker.Identity, codex.Code, default)).Succeeded, Is.True);
    }

    [Test]
    public async Task Cancelling_a_login_is_idempotent_and_blocks_the_claim()
    {
        // Arrange
        var start = await StartAsync();

        // Act
        var first = await _h.Service.CancelLoginAsync(_h.Owner, start.Login.Id, default);
        var second = await _h.Service.CancelLoginAsync(_h.Owner, start.Login.Id, default);
        var claimed = await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);

        // Assert
        Assert.That(first.Succeeded && second.Succeeded, Is.True);
        Assert.That(claimed.Succeeded, Is.False);
    }

    [Test]
    public async Task Login_cannot_start_for_an_agent_that_is_not_paired_or_is_disabled()
    {
        // Arrange
        var waiting = await _h.CreateAgentAsync("沒配對");
        await _h.Service.UpdateAgentAsync(_h.Owner, _worker.AgentId, new HostAgentInput("主機代理") { Enabled = false }, default);

        // Act
        var notPaired = await _h.Service.StartLoginAsync(_h.Owner, waiting.Agent.Id, AiProviderKind.ClaudeCode, default);
        var disabled = await _h.Service.StartLoginAsync(_h.Owner, _worker.AgentId, AiProviderKind.ClaudeCode, default);

        // Assert
        Assert.That(notPaired.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentUnavailable));
        Assert.That(disabled.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentUnavailable));
    }

    [Test]
    public async Task Logout_revokes_the_authorization_and_tells_the_worker_only_while_it_is_still_logged_in()
    {
        // Arrange
        var setup = await _h.SetUpAsync(agentName: "第二台");

        // Act
        var logout = await _h.Service.LogoutAsync(_h.Owner, setup.Worker.AgentId, AiProviderKind.ClaudeCode, default);
        var first = await _h.CheckInAsync(setup.Worker);
        setup.Worker.Reports = [HostHarness.Report(loggedIn: false)];
        var second = await _h.CheckInAsync(setup.Worker);
        setup.Worker.Reports = [HostHarness.Report(loggedIn: true)];
        var third = await _h.CheckInAsync(setup.Worker);

        // Assert
        Assert.That(logout.Succeeded, Is.True);
        Assert.That(first.Logout, Is.EqualTo(new[] { AiProviderKind.ClaudeCode }));
        Assert.That(second.Logout, Is.Empty);
        Assert.That(third.Logout, Is.Empty);
        Assert.That((await _h.SourceAsync(setup.Source.Id)).Status, Is.EqualTo(HostSourceStatus.EngineUnavailable));
    }

    [Test]
    public async Task Failed_logins_show_their_reason_to_the_owner_for_a_while_only()
    {
        // Arrange
        var start = await StartAsync();
        await _h.Worker.ClaimLoginAsync(_worker.Identity, start.Code, default);
        await CompleteAsync(start.Login.Id, billing: HostBilling.Subscription);

        // Act
        var shown = (await _h.AgentAsync(_worker.AgentId)).Logins;
        _h.Clock.Advance(AiRules.HostLoginLifetime + TimeSpan.FromSeconds(1));
        var later = (await _h.AgentAsync(_worker.AgentId)).Logins;

        // Assert
        Assert.That(shown.Single().State, Is.EqualTo(HostLoginState.Failed));
        Assert.That(shown.Single().Error, Is.Not.Null.And.Not.Empty);
        Assert.That(later, Is.Empty);
    }
}
