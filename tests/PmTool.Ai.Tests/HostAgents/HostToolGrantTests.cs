using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.LocalAgents;
using PmTool.Identity;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>工具授權（kjt_）：每次請求都查，工作還在跑、沒取消、沒過期、來源與代理開著、發問的人現在是有效成員；Actor 是發問的人。</summary>
[TestFixture]
public sealed class HostToolGrantTests
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

    private async Task<(LocalRun Run, HostJob Job)> RunningAsync(Actor? asker = null)
    {
        var run = await _h.SendAsync(asker ?? _h.Member, _setup.Source);
        return (run, await _h.ClaimAsync(_setup.Worker));
    }

    [Test]
    public async Task Grant_acts_as_the_asker_in_the_jobs_project_with_the_jobs_tools()
    {
        // Arrange
        var (_, job) = await RunningAsync(_h.Member);

        // Act
        var grant = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Assert
        Assert.That(grant, Is.Not.Null);
        Assert.That(grant!.ToActor(), Is.EqualTo(new Actor(_h.Assistant.OrganizationId, _h.Member.PersonId, IsOrganizationAdmin: false)));
        Assert.That(grant.ProjectKey, Is.EqualTo("WEB"));
        Assert.That(grant.AllowedTools, Is.EqualTo(job.AllowedTools));
        Assert.That(grant.JobId, Is.EqualTo(job.JobId));
    }

    [Test]
    public async Task Grant_is_never_the_owners_or_the_workers_identity()
    {
        // Arrange
        var (_, memberJob) = await RunningAsync(_h.Member);
        var grant = await _h.Grants.ResolveAsync(memberJob.ToolToken, default);

        // Act
        var workerIdentityAsToken = await _h.Grants.ResolveAsync(_setup.Worker.Credential, default);

        // Assert
        Assert.That(grant!.PersonId, Is.Not.EqualTo(_h.Owner.PersonId));
        Assert.That(grant.IsOrganizationAdmin, Is.False);
        Assert.That(workerIdentityAsToken, Is.Null);
    }

    [TestCase(OrganizationRole.Admin, true)]
    [TestCase(OrganizationRole.Owner, true)]
    [TestCase(OrganizationRole.Member, false)]
    public async Task Role_in_the_grant_is_the_askers_role_right_now(OrganizationRole role, bool expectedAdmin)
    {
        // Arrange
        var (_, job) = await RunningAsync(_h.Member);
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Member.PersonId, role);

        // Act
        var grant = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Assert
        Assert.That(grant!.IsOrganizationAdmin, Is.EqualTo(expectedAdmin));
    }

    [Test]
    public async Task Grant_stops_working_when_the_asker_is_disabled_or_becomes_a_guest()
    {
        // Arrange
        var (_, job) = await RunningAsync(_h.Member);

        // Act
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Member.PersonId, OrganizationRole.Guest);
        var asGuest = await _h.Grants.ResolveAsync(job.ToolToken, default);
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Member.PersonId, OrganizationRole.Member);
        var restored = await _h.Grants.ResolveAsync(job.ToolToken, default);
        _h.People.SetDisabled(_h.Assistant.OrganizationId, _h.Member.PersonId, true);
        var disabled = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Assert
        Assert.That(asGuest, Is.Null);
        Assert.That(restored, Is.Not.Null);
        Assert.That(disabled, Is.Null);
    }

    [Test]
    public async Task Grant_does_not_exist_before_the_job_is_claimed_and_ends_with_the_job()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        var running = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Act
        await _h.FinishAsync(_setup.Worker, job);
        var finished = await _h.Grants.ResolveAsync(job.ToolToken, default);
        await run.ReadAllAsync();
        var deleted = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Assert
        Assert.That(running, Is.Not.Null);
        Assert.That(finished, Is.Null);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task Grant_expires_after_the_reply_timeout_plus_a_minute()
    {
        // Arrange
        var (_, job) = await RunningAsync();

        // Act
        var before = job.ToolTokenExpiresAt;
        _h.Clock.Advance(AiRules.ReplyTimeout + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var expired = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Assert
        Assert.That(before - _h.Clock.GetUtcNow(), Is.LessThan(AiRules.ReplyTimeout + TimeSpan.FromMinutes(1)));
        Assert.That(expired, Is.Null);
    }

    [Test]
    public async Task Grant_stops_when_the_source_or_agent_is_switched_off()
    {
        // Arrange
        var (_, job) = await RunningAsync();

        // Act
        await _h.Service.UpdateSourceAsync(
            _h.Owner, _setup.Source.Id, HostHarness.SourceInput(_setup.Worker.AgentId, enabled: false, acknowledge: false), default);
        var sourceOff = await _h.Grants.ResolveAsync(job.ToolToken, default);

        // Assert
        Assert.That(sourceOff, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("kjt_")]
    [TestCase("kjt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [TestCase("pmt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Unknown_or_malformed_tokens_resolve_to_nothing(string? token)
    {
        // Arrange
        await RunningAsync();

        // Act
        var grant = await _h.Grants.ResolveAsync(token!, default);

        // Assert
        Assert.That(grant, Is.Null);
    }

    [Test]
    public async Task Each_job_gets_its_own_token()
    {
        // Arrange
        var (_, first) = await RunningAsync();
        var firstGrant = await _h.Grants.ResolveAsync(first.ToolToken, default);
        await _h.FinishAsync(_setup.Worker, first);

        // Act
        var (_, second) = await RunningAsync(_h.Member2);
        var secondGrant = await _h.Grants.ResolveAsync(second.ToolToken, default);

        // Assert
        Assert.That(second.ToolToken, Is.Not.EqualTo(first.ToolToken));
        Assert.That(firstGrant!.JobId, Is.Not.EqualTo(secondGrant!.JobId));
        Assert.That(secondGrant.PersonId, Is.EqualTo(_h.Member2.PersonId));
    }

    [Test]
    public async Task A_repeated_propose_call_is_only_executed_once()
    {
        // Arrange
        var (_, job) = await RunningAsync();

        // Act
        var first = await _h.Grants.BeginWriteAsync(job.JobId, "propose_item_changes", "hash-1", default);
        var duplicateWhileRunning = await _h.Grants.BeginWriteAsync(job.JobId, "propose_item_changes", "hash-1", default);
        await _h.Grants.CompleteWriteAsync(job.JobId, "propose_item_changes", "hash-1", "{\"id\":\"p1\"}", default);
        var replay = await _h.Grants.BeginWriteAsync(job.JobId, "propose_item_changes", "hash-1", default);
        var otherArguments = await _h.Grants.BeginWriteAsync(job.JobId, "propose_item_changes", "hash-2", default);
        var otherTool = await _h.Grants.BeginWriteAsync(job.JobId, "propose_changeset", "hash-1", default);

        // Assert
        Assert.That(first.Kind, Is.EqualTo(HostToolReplayKind.Proceed));
        Assert.That(duplicateWhileRunning.Kind, Is.EqualTo(HostToolReplayKind.InProgress));
        Assert.That(replay.Kind, Is.EqualTo(HostToolReplayKind.Replay));
        Assert.That(replay.ResultJson, Does.Contain("p1"));
        Assert.That(otherArguments.Kind, Is.EqualTo(HostToolReplayKind.Proceed));
        Assert.That(otherTool.Kind, Is.EqualTo(HostToolReplayKind.Proceed));
    }

    [Test]
    public async Task Concurrent_identical_calls_get_exactly_one_proceed()
    {
        // Arrange
        var (_, job) = await RunningAsync();

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _h.Grants.BeginWriteAsync(job.JobId, "propose_changeset", "same", default)));

        // Assert
        Assert.That(results.Count(r => r.Kind == HostToolReplayKind.Proceed), Is.EqualTo(1));
        Assert.That(results.Count(r => r.Kind == HostToolReplayKind.InProgress), Is.EqualTo(9));
    }

    [Test]
    public async Task A_failed_call_can_be_retried_after_the_model_fixes_it()
    {
        // Arrange
        var (_, job) = await RunningAsync();
        await _h.Grants.BeginWriteAsync(job.JobId, "propose_changeset", "h", default);

        // Act
        await _h.Grants.CompleteWriteAsync(job.JobId, "propose_changeset", "h", null, default);
        var retry = await _h.Grants.BeginWriteAsync(job.JobId, "propose_changeset", "h", default);

        // Assert
        Assert.That(retry.Kind, Is.EqualTo(HostToolReplayKind.Proceed));
    }

    [Test]
    public async Task A_stuck_pending_call_can_be_taken_over_after_a_while()
    {
        // Arrange
        var (_, job) = await RunningAsync();
        await _h.Grants.BeginWriteAsync(job.JobId, "propose_changeset", "h", default);

        // Act
        var soon = await _h.Grants.BeginWriteAsync(job.JobId, "propose_changeset", "h", default);
        _h.Clock.Advance(TimeSpan.FromMinutes(3));
        var later = await _h.Grants.BeginWriteAsync(job.JobId, "propose_changeset", "h", default);

        // Assert
        Assert.That(soon.Kind, Is.EqualTo(HostToolReplayKind.InProgress));
        Assert.That(later.Kind, Is.EqualTo(HostToolReplayKind.Proceed));
    }

    [Test]
    public async Task Writes_for_a_job_that_no_longer_exists_are_not_executed()
    {
        // Arrange
        // Act
        var replay = await _h.Grants.BeginWriteAsync(Guid.NewGuid(), "propose_changeset", "h", default);

        // Assert
        Assert.That(replay.Kind, Is.Not.EqualTo(HostToolReplayKind.Proceed));
    }
}
