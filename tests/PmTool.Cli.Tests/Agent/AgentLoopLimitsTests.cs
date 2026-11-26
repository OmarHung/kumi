using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;

namespace PmTool.Cli.Tests.Agent;

/// <summary>本機代理自己的上限：每小時與每天的件數、一件工作的來回次數。</summary>
[TestFixture]
public sealed class AgentLoopLimitsTests
{
    private string _directory = null!;
    private string _configPath = null!;

    private static AgentOptions Fast => new()
    {
        FlushInterval = TimeSpan.FromMilliseconds(15),
        HeartbeatInterval = TimeSpan.FromHours(1),
        MinPollGap = TimeSpan.Zero,
        BusyCheckInInterval = TimeSpan.FromMilliseconds(20),
        FinishTimeout = TimeSpan.FromSeconds(10),
    };

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"kumi-loop-limits-{Guid.NewGuid():N}")).FullName;
        _configPath = Path.Combine(_directory, "config.json");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private JobQuota Quota(int perHour, int perDay = 0) => new(_configPath, perHour, perDay, () => DateTimeOffset.Now);

    private static async Task<(StringWriter Error, FakeAgentServer Server)> RunAsync(
        FakeAgentServer server, AgentEngineStub engine, JobQuota quota, CancellationTokenSource stop)
    {
        using var http = new HttpClient(server, disposeHandler: false);
        var error = new StringWriter();
        var loop = new AgentLoop(
            new ApiClient(http, "https://pm.example.com", CliHarness.Token), [engine.Engine], "machine-key-1234", "小明的 Mac", "0.1.0", Fast,
            new AgentConsole(new StringWriter(), error, json: false), () => DateTimeOffset.Now, quota: quota);
        await loop.RunAsync(stop.Token).WaitAsync(TimeSpan.FromSeconds(20));
        return (error, server);
    }

    /// <summary>引擎一被呼叫就記下收到的工作；回傳完成。</summary>
    private sealed class AgentEngineStub
    {
        public List<AgentJob> Seen { get; } = [];

        public IAgentEngine Engine => new FakeEngine((job, _, _) =>
        {
            lock (Seen)
            {
                Seen.Add(job);
            }

            return Task.FromResult(new JobOutcome(JobStatus.Complete, null, 1, 1));
        });
    }

    [Test]
    public async Task CheckIn_WhenTheHourlyLimitIsUsedUp_StillRegistersWithCapacityZeroAndLogsOnce()
    {
        // Arrange
        var quota = Quota(perHour: 1);
        quota.TryReserve();
        using var stop = new CancellationTokenSource();
        var calls = 0;
        var server = new FakeAgentServer
        {
            CheckIn = async (_, _, cancellation) =>
            {
                if (Interlocked.Increment(ref calls) >= 3)
                {
                    stop.Cancel();
                }

                await Task.Delay(1, CancellationToken.None);
                return FakeAgentServer.Ok(new { job = (object?)null });
            },
        };

        // Act
        var (error, _) = await RunAsync(server, new AgentEngineStub(), quota, stop);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(server.CheckInBodies, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(server.CheckInBodies.Select(body => body["capacity"]!.GetValue<int>()), Has.All.Zero);
            Assert.That(error.ToString().Split('\n').Count(line => line.Contains("本機上限")), Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("每小時 1 件").And.Contain("--max-jobs-per-hour"));
        }
    }

    [Test]
    public async Task CheckIn_CapacityIsTheSmallerOfTheFreeSlotsAndWhatTheQuotaHasLeft()
    {
        // Arrange（同時最多 2 件，但每小時只剩 1 件）
        var quota = Quota(perHour: 1);
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = (_, _, _) => { stop.Cancel(); return Task.FromResult(FakeAgentServer.Ok(new { job = (object?)null })); } };

        // Act
        await RunAsync(server, new AgentEngineStub(), quota, stop);

        // Assert
        Assert.That(server.CheckInBodies.First()["capacity"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public async Task ClaimedJob_CountsAgainstTheQuotaAndSurvivesARestart()
    {
        // Arrange
        var quota = Quota(perHour: 5);
        using var stop = new CancellationTokenSource();
        var job = TestJobs.Make();
        var server = new FakeAgentServer
        {
            CheckIn = async (_, call, cancellation) =>
            {
                if (call == 1)
                {
                    return FakeAgentServer.JobResponse(job);
                }

                await Task.Delay(20, cancellation);
                return FakeAgentServer.Ok(new { job = (object?)null });
            },
            Finish = (_, _) =>
            {
                stop.Cancel();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            },
        };

        // Act
        await RunAsync(server, new AgentEngineStub(), quota, stop);

        // Assert
        Assert.That(Quota(perHour: 5).Remaining(), Is.EqualTo(4));
    }

    [Test]
    public async Task ClaimedJob_WhenTheServerSendsOneAnywayAfterTheLimit_IsNotRunAndFinishesAsFailed()
    {
        // Arrange
        var quota = Quota(perHour: 1);
        quota.TryReserve();
        using var stop = new CancellationTokenSource();
        var job = TestJobs.Make();
        var engine = new AgentEngineStub();
        var server = new FakeAgentServer
        {
            CheckIn = async (_, call, cancellation) =>
            {
                if (call == 1)
                {
                    return FakeAgentServer.JobResponse(job);
                }

                await Task.Delay(20, cancellation);
                return FakeAgentServer.Ok(new { job = (object?)null });
            },
            Finish = (_, _) =>
            {
                stop.Cancel();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            },
        };

        // Act
        var (error, _) = await RunAsync(server, engine, quota, stop);

        // Assert
        var finish = server.FinishBodies(job.JobId).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.Seen, Is.Empty);
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(finish["error"]!.GetValue<string>(), Does.Contain("上限"));
            Assert.That(error.ToString(), Does.Contain("不執行"));
        }
    }

    [Test]
    public async Task ClaimedJob_WithMoreTurnsThanTheLocalLimit_RunsWithTheLocalLimit()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        var job = TestJobs.Make() with { MaxTurns = 500 };
        var engine = new AgentEngineStub();
        var server = new FakeAgentServer
        {
            CheckIn = async (_, call, cancellation) =>
            {
                if (call == 1)
                {
                    return FakeAgentServer.JobResponse(job);
                }

                await Task.Delay(20, cancellation);
                return FakeAgentServer.Ok(new { job = (object?)null });
            },
            Finish = (_, _) =>
            {
                stop.Cancel();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            },
        };

        // Act
        await RunAsync(server, engine, Quota(perHour: 0), stop);

        // Assert
        Assert.That(engine.Seen.Single().MaxTurns, Is.EqualTo(60));
    }

    [TestCase(HttpStatusCode.Forbidden, "auth.insufficient_scope", false)]
    [TestCase(HttpStatusCode.Forbidden, "ai.personal_disabled", false)]
    [TestCase(HttpStatusCode.Unauthorized, "auth.token_invalid", false)]
    [TestCase(HttpStatusCode.ServiceUnavailable, "server.unavailable", true)]
    [TestCase(HttpStatusCode.TooManyRequests, "rate_limited", true)]
    public void IsTransient_OnlyRetriesWhatWaitingCanFix(HttpStatusCode status, string code, bool expected) =>
        Assert.That(AgentLoop.IsTransient(new CliException(ExitCodes.Error, "x", code: code, httpStatus: (int)status)), Is.EqualTo(expected));

    [Test]
    public void IsTransient_AMissingScopeIsNeverTransientEvenWithoutAStatus() =>
        Assert.That(AgentLoop.IsTransient(new CliException(ExitCodes.Error, "缺少範圍", code: AgentLoop.MissingScopesCode)), Is.False);

    [Test]
    public void IsTransient_ANetworkFailureWithoutAStatusIsTransient() =>
        Assert.That(AgentLoop.IsTransient(new CliException(ExitCodes.Error, "連不上")), Is.True);
}
