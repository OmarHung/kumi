using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.LocalAgents;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>配額：每人每天、來源每月的則數、每月 token。預留在來源的鎖裡原子完成，同時送出不會一起超過；排隊時取消的不算。</summary>
[TestFixture]
public sealed class HostQuotaTests
{
    private HostHarness _h = null!;

    [SetUp]
    public async Task SetUp() => _h = await HostHarness.StartAsync();

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private async Task CompleteOneAsync(HostSetup setup, LocalRun run, AssistantUsage? usage = null, AssistantMessageStatus status = AssistantMessageStatus.Complete)
    {
        var job = await _h.ClaimAsync(setup.Worker);

        // 失敗又沒有任何進度、沒有用量的，引擎根本沒跑起來，不算用量；先回報一則，才是「跑過但失敗」。
        await _h.ReportAsync(setup.Worker, job, 1, new LocalAgentText("跑過"));
        await _h.FinishAsync(setup.Worker, job, status, status == AssistantMessageStatus.Failed ? "壞了" : null, usage);
        await run.ReadAllAsync();
    }

    [Test]
    public async Task Concurrent_sends_never_exceed_the_monthly_request_limit()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(null, 3, null));
        var people = Enumerable.Range(0, 8).Select(_ => _h.NewMember()).ToList();

        // Act
        var results = await Task.WhenAll(people.Select(p => _h.TrySendAsync(p, setup.Source)));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(3));
        Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Is.All.EqualTo(AiErrorCodes.HostQuotaExceeded));
        Assert.That(await _h.JobRowCountAsync(), Is.EqualTo(3));
        foreach (var run in results.Where(r => r.Succeeded))
        {
            await run.Value.Events.GetAsyncEnumerator().DisposeAsync();
        }
    }

    [Test]
    public async Task Concurrent_sends_by_one_person_never_exceed_the_daily_limit_or_the_one_waiting_rule()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(1, null, null));

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _h.TrySendAsync(_h.Member, setup.Source)));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Is.All.AnyOf(AiErrorCodes.HostQuotaExceeded, AiErrorCodes.HostQueueFull));
        await results.Single(r => r.Succeeded).Value.Events.GetAsyncEnumerator().DisposeAsync();
    }

    [Test]
    public async Task Daily_limit_is_per_person_and_resets_the_next_day_in_the_organization_time_zone()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(1, null, null));
        await CompleteOneAsync(setup, await _h.SendAsync(_h.Member, setup.Source));

        // Act
        var sameDay = await _h.TrySendAsync(_h.Member, setup.Source);
        var otherPerson = await _h.TrySendAsync(_h.Member2, setup.Source);
        _h.Clock.Advance(TimeSpan.FromHours(24));
        await _h.CheckInAsync(setup.Worker);
        var nextDay = await _h.TrySendAsync(_h.Member, setup.Source);

        // Assert
        Assert.That(sameDay.Failure?.Code, Is.EqualTo(AiErrorCodes.HostQuotaExceeded));
        Assert.That(sameDay.Failure!.Message, Does.Contain("今天"));
        Assert.That(otherPerson.Succeeded, Is.True);
        Assert.That(nextDay.Succeeded, Is.True);
    }

    [Test]
    public async Task Day_boundary_follows_the_organization_time_zone_not_utc()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(1, null, null));

        // 時鐘從 UTC 08:00（台北 16:00）開始；加 7 小時是 UTC 15:00（台北 23:00），還是同一天。
        _h.Clock.Advance(TimeSpan.FromHours(7));
        await _h.CheckInAsync(setup.Worker);
        await CompleteOneAsync(setup, await _h.SendAsync(_h.Member, setup.Source));

        // Act
        _h.Clock.Advance(TimeSpan.FromHours(2));
        await _h.CheckInAsync(setup.Worker);
        var nextTaipeiDay = await _h.TrySendAsync(_h.Member, setup.Source);

        // Assert
        Assert.That(nextTaipeiDay.Succeeded, Is.True);
    }

    [Test]
    public async Task Monthly_token_limit_counts_finished_replies_only()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(null, null, 100));
        var first = await _h.SendAsync(_h.Member, setup.Source);
        var inFlight = await _h.TrySendAsync(_h.Member2, setup.Source);
        await CompleteOneAsync(setup, first, new AssistantUsage(60, 50));

        // Act
        var after = await _h.TrySendAsync(_h.NewMember(), setup.Source);

        // Assert
        Assert.That(inFlight.Succeeded, Is.True);
        Assert.That(after.Failure?.Code, Is.EqualTo(AiErrorCodes.HostQuotaExceeded));
        Assert.That(after.Failure!.Message, Does.Contain("token"));
        var usage = (await _h.SourceAsync(setup.Source.Id)).Usage;
        Assert.That((usage.InputTokens, usage.OutputTokens), Is.EqualTo((60L, 50L)));
    }

    [Test]
    public async Task Failed_and_stopped_replies_count_but_cancelled_while_queued_do_not()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(null, 2, null));
        await CompleteOneAsync(setup, await _h.SendAsync(_h.Member, setup.Source), status: AssistantMessageStatus.Failed);
        var queued = await _h.SendAsync(_h.Member2, setup.Source);
        await _h.WaitForQueuedAsync(1);
        await queued.LeaveAsync();

        // Act
        var next = await _h.TrySendAsync(_h.Member2, setup.Source);
        var stoppedRun = next.Value;
        var job = await _h.ClaimAsync(setup.Worker);
        await _h.FinishAsync(setup.Worker, job, AssistantMessageStatus.Stopped);
        await AssistantHarness_ReadAsync(stoppedRun);
        var over = await _h.TrySendAsync(_h.Member, setup.Source);

        // Assert
        var usage = (await _h.SourceAsync(setup.Source.Id)).Usage;
        Assert.That((usage.Requests, usage.Failed, usage.Stopped), Is.EqualTo((2, 1, 1)));
        Assert.That(over.Failure?.Code, Is.EqualTo(AiErrorCodes.HostQuotaExceeded));
    }

    [Test]
    public async Task A_reservation_that_never_became_a_queued_job_is_given_back_on_the_next_worker_check_in()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(null, 1, null));
        var reservations = _h.Host.Get<PmTool.Ai.HostAgents.HostReservations>();
        var reserved = await reservations.ReserveAsync(_h.Member, setup.Source.Id, "WEB", Guid.NewGuid(), Guid.NewGuid(), default);
        var blocked = await _h.TrySendAsync(_h.Member2, setup.Source);

        // Act
        _h.Clock.Advance(TimeSpan.FromMinutes(2));
        await _h.CheckInAsync(setup.Worker);
        var afterSweep = await _h.TrySendAsync(_h.Member2, setup.Source);

        // Assert
        Assert.That(reserved.Succeeded, Is.True);
        Assert.That(blocked.Failure?.Code, Is.EqualTo(AiErrorCodes.HostQuotaExceeded));
        Assert.That(afterSweep.Succeeded, Is.True, () => afterSweep.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task Releasing_a_reservation_gives_the_quota_back()
    {
        // Arrange
        var setup = await _h.SetUpAsync(limits: new HostSourceLimits(null, 1, null));
        var reservations = _h.Host.Get<PmTool.Ai.HostAgents.HostReservations>();
        var reserved = await reservations.ReserveAsync(_h.Member, setup.Source.Id, "WEB", Guid.NewGuid(), Guid.NewGuid(), default);

        // Act
        await reservations.ReleaseAsync(reserved.Value);
        var again = await reservations.ReserveAsync(_h.Member, setup.Source.Id, "WEB", Guid.NewGuid(), Guid.NewGuid(), default);

        // Assert
        Assert.That(again.Succeeded, Is.True);
    }

    private static Task<IReadOnlyList<AssistantRunEvent>> AssistantHarness_ReadAsync(AssistantRun run) =>
        PmTool.Ai.Tests.Assistant.Fakes.AssistantHarness.ReadAllAsync(run);
}
