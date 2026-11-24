using PmTool.Abstractions;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>組織來源：開著時要 API 計費的登入與計費確認；模型、預設、上限的檢查；狀態；停用與刪除。</summary>
[TestFixture]
public sealed class HostSourceAdminTests
{
    private HostHarness _h = null!;

    [SetUp]
    public async Task SetUp() => _h = await HostHarness.StartAsync();

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private async Task<(HostWorker Worker, Guid LoginId)> LoggedInWorkerAsync()
    {
        var worker = await _h.PairAsync();
        var loginId = await _h.LoginAsync(worker);
        worker.Reports = [HostHarness.Report(loginId: loginId)];
        await _h.CheckInAsync(worker);
        return (worker, loginId);
    }

    private static FailureDetail Detail(Result<HostSource> result) => result.Failure!.Details.Single();

    [Test]
    public async Task Enabled_source_needs_the_billing_acknowledgement()
    {
        // Arrange
        var (worker, _) = await LoggedInWorkerAsync();

        // Act
        var created = await _h.Service.CreateSourceAsync(_h.Owner, HostHarness.SourceInput(worker.AgentId, acknowledge: false), default);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
        Assert.That(Detail(created).Code, Is.EqualTo(AiErrorCodes.FieldRequired));
        Assert.That(Detail(created).Path, Is.EqualTo("acknowledgeApiBilling"));
    }

    [Test]
    public async Task Enabled_source_needs_a_kumi_authorized_login()
    {
        // Arrange
        var worker = await _h.PairAsync();
        worker.Reports = [HostHarness.Report(loggedIn: false, billing: HostBilling.Unknown)];
        await _h.CheckInAsync(worker);

        // Act
        var created = await _h.Service.CreateSourceAsync(_h.Owner, HostHarness.SourceInput(worker.AgentId), default);

        // Assert
        Assert.That(Detail(created).Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
        Assert.That(Detail(created).Path, Is.EqualTo("enabled"));
    }

    [TestCase(HostBilling.Subscription)]
    [TestCase(HostBilling.Unknown)]
    public async Task Source_cannot_be_enabled_when_the_engine_reports_a_subscription_login(HostBilling billing)
    {
        // Arrange
        var worker = await _h.PairAsync();
        worker.Reports = [HostHarness.Report(billing: billing, loginId: Guid.NewGuid())];
        await _h.CheckInAsync(worker);

        // Act
        var created = await _h.Service.CreateSourceAsync(_h.Owner, HostHarness.SourceInput(worker.AgentId), default);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.HostBillingNotShareable));
    }

    [Test]
    public async Task Disabled_source_can_be_saved_before_the_engine_is_logged_in_or_has_reported()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        var created = await _h.Service.CreateSourceAsync(
            _h.Owner, HostHarness.SourceInput(worker.AgentId, enabled: false, acknowledge: false), default);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        Assert.That(created.Value.Status, Is.EqualTo(HostSourceStatus.Disabled));
        Assert.That(created.Value.Enabled, Is.False);
    }

    [Test]
    public async Task Models_must_be_in_the_list_the_engine_reported()
    {
        // Arrange
        var (worker, _) = await LoggedInWorkerAsync();

        // Act
        var created = await _h.Service.CreateSourceAsync(
            _h.Owner, HostHarness.SourceInput(worker.AgentId, models: ["default", "nope"]), default);

        // Assert
        Assert.That(Detail(created).Code, Is.EqualTo(AiErrorCodes.ModelNotAvailable));
        Assert.That(Detail(created).Path, Is.EqualTo("enabledModels"));
    }

    [Test]
    public async Task Default_model_and_effort_must_be_in_the_enabled_model_and_its_efforts()
    {
        // Arrange
        var (worker, _) = await LoggedInWorkerAsync();
        var input = HostHarness.SourceInput(worker.AgentId, models: ["default"]);

        // Act
        var wrongModel = await _h.Service.CreateSourceAsync(_h.Owner, input with { DefaultModel = "sonnet" }, default);
        var wrongEffort = await _h.Service.CreateSourceAsync(_h.Owner, input with { DefaultEffort = AiEffort.High }, default);

        // Assert
        Assert.That(Detail(wrongModel).Path, Is.EqualTo("defaultModel"));
        Assert.That(Detail(wrongEffort).Path, Is.EqualTo("defaultEffort"));
    }

    [Test]
    public async Task Enabled_source_needs_at_least_one_model()
    {
        // Arrange
        var (worker, _) = await LoggedInWorkerAsync();

        // Act
        var created = await _h.Service.CreateSourceAsync(_h.Owner, HostHarness.SourceInput(worker.AgentId, models: []), default);

        // Assert
        Assert.That(Detail(created).Code, Is.EqualTo(AiErrorCodes.FieldRequired));
    }

    [TestCase(0, null, null, "limits.dailyRequestsPerPerson")]
    [TestCase(null, AiRules.MaxHostRequestLimit + 1, null, "limits.monthlyRequests")]
    [TestCase(null, null, 0L, "limits.monthlyTokens")]
    [TestCase(null, null, AiRules.MaxHostTokenLimit + 1, "limits.monthlyTokens")]
    public async Task Limits_are_one_to_the_maximum_or_unlimited(int? daily, int? monthly, long? tokens, string path)
    {
        // Arrange
        var (worker, _) = await LoggedInWorkerAsync();
        var input = HostHarness.SourceInput(worker.AgentId, limits: new HostSourceLimits(daily, monthly, tokens));

        // Act
        var created = await _h.Service.CreateSourceAsync(_h.Owner, input, default);

        // Assert
        Assert.That(Detail(created).Path, Is.EqualTo(path));
    }

    [Test]
    public async Task One_source_per_agent_engine_and_unique_names_ignoring_case()
    {
        // Arrange
        var (worker, _) = await LoggedInWorkerAsync();
        await _h.Service.CreateSourceAsync(_h.Owner, HostHarness.SourceInput(worker.AgentId), default);
        var second = await _h.PairAsync("第二台");

        // Act
        var sameEngine = await _h.Service.CreateSourceAsync(_h.Owner, HostHarness.SourceInput(worker.AgentId, name: "另一個名字"), default);
        var sameName = await _h.Service.CreateSourceAsync(
            _h.Owner, HostHarness.SourceInput(second.AgentId, name: "主機上的 CLAUDE code", enabled: false), default);

        // Assert
        Assert.That(Detail(sameEngine).Path, Is.EqualTo("engine"));
        Assert.That(Detail(sameName).Path, Is.EqualTo("name"));
    }

    [Test]
    public async Task Agent_and_engine_cannot_be_changed_on_update()
    {
        // Arrange
        var setup = await _h.SetUpAsync();
        var other = await _h.PairAsync("第二台");

        // Act
        var moved = await _h.Service.UpdateSourceAsync(_h.Owner, setup.Source.Id, HostHarness.SourceInput(other.AgentId), default);
        var swapped = await _h.Service.UpdateSourceAsync(
            _h.Owner, setup.Source.Id, HostHarness.SourceInput(setup.Worker.AgentId, AiProviderKind.Codex), default);

        // Assert
        Assert.That(Detail(moved).Path, Is.EqualTo("agentId"));
        Assert.That(Detail(swapped).Path, Is.EqualTo("engine"));
    }

    [Test]
    public async Task Ready_source_reports_models_running_and_queued_counts()
    {
        // Arrange
        // Act
        var setup = await _h.SetUpAsync();

        // Assert
        var source = await _h.SourceAsync(setup.Source.Id);
        Assert.That(source.Status, Is.EqualTo(HostSourceStatus.Ready));
        Assert.That(source.Models.Select(m => (m.Id, m.Enabled)), Is.EqualTo(new[] { ("default", true), ("sonnet", true) }));
        Assert.That((source.Running, source.Queued), Is.EqualTo((0, 0)));
        Assert.That(source.BillingAcknowledgedAt, Is.Not.Null);
    }

    [Test]
    public async Task Source_status_follows_the_contract_order()
    {
        // Arrange
        var setup = await _h.SetUpAsync();
        var statuses = new List<HostSourceStatus>();

        // Act
        _h.Clock.Advance(AiRules.HostAgentOnlineWindow + TimeSpan.FromSeconds(1));
        statuses.Add((await _h.SourceAsync(setup.Source.Id)).Status);
        setup.Worker.Reports = [HostHarness.Report(loggedIn: false)];
        await _h.CheckInAsync(setup.Worker);
        statuses.Add((await _h.SourceAsync(setup.Source.Id)).Status);
        await _h.Service.UpdateAgentAsync(_h.Owner, setup.Worker.AgentId, new HostAgentInput("主機代理") { Enabled = false }, default);
        statuses.Add((await _h.SourceAsync(setup.Source.Id)).Status);
        await _h.Service.UpdateSourceAsync(_h.Owner, setup.Source.Id, HostHarness.SourceInput(setup.Worker.AgentId, enabled: false, acknowledge: false), default);
        statuses.Add((await _h.SourceAsync(setup.Source.Id)).Status);

        // Assert
        Assert.That(statuses, Is.EqualTo(new[]
        {
            HostSourceStatus.AgentOffline,
            HostSourceStatus.EngineUnavailable,
            HostSourceStatus.AgentUnavailable,
            HostSourceStatus.Disabled,
        }));
    }

    [Test]
    public async Task Engine_unavailable_status_explains_each_readiness()
    {
        // Arrange
        var setup = await _h.SetUpAsync();
        var cases = new (HostEngineReport Report, HostEngineReadiness Expected)[]
        {
            (HostHarness.Report(installed: false), HostEngineReadiness.NotInstalled),
            (HostHarness.Report(supported: false), HostEngineReadiness.VersionUnsupported),
            (HostHarness.Report(loggedIn: false), HostEngineReadiness.NotLoggedIn),
            (HostHarness.Report(billing: HostBilling.Subscription, loginId: setup.LoginId), HostEngineReadiness.BillingNotShareable),
            (HostHarness.Report(loginId: Guid.NewGuid()), HostEngineReadiness.NotAuthorized),
        };

        // Act
        var seen = new List<(HostEngineReadiness, HostSourceStatus, bool)>();
        foreach (var (report, _) in cases)
        {
            setup.Worker.Reports = [report];
            await _h.CheckInAsync(setup.Worker);
            var source = await _h.SourceAsync(setup.Source.Id);
            var engine = (await _h.AgentAsync(setup.Worker.AgentId)).Engines.Single();
            seen.Add((engine.Readiness, source.Status, !string.IsNullOrEmpty(source.StatusMessage)));
        }

        // Assert
        Assert.That(seen.Select(s => s.Item1), Is.EqualTo(cases.Select(c => c.Expected)));
        Assert.That(seen.Select(s => s.Item2), Is.All.EqualTo(HostSourceStatus.EngineUnavailable));
        Assert.That(seen.Select(s => s.Item3), Is.All.True);
    }

    [Test]
    public async Task Source_stays_enabled_when_the_login_later_fails_and_turns_ready_again_after_a_new_login()
    {
        // Arrange
        var setup = await _h.SetUpAsync();
        setup.Worker.Reports = [HostHarness.Report(loggedIn: false)];
        await _h.CheckInAsync(setup.Worker);

        // Act
        var during = await _h.SourceAsync(setup.Source.Id);
        var loginId = await _h.LoginAsync(setup.Worker);
        setup.Worker.Reports = [HostHarness.Report(loginId: loginId)];
        await _h.CheckInAsync(setup.Worker);
        var after = await _h.SourceAsync(setup.Source.Id);

        // Assert
        Assert.That(during.Enabled, Is.True);
        Assert.That(during.Status, Is.EqualTo(HostSourceStatus.EngineUnavailable));
        Assert.That(after.Status, Is.EqualTo(HostSourceStatus.Ready));
    }

    [Test]
    public async Task Reaching_the_monthly_request_limit_shows_quota_reached()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(null, 1, null));
        var run = await _h.SendAsync(_h.Member, setup.Source);
        var job = await _h.ClaimAsync(setup.Worker);
        await _h.FinishAsync(setup.Worker, job);
        await run.ReadAllAsync();

        // Act
        var source = await _h.SourceAsync(setup.Source.Id);

        // Assert
        Assert.That(source.Status, Is.EqualTo(HostSourceStatus.QuotaReached));
        Assert.That(source.Usage.Requests, Is.EqualTo(1));
    }

    [Test]
    public async Task Removing_the_source_deletes_it_and_a_missing_one_is_not_found()
    {
        // Arrange
        var setup = await _h.SetUpAsync();

        // Act
        var deleted = await _h.Service.DeleteSourceAsync(_h.Owner, setup.Source.Id, default);
        var again = await _h.Service.DeleteSourceAsync(_h.Owner, setup.Source.Id, default);

        // Assert
        Assert.That(deleted.Succeeded, Is.True);
        Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.HostSourceNotFound));
        Assert.That((await _h.Service.GetAsync(_h.Owner, default)).Value.Sources, Is.Empty);
    }

    [Test]
    public async Task Name_with_control_characters_is_refused()
    {
        // Arrange
        var (worker, _) = await LoggedInWorkerAsync();

        // Act
        var created = await _h.Service.CreateSourceAsync(_h.Owner, HostHarness.SourceInput(worker.AgentId, name: "壞\0名"), default);

        // Assert
        Assert.That(Detail(created).Path, Is.EqualTo("name"));
    }
}
