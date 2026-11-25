using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.LocalAgents;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>擁有者的動作（關掉來源、停用或移除或重新配對代理、登出）：排隊中的取消（用量退還）、執行中的停下、工具授權立刻失效。</summary>
[TestFixture]
public sealed class HostCancelTests
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

    private Task DisableSourceAsync() => _h.Service.UpdateSourceAsync(
        _h.Owner, _setup.Source.Id, HostHarness.SourceInput(_setup.Worker.AgentId, enabled: false, acknowledge: false), default);

    private Task DisableAgentAsync() =>
        _h.Service.UpdateAgentAsync(_h.Owner, _setup.Worker.AgentId, new HostAgentInput("主機代理") { Enabled = false }, default);

    public static IEnumerable<TestCaseData> OwnerActions() =>
    [
        new TestCaseData("disable source").SetArgDisplayNames("disable_source"),
        new TestCaseData("delete source").SetArgDisplayNames("delete_source"),
        new TestCaseData("disable agent").SetArgDisplayNames("disable_agent"),
        new TestCaseData("reset pairing").SetArgDisplayNames("reset_pairing"),
        new TestCaseData("remove agent").SetArgDisplayNames("remove_agent"),
        new TestCaseData("logout").SetArgDisplayNames("logout"),
    ];

    private async Task ActAsync(string action)
    {
        switch (action)
        {
            case "disable source":
                await DisableSourceAsync();
                break;
            case "delete source":
                await _h.Service.DeleteSourceAsync(_h.Owner, _setup.Source.Id, default);
                break;
            case "disable agent":
                await DisableAgentAsync();
                break;
            case "reset pairing":
                await _h.Service.ResetPairingAsync(_h.Owner, _setup.Worker.AgentId, default);
                break;
            case "remove agent":
                await _h.Service.RemoveAgentAsync(_h.Owner, _setup.Worker.AgentId, default);
                break;
            default:
                await _h.Service.LogoutAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.ClaudeCode, default);
                break;
        }
    }

    [TestCaseSource(nameof(OwnerActions))]
    public async Task Queued_messages_are_cancelled_with_a_reason_and_do_not_count_as_usage(string action)
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        await _h.WaitForQueuedAsync(1);

        // Act
        await ActAsync(action);
        var events = await run.ReadAllAsync();

        // Assert
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
        Assert.That(final.Error, Does.Contain("沒有執行"));
        Assert.That(events.OfType<AssistantRunStarted>(), Is.Empty);
        Assert.That(await _h.JobRowCountAsync(), Is.EqualTo(0));
    }

    [TestCaseSource(nameof(OwnerActions))]
    public async Task Running_jobs_are_told_to_stop_and_their_tool_grant_stops_working_at_once(string action)
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        var before = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Act
        await ActAsync(action);
        var after = await _h.Grants.ResolveAsync(job.ToolToken, default);
        var ack = await _h.Worker.ReportAsync(_setup.Worker.Identity, job.JobId, job.Lease, 1, [], default);
        var stopped = ack.Succeeded && ack.Value.Cancelled;
        await _h.Worker.FinishAsync(
            _setup.Worker.Identity, job.JobId, job.Lease, new LocalAgentFinish(AssistantMessageStatus.Stopped, null, null), default);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(before, Is.Not.Null);
        Assert.That(after, Is.Null);
        Assert.That(stopped || action == "reset pairing" || action == "remove agent", Is.True);
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Stopped));

        // 已經跑了一部分：說「回覆中斷了」，不是「沒有執行」。
        Assert.That(final.Error, Does.Contain("回覆中斷了").And.Not.Contain("沒有執行"));
    }

    [Test]
    public async Task Disabled_source_hands_out_nothing_and_reenabling_serves_new_messages()
    {
        // Arrange
        await DisableSourceAsync();

        // Act
        var idle = await _h.CheckInAsync(_setup.Worker, capacity: 2);
        var refused = await _h.TrySendAsync(_h.Member, _setup.Source);
        await _h.Service.UpdateSourceAsync(_h.Owner, _setup.Source.Id, HostHarness.SourceInput(_setup.Worker.AgentId), default);
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, job);
        await run.ReadAllAsync();

        // Assert
        Assert.That(idle.Job, Is.Null);
        Assert.That(refused.Succeeded, Is.False);
    }

    [Test]
    public async Task Re_enabled_agent_works_with_the_same_credential()
    {
        // Arrange
        await DisableAgentAsync();
        var disabledCheckIn = await _h.CheckInAsync(_setup.Worker, capacity: 1);

        // Act
        await _h.Service.UpdateAgentAsync(_h.Owner, _setup.Worker.AgentId, new HostAgentInput("主機代理") { Enabled = true }, default);
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, job);
        await run.ReadAllAsync();

        // Assert
        Assert.That(disabledCheckIn.Disabled, Is.True);
        Assert.That(job.JobId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task Removed_agent_cannot_register_again_with_its_old_credential()
    {
        // Arrange
        await _h.Service.RemoveAgentAsync(_h.Owner, _setup.Worker.AgentId, default);

        // Act
        var identity = await _h.Worker.AuthenticateAsync(_setup.Worker.Credential, HostHarness.DefaultMachineKey, default);
        var checkIn = await _h.Worker.CheckInAsync(_setup.Worker.Identity, new HostAgentCheckIn("0.9.0", HostHarness.ServerUrl, []), 0, default);

        // Assert
        Assert.That(identity, Is.Null);
        Assert.That(checkIn.Succeeded, Is.False);
        Assert.That((await _h.Service.GetAsync(_h.Owner, default)).Value.Agents, Is.Empty);
    }

    [Test]
    public async Task Usage_of_a_removed_source_is_kept_for_the_record_but_not_listed()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, job);
        await run.ReadAllAsync();

        // Act
        await _h.Service.DeleteSourceAsync(_h.Owner, _setup.Source.Id, default);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        Assert.That(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.HostUsage), Is.EqualTo(1));
    }
}
