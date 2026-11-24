using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.LocalAgents;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>租約與流水號：錯的租約、結束之後的回報被拒、重送不重複、跳號被擋；結果不明不重跑；執行逾時。</summary>
[TestFixture]
public sealed class HostLeaseTests
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

    private static LocalAgentText Text(string text) => new(text);

    private async Task<(LocalRun Run, HostJob Job)> RunningAsync()
    {
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        return (run, await _h.ClaimAsync(_setup.Worker));
    }

    [Test]
    public async Task Report_and_finish_with_a_wrong_lease_are_rejected_and_change_nothing()
    {
        // Arrange
        var (run, job) = await RunningAsync();

        // Act
        var report = await _h.Worker.ReportAsync(_setup.Worker.Identity, job.JobId, job.Lease + 1, 1, [Text("偷")], default);
        var finish = await _h.Worker.FinishAsync(_setup.Worker.Identity, job.JobId, job.Lease + 1, new LocalAgentFinish(AssistantMessageStatus.Complete, null, null), default);
        await _h.FinishAsync(_setup.Worker, job);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(report.Failure?.Code, Is.EqualTo(AiErrorCodes.HostJobLeaseLost));
        Assert.That(finish.Failure?.Code, Is.EqualTo(AiErrorCodes.HostJobLeaseLost));
        Assert.That(events.OfType<AssistantTextDelta>(), Is.Empty);
    }

    [Test]
    public async Task Reporting_after_the_worker_finished_is_rejected_and_a_repeated_finish_keeps_the_first_result()
    {
        // Arrange
        var (run, job) = await RunningAsync();
        await _h.FinishAsync(_setup.Worker, job, AssistantMessageStatus.Complete);

        // Act
        var late = await _h.Worker.ReportAsync(_setup.Worker.Identity, job.JobId, job.Lease, 1, [Text("遲了")], default);
        var again = await _h.Worker.FinishAsync(
            _setup.Worker.Identity, job.JobId, job.Lease, new LocalAgentFinish(AssistantMessageStatus.Failed, "改口", null), default);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(late.Failure?.Code, Is.EqualTo(AiErrorCodes.HostJobLeaseLost));
        Assert.That(again.Succeeded, Is.True);
        Assert.That(events.OfType<AssistantRunCompleted>().Single().Message.Status, Is.EqualTo(AssistantMessageStatus.Complete));
    }

    [Test]
    public async Task Another_agents_or_unknown_jobs_are_not_found()
    {
        // Arrange
        var (_, job) = await RunningAsync();
        var other = await _h.PairAsync("第二台");

        // Act
        var stranger = await _h.Worker.ReportAsync(other.Identity, job.JobId, job.Lease, 1, [], default);
        var unknown = await _h.Worker.FinishAsync(
            _setup.Worker.Identity, Guid.NewGuid(), 1, new LocalAgentFinish(AssistantMessageStatus.Complete, null, null), default);

        // Assert
        Assert.That(stranger.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentJobNotFound));
        Assert.That(unknown.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentJobNotFound));
    }

    [Test]
    public async Task Resent_batches_are_stored_once_and_overlapping_batches_only_add_the_new_part()
    {
        // Arrange
        var (run, job) = await RunningAsync();

        // Act
        var first = await _h.ReportAsync(_setup.Worker, job, 1, Text("甲"), Text("乙"));
        var resent = await _h.ReportAsync(_setup.Worker, job, 1, Text("甲"), Text("乙"));
        var overlap = await _h.ReportAsync(_setup.Worker, job, 2, Text("乙"), Text("丙"));
        await _h.FinishAsync(_setup.Worker, job);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That((first.NextSeq, resent.NextSeq, overlap.NextSeq), Is.EqualTo((3, 3, 4)));
        Assert.That(string.Concat(events.OfType<AssistantTextDelta>().Select(e => e.Delta)), Is.EqualTo("甲乙丙"));
    }

    [Test]
    public async Task A_gap_in_sequence_numbers_is_refused()
    {
        // Arrange
        var (_, job) = await RunningAsync();
        await _h.ReportAsync(_setup.Worker, job, 1, Text("甲"));

        // Act
        var gap = await _h.Worker.ReportAsync(_setup.Worker.Identity, job.JobId, job.Lease, 5, [Text("跳")], default);
        var zero = await _h.Worker.ReportAsync(_setup.Worker.Identity, job.JobId, job.Lease, 0, [Text("零")], default);

        // Assert
        Assert.That(gap.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentInvalid));
        Assert.That(zero.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentInvalid));
    }

    [Test]
    public async Task Empty_text_takes_a_sequence_number_but_is_not_stored()
    {
        // Arrange
        var (run, job) = await RunningAsync();

        // Act
        var ack = await _h.ReportAsync(_setup.Worker, job, 1, Text(string.Empty), Text("字"));
        await _h.FinishAsync(_setup.Worker, job);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(ack.NextSeq, Is.EqualTo(3));
        Assert.That(events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "字" }));
    }

    [Test]
    public async Task Control_characters_in_reported_text_are_cleaned_and_bad_events_are_invalid()
    {
        // Arrange
        var (run, job) = await RunningAsync();

        // Act
        await _h.ReportAsync(_setup.Worker, job, 1, Text("好\0嗎"));
        var bad = await _h.Worker.ReportAsync(
            _setup.Worker.Identity, job.JobId, job.Lease, 2,
            [new LocalAgentTool(new AssistantToolPart("c1", "bad name!", AssistantToolStatus.Done, null, null))], default);
        await _h.FinishAsync(_setup.Worker, job);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(events.OfType<AssistantTextDelta>().Single().Delta, Is.EqualTo("好嗎"));
        Assert.That(bad.Failure?.Code, Is.EqualTo(AiErrorCodes.HostAgentInvalid));
    }

    [Test]
    public async Task Proposal_ids_that_are_not_the_askers_are_dropped()
    {
        // Arrange
        var (run, job) = await RunningAsync();

        // Act
        await _h.ReportAsync(
            _setup.Worker, job, 1,
            new LocalAgentTool(new AssistantToolPart("c1", "propose_item_changes", AssistantToolStatus.Done, null, null) { ItemProposalId = Guid.NewGuid() }));
        await _h.FinishAsync(_setup.Worker, job);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(events.OfType<AssistantToolUpdate>().Single().Tool.ItemProposalId, Is.Null);
    }

    [Test]
    public async Task Worker_that_never_reported_after_claiming_did_not_start_and_the_message_is_not_counted_or_rerun()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await HostHarness.StartAsync(HostHarness.FastTimings with { LeaseTimeout = TimeSpan.FromMilliseconds(500) });
        _setup = await _h.SetUpAsync();
        var (run, job) = await RunningAsync();

        // Act
        var events = await run.ReadAllAsync();
        var late = await _h.Worker.ReportAsync(_setup.Worker.Identity, job.JobId, job.Lease, 1, [Text("遲")], default);
        var lateFinish = await _h.Worker.FinishAsync(
            _setup.Worker.Identity, job.JobId, job.Lease, new LocalAgentFinish(AssistantMessageStatus.Complete, null, null), default);
        var next = await _h.CheckInAsync(_setup.Worker, capacity: 2);

        // Assert
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
        Assert.That(final.Error, Does.Contain("沒有開始執行").And.Contain("不會自動重跑"));

        // 回覆存完之後工作列就刪了：worker 看到的是「找不到」或「租約已失效」，兩者都是叫它停下。
        Assert.That(late.Failure?.Code, Is.AnyOf(AiErrorCodes.HostJobLeaseLost, AiErrorCodes.AgentJobNotFound));
        Assert.That(lateFinish.Failure?.Code, Is.AnyOf(AiErrorCodes.HostJobLeaseLost, AiErrorCodes.AgentJobNotFound));
        Assert.That(next.Job, Is.Null);
        Assert.That((await _h.SourceAsync(_setup.Source.Id)).Usage.Requests, Is.EqualTo(0));
    }

    [Test]
    public async Task Heartbeats_keep_a_quiet_job_alive()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await HostHarness.StartAsync(HostHarness.FastTimings with { LeaseTimeout = TimeSpan.FromMilliseconds(600) });
        _setup = await _h.SetUpAsync();
        var (run, job) = await RunningAsync();

        // Act
        var seq = 1;
        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(150);

            // 測試的時鐘是停著的：撥一下，心跳的「最後回報時間」才會變。
            _h.Clock.Advance(TimeSpan.FromSeconds(1));
            seq = (await _h.ReportAsync(_setup.Worker, job, seq)).NextSeq;
        }

        await _h.FinishAsync(_setup.Worker, job);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(events.OfType<AssistantRunCompleted>().Single().Message.Status, Is.EqualTo(AssistantMessageStatus.Complete));
    }

    [Test]
    public async Task Running_too_long_asks_the_worker_to_stop_and_saves_a_stopped_reply_with_the_reason()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await HostHarness.StartAsync(timings: null, assistantTimings: new AssistantTimings(TimeSpan.FromMilliseconds(500), TimeSpan.FromMinutes(1)));
        _setup = await _h.SetUpAsync();
        var (run, job) = await RunningAsync();

        // Act
        var cancelled = false;
        for (var i = 0; i < 100 && !cancelled; i++)
        {
            await Task.Delay(50);
            cancelled = (await _h.ReportAsync(_setup.Worker, job, 1)).Cancelled;
        }

        await _h.FinishAsync(_setup.Worker, job, AssistantMessageStatus.Stopped);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(cancelled, Is.True);
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
        Assert.That(final.Error, Does.Contain("5 分鐘"));
    }

    [Test]
    public async Task Worker_that_ignores_the_stop_is_ended_by_the_server_and_its_next_report_says_cancelled()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await HostHarness.StartAsync(timings: null, assistantTimings: new AssistantTimings(TimeSpan.FromMilliseconds(300), TimeSpan.FromMinutes(1)));
        _setup = await _h.SetUpAsync();
        var (run, job) = await RunningAsync();

        // Act
        var reading = run.ReadAllAsync();
        var cancelled = false;
        for (var i = 0; i < 100 && !cancelled && !reading.IsCompleted; i++)
        {
            await Task.Delay(30);
            var ack = await _h.Worker.ReportAsync(_setup.Worker.Identity, job.JobId, job.Lease, 1, [], default);
            cancelled = ack.Succeeded && ack.Value.Cancelled;
        }

        var events = await reading;

        // Assert
        Assert.That(cancelled, Is.True);
        Assert.That(events.OfType<AssistantRunCompleted>().Single().Message.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
    }

    [Test]
    public async Task Invalid_check_in_content_is_refused_with_the_host_agent_code()
    {
        // Arrange
        var bad = new HostAgentCheckIn("0.9.0", "ftp://nope", []);
        var dup = new HostAgentCheckIn("0.9.0", HostHarness.ServerUrl, [HostHarness.Report(), HostHarness.Report()]);
        var creds = new HostAgentCheckIn("0.9.0", "http://user:pw@api:8080", []);
        var longVersion = new HostAgentCheckIn(new string('v', 41), HostHarness.ServerUrl, []);

        // Act
        var results = new List<Result<HostCheckInResult>>();
        foreach (var checkIn in new[] { bad, dup, creds, longVersion })
        {
            results.Add(await _h.Worker.CheckInAsync(_setup.Worker.Identity, checkIn, 0, default));
        }

        // Assert
        Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.HostAgentInvalid));
    }

    [Test]
    public async Task Check_in_records_server_url_version_and_engine_status()
    {
        // Arrange
        // Act
        var agent = await _h.AgentAsync(_setup.Worker.AgentId);

        // Assert
        Assert.That(agent.ServerUrl, Is.EqualTo(HostHarness.ServerUrl));
        Assert.That(agent.AgentVersion, Is.EqualTo("0.9.0"));
        Assert.That(agent.Online, Is.True);
        Assert.That(agent.Engines.Single().Models.Select(m => m.Id), Is.EqualTo(new[] { "default", "sonnet" }));
    }
}
