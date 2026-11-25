using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>配對：一次性的碼、期限、開始配對的人現在還要是擁有者；重新配對換掉機器憑證。</summary>
[TestFixture]
public sealed class HostPairingTests
{
    private HostHarness _h = null!;

    [SetUp]
    public async Task SetUp() => _h = await HostHarness.StartAsync();

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private Task<Result<HostPairingResult>> PairWithAsync(string code, string machineKey = "machine-0001") =>
        _h.Worker.PairAsync(new HostPairingRequest(code, machineKey, "主機", "0.9.0"), default);

    [Test]
    public async Task Pairing_returns_a_credential_that_authenticates_and_the_agent_becomes_active()
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync();

        // Act
        var paired = await PairWithAsync(pairing.PairingCode);

        // Assert
        Assert.That(paired.Succeeded, Is.True);
        Assert.That(paired.Value.Credential, Does.StartWith("kha_"));
        var identity = await _h.Worker.AuthenticateAsync(paired.Value.Credential, HostHarness.DefaultMachineKey, default);
        Assert.That(identity, Is.EqualTo(new HostAgentIdentity(_h.Assistant.OrganizationId, pairing.Agent.Id)));
        var agent = await _h.AgentAsync(pairing.Agent.Id);
        Assert.That(agent.State, Is.EqualTo(HostAgentState.Active));
        Assert.That(agent.MachineName, Is.EqualTo("主機"));
        Assert.That(agent.PairingExpiresAt, Is.Null);
    }

    [Test]
    public async Task Pairing_code_is_shown_in_groups_and_accepts_lowercase_and_confusable_letters()
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync();
        var sloppy = pairing.PairingCode.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal).Replace('0', 'o').Replace('1', 'l');

        // Act
        var paired = await PairWithAsync(sloppy);

        // Assert
        Assert.That(pairing.PairingCode, Does.Match("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$"));
        Assert.That(paired.Succeeded, Is.True, () => paired.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task Replaying_a_used_pairing_code_is_refused_and_keeps_the_first_credential_working()
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync();
        var first = await PairWithAsync(pairing.PairingCode);

        // Act
        var replay = await PairWithAsync(pairing.PairingCode, "machine-0002");

        // Assert
        Assert.That(replay.Failure?.Code, Is.EqualTo(AiErrorCodes.HostPairingInvalid));
        Assert.That(await _h.Worker.AuthenticateAsync(first.Value.Credential, HostHarness.DefaultMachineKey, default), Is.Not.Null);
    }

    [Test]
    public async Task Concurrent_pairings_with_one_code_succeed_exactly_once()
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync();

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => PairWithAsync(pairing.PairingCode, $"machine-000{i}")));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
    }

    [Test]
    public async Task Expired_pairing_code_is_refused()
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync();
        _h.Clock.Advance(AiRules.HostPairingLifetime + TimeSpan.FromSeconds(1));

        // Act
        var paired = await PairWithAsync(pairing.PairingCode);

        // Assert
        Assert.That(paired.Failure?.Code, Is.EqualTo(AiErrorCodes.HostPairingInvalid));
    }

    [Test]
    public async Task Wrong_or_malformed_pairing_code_is_refused_without_saying_why()
    {
        // Arrange
        await _h.CreateAgentAsync();

        // Act
        var wrong = await PairWithAsync("AAAA-BBBB-CCCC");
        var malformed = await PairWithAsync("not a code");

        // Assert
        Assert.That(wrong.Failure?.Code, Is.EqualTo(AiErrorCodes.HostPairingInvalid));
        Assert.That(malformed.Failure?.Code, Is.EqualTo(AiErrorCodes.HostPairingInvalid));
        Assert.That(wrong.Failure!.Message, Is.EqualTo(malformed.Failure!.Message));
    }

    [Test]
    public async Task Pairing_is_refused_when_the_person_who_started_it_is_no_longer_the_owner()
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync();
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Admin);

        // Act
        var paired = await PairWithAsync(pairing.PairingCode);

        // Assert
        Assert.That(paired.Failure?.Code, Is.EqualTo(AiErrorCodes.HostPairingInvalid));
        Assert.That((await _h.People.FindManyAsync(_h.Assistant.OrganizationId, [_h.Owner.PersonId], default)).Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Pairing_succeeds_after_the_person_is_owner_again_because_a_refusal_changes_nothing()
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync();
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Admin);
        await PairWithAsync(pairing.PairingCode);
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Owner);

        // Act
        var paired = await PairWithAsync(pairing.PairingCode);

        // Assert
        Assert.That(paired.Succeeded, Is.True);
    }

    [TestCase("")]
    [TestCase("short")]
    [TestCase("has space inside key")]
    public async Task Bad_machine_key_is_an_invalid_agent_request(string machineKey)
    {
        // Arrange
        var pairing = await _h.CreateAgentAsync();

        // Act
        var paired = await PairWithAsync(pairing.PairingCode, machineKey);

        // Assert
        Assert.That(paired.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentInvalid));
    }

    [Test]
    public async Task Pairing_request_text_hides_the_code()
    {
        // Arrange
        var request = new HostPairingRequest("K7QD-3MZP-W9XA", "machine-0001", "主機", "0.9.0");

        // Act
        var text = request.ToString();

        // Assert
        Assert.That(text, Does.Not.Contain("K7QD"));
    }

    [Test]
    public async Task Resetting_the_pairing_revokes_the_old_credential_and_needs_a_new_code()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        var reset = await _h.Service.ResetPairingAsync(_h.Owner, worker.AgentId, default);

        // Assert
        Assert.That(await _h.Worker.AuthenticateAsync(worker.Credential, HostHarness.DefaultMachineKey, default), Is.Null);
        Assert.That(reset.Value.Agent.State, Is.EqualTo(HostAgentState.AwaitingPairing));
        var paired = await PairWithAsync(reset.Value.PairingCode);
        Assert.That(paired.Succeeded, Is.True);
        Assert.That(paired.Value.AgentId, Is.EqualTo(worker.AgentId));
        Assert.That(await _h.Worker.AuthenticateAsync(paired.Value.Credential, HostHarness.DefaultMachineKey, default), Is.Not.Null);
    }

    [Test]
    public async Task Removing_the_agent_makes_its_credential_unknown()
    {
        // Arrange
        var worker = await _h.PairAsync();

        // Act
        var removed = await _h.Service.RemoveAgentAsync(_h.Owner, worker.AgentId, default);

        // Assert
        Assert.That(removed.Succeeded, Is.True);
        Assert.That(await _h.Worker.AuthenticateAsync(worker.Credential, HostHarness.DefaultMachineKey, default), Is.Null);
        var overview = await _h.Service.GetAsync(_h.Owner, default);
        Assert.That(overview.Value.Agents, Is.Empty);
    }

    [Test]
    public async Task Check_in_that_slips_through_after_the_agent_was_disabled_says_disabled_and_gives_no_job()
    {
        // Arrange
        var worker = await _h.PairAsync();
        await _h.Service.UpdateAgentAsync(_h.Owner, worker.AgentId, new HostAgentInput("主機代理") { Enabled = false }, default);

        // Act
        var checkedIn = await _h.CheckInAsync(worker, capacity: 1);

        // Assert
        Assert.That(checkedIn.Disabled, Is.True);
        Assert.That(checkedIn.Job, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("kha_")]
    [TestCase("kha_not-a-real-one")]
    [TestCase("kjt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Malformed_or_unknown_credentials_do_not_authenticate(string? credential)
    {
        // Arrange
        await _h.PairAsync();

        // Act
        var identity = await _h.Worker.AuthenticateAsync(credential!, HostHarness.DefaultMachineKey, default);

        // Assert
        Assert.That(identity, Is.Null);
    }

    [Test]
    public async Task Agent_limit_and_duplicate_names_are_refused_with_details()
    {
        // Arrange
        for (var i = 0; i < AiRules.MaxHostAgentsPerOrganization; i++)
        {
            await _h.CreateAgentAsync($"代理 {i}");
        }

        // Act
        var tooMany = await _h.Service.CreateAgentAsync(_h.Owner, new HostAgentInput("多一台"), default);
        var duplicate = await _h.Service.UpdateAgentAsync(
            _h.Owner, (await _h.Service.GetAsync(_h.Owner, default)).Value.Agents[1].Id, new HostAgentInput("代理 0"), default);

        // Assert
        Assert.That(tooMany.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
        Assert.That(tooMany.Failure!.Details.Select(d => d.Code), Does.Contain(AiErrorCodes.TooManyConnections));
        Assert.That(duplicate.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("壞\0名字")]
    public async Task Agent_names_with_control_characters_or_blank_are_refused(string name)
    {
        // Arrange
        // Act
        var created = await _h.Service.CreateAgentAsync(_h.Owner, new HostAgentInput(name), default);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
        Assert.That(created.Failure!.Details.Single().Path, Is.EqualTo("name"));
    }
}
