using PmTool.Abstractions;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>
/// 擁有者按「登出」之後，worker 要馬上知道（不等長輪詢的 25 秒），網頁在 worker 回報之前看得出「登出中」，
/// 不會顯示成「需要重新授權、請重新登入」而讓人以為登出沒成功。長輪詢故意設得很長，等滿了測試就會逾時。
/// </summary>
[TestFixture]
public sealed class HostLogoutTests
{
    private static readonly TimeSpan LongPollWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private HostHarness _h = null!;
    private HostSetup _setup = null!;

    [SetUp]
    public async Task SetUp()
    {
        _h = await HostHarness.StartAsync(HostHarness.FastTimings with { PollWait = LongPollWait });
        _setup = await _h.SetUpAsync();
    }

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private async Task<HostCheckInResult> WithinPatienceAsync(Task<Result<HostCheckInResult>> checkIn)
    {
        var finished = await Task.WhenAny(checkIn, Task.Delay(Patience));
        Assert.That(finished, Is.SameAs(checkIn), "登記等到長輪詢結束才回，worker 要很久才會登出。");
        var result = await checkIn;
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
        return result.Value;
    }

    [Test]
    public async Task Worker_still_logged_in_after_being_told_keeps_waiting_so_it_does_not_spin()
    {
        // Arrange：已經要求過登出，worker 登出失敗、這次登記還說登入著。
        await _h.Service.LogoutAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.ClaudeCode, default);
        using var stop = new CancellationTokenSource();

        // Act
        var checkIn = _h.Worker.CheckInAsync(_setup.Worker.Identity, _setup.Worker.CheckIn(), 1, stop.Token);
        var finished = await Task.WhenAny(checkIn, Task.Delay(TimeSpan.FromMilliseconds(400)));
        await stop.CancelAsync();
        var result = await checkIn;

        // Assert：沒有馬上回（不然 worker 會不停地登記、重跑登出），結束時照樣帶著要登出的引擎。
        Assert.That(finished, Is.Not.SameAs(checkIn));
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
        Assert.That(result.Value.Logout, Is.EqualTo(new[] { AiProviderKind.ClaudeCode }));
    }

    [Test]
    public async Task Logout_wakes_a_worker_that_is_already_waiting_and_tells_it_to_log_out()
    {
        // Arrange
        var waiting = _h.Worker.CheckInAsync(_setup.Worker.Identity, _setup.Worker.CheckIn(), 1, default);
        await Task.Delay(150);

        // Act
        await _h.Service.LogoutAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.ClaudeCode, default);
        var result = await WithinPatienceAsync(waiting);

        // Assert
        Assert.That(result.Logout, Is.EqualTo(new[] { AiProviderKind.ClaudeCode }));
    }

    [Test]
    public async Task Engine_shows_logout_pending_until_the_worker_reports_it_is_logged_out()
    {
        // Arrange
        await _h.Service.LogoutAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.ClaudeCode, default);

        // Act
        var pending = (await _h.AgentAsync(_setup.Worker.AgentId)).Engines.Single();
        _setup.Worker.Reports = [HostHarness.Report(loggedIn: false)];
        await _h.CheckInAsync(_setup.Worker);
        var done = (await _h.AgentAsync(_setup.Worker.AgentId)).Engines.Single();

        // Assert
        Assert.That(pending.LogoutPending, Is.True);
        Assert.That(pending.Problem, Does.Contain("登出"));
        Assert.That(pending.Problem, Does.Not.Contain("重新登入"));
        Assert.That(done.LogoutPending, Is.False);
        Assert.That(done.Readiness, Is.EqualTo(HostEngineReadiness.NotLoggedIn));
    }

    [Test]
    public async Task Engine_is_not_pending_when_no_logout_was_requested()
    {
        // Act
        var engine = (await _h.AgentAsync(_setup.Worker.AgentId)).Engines.Single();

        // Assert
        Assert.That(engine.LogoutPending, Is.False);
        Assert.That(engine.Readiness, Is.EqualTo(HostEngineReadiness.Ready));
    }
}
