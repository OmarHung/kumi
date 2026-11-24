using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>佇列：先排先做、每個來源同時 1 件、每人最多等 1 件、全來源最多排 20 件、位置事件、排隊逾時與停止。</summary>
[TestFixture]
public sealed class HostQueueTests
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

    [Test]
    public async Task Reply_streams_queued_then_started_then_text_then_done()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        await _h.WaitForQueuedAsync(1);

        // Act
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.ReportAsync(_setup.Worker, job, 1, new LocalAgentText("你"), new LocalAgentText("好"));
        await _h.FinishAsync(_setup.Worker, job, usage: new AssistantUsage(10, 5));
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(events[0], Is.EqualTo(new AssistantRunQueued(1)));
        Assert.That(events[1], Is.TypeOf<AssistantRunStarted>());
        Assert.That(events.OfType<AssistantRunQueued>().Count(), Is.EqualTo(1));
        Assert.That(events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "你", "好" }));
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
        Assert.That(final.Usage, Is.EqualTo(new AssistantUsage(10, 5)));
        Assert.That(events[^1], Is.TypeOf<AssistantRunCompleted>());
    }

    [Test]
    public async Task Job_row_is_deleted_after_the_reply_is_saved_but_usage_stays()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, job);

        // Act
        await run.ReadAllAsync();

        // Assert
        Assert.That(await _h.JobRowCountAsync(), Is.EqualTo(0));
        Assert.That((await _h.SourceAsync(_setup.Source.Id)).Usage.Requests, Is.EqualTo(1));
    }

    [Test]
    public async Task A_person_can_have_only_one_waiting_message_per_source()
    {
        // Arrange
        await _h.SendAsync(_h.Member, _setup.Source);

        // Act
        var second = await _h.TrySendAsync(_h.Member, _setup.Source);

        // Assert
        Assert.That(second.Failure?.Code, Is.EqualTo(AiErrorCodes.HostQueueFull));
        Assert.That(second.Failure!.Kind, Is.EqualTo(FailureKind.Conflict));
        Assert.That(await _h.JobRowCountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Refused_send_stores_nothing_in_the_conversation()
    {
        // Arrange
        await _h.SendAsync(_h.Member, _setup.Source);
        var conversation = await _h.Assistant.NewConversationAsync(_h.Member);

        // Act
        var second = await _h.Assistant.Service.StartAsync(
            _h.Member, AssistantHarness.ProjectKey, conversation.Id,
            new AssistantMessageInput("再一則", new AiSelection(_setup.Source.Id, "default", null)), default);

        // Assert
        Assert.That(second.Failure?.Code, Is.EqualTo(AiErrorCodes.HostQueueFull));
        Assert.That(await _h.Assistant.MessagesAsync(conversation.Id, _h.Member), Is.Empty);
    }

    [Test]
    public async Task Once_a_message_starts_running_the_same_person_can_queue_the_next_one()
    {
        // Arrange
        await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);

        // Act
        var next = await _h.TrySendAsync(_h.Member, _setup.Source);

        // Assert
        Assert.That(next.Succeeded, Is.True, () => next.Failure?.Message ?? string.Empty);
        await _h.FinishAsync(_setup.Worker, job);
    }

    [Test]
    public async Task Capacity_is_the_slots_the_worker_still_has_so_both_engines_can_run_at_once()
    {
        // Arrange：同一台的 Codex 也登入、開成來源，兩個來源各排一件。
        var codexLogin = await _h.LoginAsync(_setup.Worker, AiProviderKind.Codex);
        _setup.Worker.Reports =
        [
            HostHarness.Report(loginId: _setup.LoginId),
            HostHarness.Report(AiProviderKind.Codex, loginId: codexLogin),
        ];
        await _h.CheckInAsync(_setup.Worker);
        var codex = await _h.Service.CreateSourceAsync(
            _h.Owner, HostHarness.SourceInput(_setup.Worker.AgentId, AiProviderKind.Codex, "主機上的 Codex"), CancellationToken.None);
        Assert.That(codex.Succeeded, Is.True, () => codex.Failure?.Message ?? string.Empty);
        var claudeRun = await _h.SendAsync(_h.Member, _setup.Source, "甲");
        var codexRun = await _h.SendAsync(_h.Member2, codex.Value, "乙");
        await _h.WaitForQueuedAsync(2);

        // Act：worker 先回報還能跑 2 件，領走一件之後回報還能跑 1 件。
        var first = await _h.ClaimAsync(_setup.Worker, capacity: 2);
        var second = await _h.ClaimAsync(_setup.Worker, capacity: 1);
        await _h.FinishAsync(_setup.Worker, first);
        await _h.FinishAsync(_setup.Worker, second);
        await claudeRun.ReadAllAsync();
        await codexRun.ReadAllAsync();

        // Assert
        Assert.That(new[] { first.Engine, second.Engine }, Is.EquivalentTo(new[] { AiProviderKind.ClaudeCode, AiProviderKind.Codex }));
    }

    [Test]
    public async Task One_source_runs_one_job_at_a_time_and_serves_in_first_come_first_served_order()
    {
        // Arrange
        var runA = await _h.SendAsync(_h.Member, _setup.Source, "甲");
        var runB = await _h.SendAsync(_h.Member2, _setup.Source, "乙");
        var runC = await _h.SendAsync(_h.Admin, _setup.Source, "丙");
        await _h.WaitForQueuedAsync(3);

        // Act
        var first = await _h.ClaimAsync(_setup.Worker);
        var while_running = await _h.CheckInAsync(_setup.Worker, capacity: 2);
        await _h.FinishAsync(_setup.Worker, first);
        var second = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, second);
        var third = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, third);
        foreach (var run in new[] { runA, runB, runC })
        {
            await run.ReadAllAsync();
        }

        // Assert
        Assert.That(first.Prompt, Does.Contain("甲"));
        Assert.That(while_running.Job, Is.Null);
        Assert.That(second.Prompt, Does.Contain("乙"));
        Assert.That(third.Prompt, Does.Contain("丙"));
    }

    [Test]
    public async Task Position_counts_the_running_job_and_those_ahead_and_updates_only_when_it_changes()
    {
        // Arrange
        var runA = await _h.SendAsync(_h.Member, _setup.Source, "甲");
        var first = await _h.ClaimAsync(_setup.Worker);
        var runB = await _h.SendAsync(_h.Member2, _setup.Source, "乙");
        await Task.Delay(300);

        // Act
        await _h.FinishAsync(_setup.Worker, first);
        await Task.Delay(300);
        var second = await _h.ClaimAsync(_setup.Worker);
        await _h.FinishAsync(_setup.Worker, second);
        await runA.ReadAllAsync();
        var events = await runB.ReadAllAsync();

        // Assert
        Assert.That(events.OfType<AssistantRunQueued>().Select(e => e.Position), Is.EqualTo(new[] { 2, 1 }));
    }

    [Test]
    public async Task Source_queue_holds_at_most_twenty()
    {
        // Arrange
        for (var i = 0; i < AiRules.HostMaxQueuedPerSource; i++)
        {
            await _h.SendAsync(_h.NewMember(), _setup.Source);
        }

        // Act
        var overflow = await _h.TrySendAsync(_h.NewMember(), _setup.Source);

        // Assert
        Assert.That(overflow.Failure?.Code, Is.EqualTo(AiErrorCodes.HostQueueFull));
    }

    [Test]
    public async Task Stopping_while_queued_saves_a_stopped_reply_and_gives_the_quota_back()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        await _h.WaitForQueuedAsync(1);

        // Act
        await run.LeaveAsync();
        var follow = await _h.CheckInAsync(_setup.Worker, capacity: 1);

        // Assert
        Assert.That(follow.Job, Is.Null);
        Assert.That(await _h.JobRowCountAsync(), Is.EqualTo(0));
        var usage = await _h.Service.GetUsageAsync(_h.Owner, _setup.Source.Id, null, default);
        Assert.That(usage.Value.Totals.Requests, Is.EqualTo(0));
    }

    [Test]
    public async Task Stopping_a_running_job_tells_the_worker_to_stop_and_saves_a_stopped_reply()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.ReportAsync(_setup.Worker, job, 1, new LocalAgentText("開始"));
        var reading = run.ReadUntilStopAsync(e => e is AssistantTextDelta);

        // Act
        var seen = false;
        for (var i = 0; i < 100 && !seen; i++)
        {
            seen = (await _h.ReportAsync(_setup.Worker, job, 2)).Cancelled;
            await Task.Delay(30);
        }

        await _h.FinishAsync(_setup.Worker, job, AssistantMessageStatus.Stopped);
        var events = await reading;

        // Assert
        Assert.That(seen, Is.True);
        Assert.That(events.OfType<AssistantRunCompleted>().Single().Message.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
        Assert.That((await _h.SourceAsync(_setup.Source.Id)).Usage.Requests, Is.EqualTo(1));
    }

    [Test]
    public async Task Waiting_too_long_fails_the_reply_and_does_not_count_as_usage()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await HostHarness.StartAsync(HostHarness.FastTimings with { QueueTimeout = TimeSpan.FromMilliseconds(400) });
        _setup = await _h.SetUpAsync();
        var run = await _h.SendAsync(_h.Member, _setup.Source);

        // Act
        var events = await run.ReadAllAsync();

        // Assert
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
        Assert.That(final.Error, Does.Contain("排隊超過"));
        Assert.That(events.OfType<AssistantRunStarted>(), Is.Empty);
        Assert.That((await _h.SourceAsync(_setup.Source.Id)).Usage.Requests, Is.EqualTo(0));
        Assert.That((await _h.CheckInAsync(_setup.Worker, capacity: 1)).Job, Is.Null);
    }

    [Test]
    public async Task Capacity_zero_only_registers()
    {
        // Arrange
        await _h.SendAsync(_h.Member, _setup.Source);
        await _h.WaitForQueuedAsync(1);

        // Act
        var result = await _h.CheckInAsync(_setup.Worker, capacity: 0);

        // Assert
        Assert.That(result.Job, Is.Null);
        Assert.That((await _h.SourceAsync(_setup.Source.Id)).Queued, Is.EqualTo(1));
    }

    [Test]
    public async Task Long_poll_wakes_up_when_a_message_is_sent()
    {
        // Arrange
        var waiting = _h.ClaimAsync(_setup.Worker, TimeSpan.FromSeconds(5));
        await Task.Delay(150);

        // Act
        await _h.SendAsync(_h.Member, _setup.Source, "叫醒");
        var job = await waiting;

        // Assert
        Assert.That(job.Prompt, Does.Contain("叫醒"));
    }

    [Test]
    public async Task Asker_who_stopped_being_a_valid_member_while_waiting_is_not_served()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source);
        await _h.WaitForQueuedAsync(1);
        _h.People.SetDisabled(_h.Assistant.OrganizationId, _h.Member.PersonId, true);

        // Act
        var result = await _h.CheckInAsync(_setup.Worker, capacity: 1);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(result.Job, Is.Null);
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
        Assert.That(final.Error, Does.Contain("有效成員"));
        Assert.That((await _h.SourceAsync(_setup.Source.Id)).Usage.Requests, Is.EqualTo(0));
    }
}
