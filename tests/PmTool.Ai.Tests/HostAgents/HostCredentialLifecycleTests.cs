using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>機器憑證的期限、換新、停用與擁有者降權之後不認，以及報了別台機器編號的警告（安全修復第四輪 AG-05、AG-11）。</summary>
[TestFixture]
public sealed class HostCredentialLifecycleTests
{
    private HostHarness _h = null!;

    [SetUp]
    public async Task SetUp() => _h = await HostHarness.StartAsync();

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private Task<HostAgentIdentity?> AuthAsync(string credential) => _h.Worker.AuthenticateAsync(credential, HostHarness.DefaultMachineKey, default);

    private async Task<Result<HostCheckInResult>> RawCheckInAsync(HostWorker worker, string? machineKey, string? machineName = null) =>
        await _h.Worker.CheckInAsync(
            worker.Identity, new HostAgentCheckIn("0.9.0", HostHarness.ServerUrl, []) { MachineKey = machineKey, MachineName = machineName }, 0, default);

    [Test]
    public async Task Credential_expires_ninety_days_after_pairing()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        var agent = await _h.AgentAsync(worker.AgentId);

        // Assert
        Assert.That(agent.CredentialExpiresAt, Is.EqualTo(_h.Clock.GetUtcNow() + AiRules.HostCredentialLifetime));
        Assert.That(agent.Warning, Is.Null);
    }

    [Test]
    public async Task Expired_credential_is_refused_and_the_owner_sees_a_warning()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        _h.Clock.Advance(AiRules.HostCredentialLifetime + TimeSpan.FromMinutes(1));

        // Assert
        Assert.That(await AuthAsync(worker.Credential), Is.Null);
        var agent = await _h.AgentAsync(worker.AgentId);
        Assert.That(agent.Warning?.Kind, Is.EqualTo(HostAgentWarningKind.CredentialExpired));
    }

    [Test]
    public async Task Check_in_with_plenty_of_time_left_does_not_rotate_the_credential()
    {
        // Arrange
        var worker = await _h.PairAsync();
        _h.Clock.Advance(TimeSpan.FromDays(10));

        // Act
        var result = await _h.CheckInAsync(worker);

        // Assert
        Assert.That(result.RotatedCredential, Is.Null);
    }

    [Test]
    public async Task Check_in_close_to_expiry_issues_a_new_credential_that_replaces_the_old_one_on_first_use()
    {
        // Arrange
        var worker = await _h.PairAsync();
        _h.Clock.Advance(TimeSpan.FromDays(61));

        // Act
        var result = await _h.CheckInAsync(worker);
        var next = result.RotatedCredential;

        // Assert
        Assert.That(next, Does.StartWith("kha_"));
        Assert.That(await AuthAsync(worker.Credential), Is.Not.Null, "新的還沒用到，舊的照樣能用");
        Assert.That(await AuthAsync(next!), Is.EqualTo(worker.Identity));
        Assert.That(await AuthAsync(worker.Credential), Is.Null, "新的用過之後舊的作廢");
        Assert.That(await AuthAsync(next!), Is.Not.Null);
        var agent = await _h.AgentAsync(worker.AgentId);
        Assert.That(agent.CredentialExpiresAt, Is.EqualTo(_h.Clock.GetUtcNow() + AiRules.HostCredentialLifetime));
        var again = await _h.CheckInAsync(worker);
        Assert.That(again.RotatedCredential, Is.Null, "換過之後離期限還遠");
    }

    [Test]
    public async Task A_credential_that_was_never_picked_up_is_replaced_by_the_next_one()
    {
        // Arrange
        var worker = await _h.PairAsync();
        _h.Clock.Advance(TimeSpan.FromDays(70));
        var first = (await _h.CheckInAsync(worker)).RotatedCredential;

        // Act
        var second = (await _h.CheckInAsync(worker)).RotatedCredential;

        // Assert
        Assert.That(second, Is.Not.Null.And.Not.EqualTo(first));
        Assert.That(await AuthAsync(first!), Is.Null);
        Assert.That(await AuthAsync(second!), Is.Not.Null);
    }

    [Test]
    public async Task Two_requests_with_the_new_credential_at_once_both_succeed_and_promote_only_once()
    {
        // Arrange
        var worker = await _h.PairAsync();
        _h.Clock.Advance(TimeSpan.FromDays(70));
        var next = (await _h.CheckInAsync(worker)).RotatedCredential!;

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => AuthAsync(next)));

        // Assert
        Assert.That(results, Is.All.EqualTo(worker.Identity));
        Assert.That(await AuthAsync(worker.Credential), Is.Null);
    }

    [Test]
    public async Task Disabled_agent_does_not_authenticate_and_works_again_once_enabled()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        await _h.Service.UpdateAgentAsync(_h.Owner, worker.AgentId, new HostAgentInput("主機代理") { Enabled = false }, default);
        var whileDisabled = await AuthAsync(worker.Credential);
        await _h.Service.UpdateAgentAsync(_h.Owner, worker.AgentId, new HostAgentInput("主機代理") { Enabled = true }, default);

        // Assert
        Assert.That(whileDisabled, Is.Null);
        Assert.That(await AuthAsync(worker.Credential), Is.Not.Null);
    }

    [Test]
    public async Task Credential_stops_working_when_the_person_who_paired_is_no_longer_an_owner()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        _h.People.Set(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Admin, false);

        // Assert
        Assert.That(await AuthAsync(worker.Credential), Is.Null);
    }

    [Test]
    public async Task Owner_overview_warns_when_the_person_who_paired_lost_ownership_and_repairing_clears_it()
    {
        // Arrange
        var worker = await _h.PairAsync();
        var second = _h.NewMember();
        _h.People.Set(_h.Assistant.OrganizationId, second.PersonId, OrganizationRole.Owner, false);
        _h.People.Set(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Admin, false);

        // Act
        var overview = await _h.Service.GetAsync(second, default);
        var warning = overview.Value.Agents.Single().Warning;
        var reset = await _h.Service.ResetPairingAsync(second, worker.AgentId, default);

        // Assert
        Assert.That(warning?.Kind, Is.EqualTo(HostAgentWarningKind.PairedByFormerOwner));
        Assert.That(reset.Value.Agent.Warning, Is.Null);
    }

    [Test]
    public async Task Check_in_without_a_machine_key_is_invalid()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        var missing = await RawCheckInAsync(worker, null);
        var malformed = await RawCheckInAsync(worker, "bad key!");

        // Assert
        Assert.That(missing.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentInvalid));
        Assert.That(malformed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentInvalid));
    }

    [Test]
    public async Task Check_in_from_another_machine_is_refused_and_recorded_until_the_agent_is_paired_again()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        var refused = await RawCheckInAsync(worker, "other-machine-9", "別台電腦");
        var warning = (await _h.AgentAsync(worker.AgentId)).Warning;
        var genuine = await _h.CheckInAsync(worker);
        var reset = await _h.Service.ResetPairingAsync(_h.Owner, worker.AgentId, default);

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(AiErrorCodes.HostMachineMismatch));
        Assert.That(warning?.Kind, Is.EqualTo(HostAgentWarningKind.OtherMachine));
        Assert.That(warning?.MachineName, Is.EqualTo("別台電腦"));
        Assert.That(genuine.Disabled, Is.False);
        Assert.That(reset.Value.Agent.Warning, Is.Null);
    }

    [Test]
    public async Task A_machine_key_that_differs_only_by_case_is_another_machine()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        var result = await RawCheckInAsync(worker, HostHarness.DefaultMachineKey.ToUpperInvariant());

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.HostMachineMismatch));
    }

    [Test]
    public async Task Credential_without_the_paired_machine_key_is_refused_for_every_request()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        var missing = await _h.Worker.AuthenticateAsync(worker.Credential, null, default);
        var other = await _h.Worker.AuthenticateAsync(worker.Credential, "other-machine-9", default);

        // Assert
        Assert.That(missing, Is.Null);
        Assert.That(other, Is.Null);
    }

    [Test]
    public async Task Credential_from_another_machine_leaves_a_warning_but_the_original_machine_keeps_working()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        await _h.Worker.AuthenticateAsync(worker.Credential, "other-machine-9", default);
        var warning = (await _h.AgentAsync(worker.AgentId)).Warning;

        // Assert
        Assert.That(warning?.Kind, Is.EqualTo(HostAgentWarningKind.OtherMachine));
        Assert.That(warning?.MachineName, Is.Null);
        Assert.That(await AuthAsync(worker.Credential), Is.EqualTo(worker.Identity));
    }

    [Test]
    public async Task A_missing_machine_key_header_leaves_no_warning()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        await _h.Worker.AuthenticateAsync(worker.Credential, null, default);

        // Assert
        Assert.That((await _h.AgentAsync(worker.AgentId)).Warning, Is.Null);
    }
}
