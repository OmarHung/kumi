using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class AgentLoopTests
{
    private const string Server = "https://pm.example.com";

    private readonly List<TimeSpan> _waits = [];

    private AgentOptions FastOptions => new()
    {
        FlushInterval = TimeSpan.FromMilliseconds(15),
        HeartbeatInterval = TimeSpan.FromHours(1),
        MinPollGap = TimeSpan.Zero,
        BusyCheckInInterval = TimeSpan.FromMilliseconds(20),
        FinishTimeout = TimeSpan.FromSeconds(10),
        // 退避與重試只記下要等多久，不真的等；其他等待照常。
        Delay = (duration, cancellation) =>
        {
            lock (_waits)
            {
                _waits.Add(duration);
            }

            return duration >= TimeSpan.FromSeconds(1) ? Task.Delay(1, cancellation) : Task.Delay(duration, cancellation);
        },
    };

    private sealed class Rig(FakeAgentServer server, AgentLoop loop, StringWriter output, StringWriter error, HttpClient http) : IDisposable
    {
        public FakeAgentServer Server { get; } = server;

        public string Out => output.ToString();

        public string Error => error.ToString();

        public Task RunAsync(CancellationToken stop) => loop.RunAsync(stop);

        public void Dispose() => http.Dispose();
    }

    private Rig NewRig(FakeAgentServer server, IReadOnlyList<IAgentEngine> engines, AgentOptions? options = null, bool json = false)
    {
        var http = new HttpClient(server, disposeHandler: false);
        var output = new StringWriter();
        var error = new StringWriter();
        var effective = options ?? FastOptions;
        var loop = new AgentLoop(
            new ApiClient(http, Server, CliHarness.Token), engines, "machine-key-1234", "小明的 Mac", "0.1.0", effective,
            new AgentConsole(output, error, json), () => DateTimeOffset.Now);
        return new Rig(server, loop, output, error, http);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(20));

    private static async Task WithTimeout(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(20));

    /// <summary>第一次登記給一件工作，之後都是空的（要等就等，直到被取消）。</summary>
    private static Func<JsonNode, int, CancellationToken, Task<HttpResponseMessage>> OneJob(params AgentJob[] jobs) => async (_, call, cancellation) =>
    {
        if (call <= jobs.Length)
        {
            return FakeAgentServer.JobResponse(jobs[call - 1]);
        }

        await Task.Delay(20, cancellation);
        return FakeAgentServer.Ok(new { job = (object?)null });
    };

    [Test]
    public async Task CheckIn_SendsTheMachineItsEnginesWithModelsAndCapacity()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = (_, _, _) => { stop.Cancel(); return Task.FromResult(FakeAgentServer.Ok(new { job = (object?)null })); } };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(JobOutcome.Stopped()))]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var body = server.CheckInBodies.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body["machineKey"]!.GetValue<string>(), Is.EqualTo("machine-key-1234"));
            Assert.That(body["machineName"]!.GetValue<string>(), Is.EqualTo("小明的 Mac"));
            Assert.That(body["agentVersion"]!.GetValue<string>(), Is.EqualTo("0.1.0"));
            Assert.That(body["capacity"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(body["engines"]![0]!["kind"]!.GetValue<string>(), Is.EqualTo("claude_code"));
            Assert.That(body["engines"]![0]!["version"]!.GetValue<string>(), Is.EqualTo("9.9.9"));
            Assert.That(body["engines"]![0]!["models"]![1]!["id"]!.GetValue<string>(), Is.EqualTo("sonnet"));
            Assert.That(body["engines"]![0]!["models"]![1]!["displayName"]!.GetValue<string>(), Is.EqualTo("Sonnet"));
            Assert.That(body["engines"]![0]!["models"]![1]!["efforts"]!.AsArray().Select(effort => effort!.GetValue<string>()), Is.EqualTo(new[] { "low", "high" }));
            Assert.That(server.Requests.Single().Path, Is.EqualTo("/v1/agent/check-in"));
        }
    }

    [Test]
    public async Task ClaimedJob_RunsReportsAndFinishesWithUsage()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Finish = (_, _) =>
        {
            stop.Cancel();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };
        var engine = new FakeEngine((_, sink, _) =>
        {
            sink.Emit(new TextEvent("你好"));
            sink.Emit(new ToolEvent("c1", "get_blueprint", "running"));
            sink.Emit(new ToolEvent("c1", "get_blueprint", "done", ChangeSetNumber: 4));
            return Task.FromResult(new JobOutcome(JobStatus.Complete, null, 100, 20));
        });
        using var rig = NewRig(server, [engine]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var events = server.EventBodies(job.JobId).SelectMany(body => body["events"]!.AsArray()).ToList();
        var finish = server.FinishBodies(job.JobId).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.Select(item => item!["type"]!.GetValue<string>()), Is.EqualTo(new[] { "text", "tool", "tool" }));
            Assert.That(events[0]!["delta"]!.GetValue<string>(), Is.EqualTo("你好"));
            Assert.That(events[2]!["changeSetNumber"]!.GetValue<long>(), Is.EqualTo(4));
            Assert.That(events[1]!["changeSetNumber"], Is.Null);
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("complete"));
            Assert.That(finish["error"], Is.Null);
            Assert.That(finish["usage"]!["inputTokens"]!.GetValue<long>(), Is.EqualTo(100));
            Assert.That(finish["usage"]!["outputTokens"]!.GetValue<long>(), Is.EqualTo(20));
            Assert.That(rig.Out, Does.Contain("DEMO").And.Contain("Claude Code").And.Contain("完成"));
            Assert.That(rig.Out, Does.Not.Contain("你好"), "終端機不印對話的內容");
        }
    }

    [Test]
    public async Task Capacity_ShrinksWithRunningJobsAndNothingIsClaimedWhenFull()
    {
        // Arrange
        var first = TestJobs.Make();
        var second = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var release = new TaskCompletionSource();
        var started = 0;
        var server = new FakeAgentServer { CheckIn = OneJob(first, second) };
        var engine = new FakeEngine(async (_, _, cancellation) =>
        {
            Interlocked.Increment(ref started);
            await release.Task.WaitAsync(cancellation);
            return new JobOutcome(JobStatus.Complete);
        });
        using var rig = NewRig(server, [engine]);
        var run = rig.RunAsync(stop.Token);

        // Act
        await WaitUntil(() => started == 2 && server.CheckInBodies.Count >= 4);
        var capacities = server.CheckInBodies.Select(body => body["capacity"]!.GetValue<int>()).ToList();
        release.SetResult();
        await WaitUntil(() => server.FinishBodies(second.JobId).Count == 1 && server.FinishBodies(first.JobId).Count == 1);
        stop.Cancel();
        await WithTimeout(run);

        // Assert
        Assert.That(capacities.Take(4), Is.EqualTo(new[] { 2, 1, 0, 0 }));
    }

    [Test]
    public async Task Reports_GoOutAsSoonAsFiftyEventsAreWaitingWithoutWaitingForTheTimer()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var reported = new TaskCompletionSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Events = (_, _) =>
        {
            reported.TrySetResult();
            return FakeAgentServer.Ok(new { cancelled = false });
        };
        var engine = new FakeEngine(async (_, sink, _) =>
        {
            for (var index = 0; index < 50; index++)
            {
                sink.Emit(new ToolEvent($"c{index}", "get_blueprint", "running"));
            }

            await WithTimeout(reported.Task);
            return new JobOutcome(JobStatus.Complete);
        });
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        using var rig = NewRig(server, [engine], FastOptions with { FlushInterval = TimeSpan.FromHours(1) });

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        Assert.That(server.EventBodies(job.JobId).First()["events"]!.AsArray(), Has.Count.EqualTo(50));
    }

    [Test]
    public async Task Reports_MergeConsecutiveTextAndSplitLongTextAtTheServerLimit()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        var engine = new FakeEngine((_, sink, _) =>
        {
            sink.Emit(new TextEvent(new string('a', 12000)));
            sink.Emit(new TextEvent(new string('b', 8000)));
            return Task.FromResult(new JobOutcome(JobStatus.Complete));
        });
        using var rig = NewRig(server, [engine], FastOptions with { FlushInterval = TimeSpan.FromHours(1) });

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var deltas = server.EventBodies(job.JobId).SelectMany(body => body["events"]!.AsArray()).Select(item => item!["delta"]!.GetValue<string>()).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deltas.Select(delta => delta.Length), Is.EqualTo(new[] { 8000, 8000, 4000 }));
            Assert.That(string.Concat(deltas), Is.EqualTo(new string('a', 12000) + new string('b', 8000)));
        }
    }

    [Test]
    public async Task Reports_NeverSplitASurrogatePair()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        var text = new string('a', 7999) + "😀" + new string('b', 10);
        var engine = new FakeEngine((_, sink, _) =>
        {
            sink.Emit(new TextEvent(text));
            return Task.FromResult(new JobOutcome(JobStatus.Complete));
        });
        using var rig = NewRig(server, [engine]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var deltas = server.EventBodies(job.JobId).SelectMany(body => body["events"]!.AsArray()).Select(item => item!["delta"]!.GetValue<string>()).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deltas, Has.Count.EqualTo(2));
            Assert.That(string.Concat(deltas), Is.EqualTo(text));
            Assert.That(deltas.All(delta => !char.IsLowSurrogate(delta[0]) && !char.IsHighSurrogate(delta[^1])), Is.True);
        }
    }

    [Test]
    public async Task Cancel_WhenTheServerAnswersCancelled_StopsTheEngineAndFinishesAsStopped()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Events = (_, _) => FakeAgentServer.Ok(new { cancelled = true });
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        var engine = new FakeEngine(async (_, sink, cancellation) =>
        {
            sink.Emit(new TextEvent("寫到一半"));
            await Task.Delay(Timeout.Infinite, cancellation).ContinueWith(_ => { });
            return JobOutcome.Stopped();
        });
        using var rig = NewRig(server, [engine]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var finish = server.FinishBodies(job.JobId).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("stopped"));
            Assert.That(finish["error"], Is.Null, "使用者按停止不是錯誤");
            Assert.That(rig.Out, Does.Contain("中斷"));
        }
    }

    [Test]
    public async Task Cancel_WhenTheServerNoLongerKnowsTheJob_StopsTheEngine()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Events = (_, _) => FakeAgentServer.Failure(HttpStatusCode.NotFound, "ai.agent_job_not_found", "找不到這件工作");
        server.Finish = (_, _) => { stop.Cancel(); return FakeAgentServer.Failure(HttpStatusCode.NotFound, "ai.agent_job_not_found", "找不到這件工作"); };
        var engine = new FakeEngine(async (_, sink, cancellation) =>
        {
            sink.Emit(new TextEvent("x"));
            await Task.Delay(Timeout.Infinite, cancellation).ContinueWith(_ => { });
            return JobOutcome.Stopped();
        });
        using var rig = NewRig(server, [engine]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        Assert.That(rig.Error, Does.Not.Contain("沒辦法送出"), "結束時伺服器說找不到：已經結束了，不用吵使用者");
    }

    [Test]
    public async Task Interrupt_StopsRunningJobsFinishesThemAndReturnsNormally()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var running = new TaskCompletionSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        var engine = new FakeEngine(async (_, _, cancellation) =>
        {
            running.SetResult();
            await Task.Delay(Timeout.Infinite, cancellation).ContinueWith(_ => { });
            return JobOutcome.Stopped();
        });
        using var rig = NewRig(server, [engine]);
        var run = rig.RunAsync(stop.Token);
        await WithTimeout(running.Task);

        // Act
        await stop.CancelAsync();
        await WithTimeout(run);

        // Assert
        var finish = server.FinishBodies(job.JobId).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("stopped"));
            Assert.That(finish["error"]!.GetValue<string>(), Does.Contain("被關掉"));
        }
    }

    [Test]
    public async Task EngineThrowing_BecomesAFailedFinishWithOnlyTheExceptionType()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        var engine = new FakeEngine((_, _, _) => throw new InvalidOperationException("內容：pmt_secret /Users/someone/x"));
        using var rig = NewRig(server, [engine]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var finish = server.FinishBodies(job.JobId).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(finish["error"]!.GetValue<string>(), Does.Contain("InvalidOperationException").And.Not.Contain("pmt_secret"));
        }
    }

    [Test]
    public async Task JobForAnEngineThisComputerDoesNotOffer_FailsWithAClearReason()
    {
        // Arrange
        var job = TestJobs.Make(EngineKinds.Codex);
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(new JobOutcome(JobStatus.Complete)))]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var finish = server.FinishBodies(job.JobId).Single();
        Assert.That(finish["error"]!.GetValue<string>(), Is.EqualTo("這台電腦沒有提供 Codex。"));
    }

    [Test]
    public async Task MalformedJob_IsSkippedWithANoticeAndTheLoopKeepsGoing()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer
        {
            CheckIn = (_, call, _) =>
            {
                if (call == 2)
                {
                    stop.Cancel();
                }

                return Task.FromResult(FakeAgentServer.Ok(new { job = new { jobId = Guid.NewGuid() } }));
            },
        };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(new JobOutcome(JobStatus.Complete)))]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rig.Error, Does.Contain("少了必要的欄位"));
            Assert.That(server.CheckInBodies, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task NetworkErrors_BackOffOneTwoFourSecondsAndRecover()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer
        {
            CheckIn = (_, call, _) =>
            {
                if (call <= 3)
                {
                    throw new HttpRequestException("連線被拒");
                }

                stop.Cancel();
                return Task.FromResult(FakeAgentServer.Ok(new { job = (object?)null }));
            },
        };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(new JobOutcome(JobStatus.Complete)))]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        lock (_waits)
        {
            Assert.That(_waits.Where(wait => wait >= TimeSpan.FromSeconds(1)).Take(3), Is.EqualTo(new[]
            {
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
            }));
        }

        Assert.That(rig.Error, Does.Contain("連不上伺服器").And.Contain("1 秒後重試"));
    }

    [Test]
    public async Task Backoff_IsCappedAtThirtySeconds()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer
        {
            CheckIn = (_, call, _) =>
            {
                if (call > 9)
                {
                    stop.Cancel();
                    return Task.FromResult(FakeAgentServer.Ok(new { job = (object?)null }));
                }

                return Task.FromResult(FakeAgentServer.Failure(HttpStatusCode.BadGateway, "bad_gateway", "上游沒有回應"));
            },
        };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(new JobOutcome(JobStatus.Complete)))]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        lock (_waits)
        {
            Assert.That(_waits.Max(), Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
    }

    [Test]
    public async Task Unauthorized_StopsWithTheTokenMessage()
    {
        // Arrange
        var server = new FakeAgentServer { CheckIn = (_, _, _) => Task.FromResult(FakeAgentServer.Failure(HttpStatusCode.Unauthorized, "auth.unauthorized", "x")) };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(new JobOutcome(JobStatus.Complete)))]);

        // Act
        var exception = Assert.ThrowsAsync<CliException>(async () => await WithTimeout(rig.RunAsync(CancellationToken.None)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.HttpStatus, Is.EqualTo(401));
            Assert.That(exception.Message, Does.Contain("權杖無效"));
            Assert.That(server.CheckInBodies, Has.Count.EqualTo(1), "不重試");
        }
    }

    [TestCase("ai.personal_disabled", "AI 設定")]
    [TestCase("auth.token_scope", "agent:run")]
    public async Task Forbidden_ExplainsWhatToDo(string code, string expectedHint)
    {
        // Arrange
        var server = new FakeAgentServer { CheckIn = (_, _, _) => Task.FromResult(FakeAgentServer.Failure(HttpStatusCode.Forbidden, code, "原文")) };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(new JobOutcome(JobStatus.Complete)))]);

        // Act
        var exception = Assert.ThrowsAsync<CliException>(async () => await WithTimeout(rig.RunAsync(CancellationToken.None)));

        // Assert
        Assert.That(exception.Message, Does.Contain(expectedHint));
    }

    [Test]
    public async Task UnknownEndpoint_SaysTheServerDoesNotSupportTheAgent()
    {
        // Arrange
        var server = new FakeAgentServer { CheckIn = (_, _, _) => Task.FromResult(FakeAgentServer.Failure(HttpStatusCode.NotFound, "not_found", "x")) };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(new JobOutcome(JobStatus.Complete)))]);

        // Act
        var exception = Assert.ThrowsAsync<CliException>(async () => await WithTimeout(rig.RunAsync(CancellationToken.None)));

        // Assert
        Assert.That(exception.Message, Does.Contain("還不支援本機代理"));
    }

    [Test]
    public async Task Unauthorized_WhileReporting_StopsTheWholeAgentAfterFinishingTheJob()
    {
        // Arrange
        var job = TestJobs.Make();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Events = (_, _) => FakeAgentServer.Failure(HttpStatusCode.Unauthorized, "auth.unauthorized", "x");
        var engine = new FakeEngine(async (_, sink, cancellation) =>
        {
            sink.Emit(new TextEvent("x"));
            await Task.Delay(Timeout.Infinite, cancellation).ContinueWith(_ => { });
            return JobOutcome.Stopped();
        });
        using var rig = NewRig(server, [engine]);

        // Act
        var exception = Assert.ThrowsAsync<CliException>(async () => await WithTimeout(rig.RunAsync(CancellationToken.None)));

        // Assert
        Assert.That(exception.HttpStatus, Is.EqualTo(401));
    }

    [Test]
    public async Task ReportFailingOnce_IsRetriedInOrderWithoutLosingEvents()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        var attempts = 0;
        server.Events = (_, _) => Interlocked.Increment(ref attempts) == 1
            ? throw new HttpRequestException("斷線")
            : FakeAgentServer.Ok(new { cancelled = false });
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        var engine = new FakeEngine((_, sink, _) =>
        {
            sink.Emit(new ToolEvent("a", "get_blueprint", "running"));
            sink.Emit(new ToolEvent("a", "get_blueprint", "done"));
            return Task.FromResult(new JobOutcome(JobStatus.Complete));
        });
        using var rig = NewRig(server, [engine], FastOptions with { FlushInterval = TimeSpan.FromHours(1) });

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var delivered = server.EventBodies(job.JobId).Skip(1).SelectMany(body => body["events"]!.AsArray()).Select(item => item!["status"]!.GetValue<string>());
        Assert.That(delivered, Is.EqualTo(new[] { "running", "done" }));
    }

    [Test]
    public async Task Heartbeat_SendsAnEmptyReportWhileTheEngineIsQuiet()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Events = (_, _) => FakeAgentServer.Ok(new { cancelled = false });
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        var engine = new FakeEngine(async (_, _, cancellation) =>
        {
            await Task.Delay(300, cancellation);
            return new JobOutcome(JobStatus.Complete);
        });
        using var rig = NewRig(server, [engine], FastOptions with { HeartbeatInterval = TimeSpan.FromMilliseconds(30) });

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        Assert.That(server.EventBodies(job.JobId).Count(body => body["events"]!.AsArray().Count == 0), Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task FinishFailing_IsRetriedAndThenReportedToTheUser()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        var attempts = 0;
        server.Finish = (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 3)
            {
                stop.Cancel();
            }

            throw new HttpRequestException("斷線");
        };
        using var rig = NewRig(server, [new FakeEngine((_, _, _) => Task.FromResult(new JobOutcome(JobStatus.Complete)))]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(rig.Error, Does.Contain("沒辦法送出工作的結果"));
        }
    }

    [Test]
    public async Task JsonMode_PrintsOneJsonLinePerJobWithoutConversationContent()
    {
        // Arrange
        var job = TestJobs.Make(model: "sonnet");
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        var engine = new FakeEngine((_, sink, _) =>
        {
            sink.Emit(new TextEvent("機密的對話內容"));
            return Task.FromResult(JobOutcome.Failed("本機的 Claude Code 沒有登入"));
        });
        using var rig = NewRig(server, [engine], json: true);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var line = JsonNode.Parse(rig.Out.Trim())!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rig.Out.Trim().Split('\n'), Has.Length.EqualTo(1));
            Assert.That(line["event"]!.GetValue<string>(), Is.EqualTo("job"));
            Assert.That(line["jobId"]!.GetValue<Guid>(), Is.EqualTo(job.JobId));
            Assert.That(line["project"]!.GetValue<string>(), Is.EqualTo("DEMO"));
            Assert.That(line["engine"]!.GetValue<string>(), Is.EqualTo("claude_code"));
            Assert.That(line["model"]!.GetValue<string>(), Is.EqualTo("sonnet"));
            Assert.That(line["status"]!.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(line["error"]!.GetValue<string>(), Does.Contain("沒有登入"));
            Assert.That(line["seconds"]!.GetValue<double>(), Is.GreaterThanOrEqualTo(0));
            Assert.That(rig.Out, Does.Not.Contain("機密"));
        }
    }

    [Test]
    public async Task Cancel_WhateverTheEngineReturnsAfterBeingInterrupted_FinishesAsStoppedWithoutItsDiagnostics()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        server.Events = (_, _) => FakeAgentServer.Ok(new { cancelled = true });
        server.Finish = (_, _) => { stop.Cancel(); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        var engine = new FakeEngine(async (_, sink, cancellation) =>
        {
            sink.Emit(new TextEvent("x"));
            await Task.Delay(Timeout.Infinite, cancellation).ContinueWith(_ => { });
            return new JobOutcome(JobStatus.Failed, "[ede_diagnostic] result_type=user stop_reason=tool_use", 5, 5);
        });
        using var rig = NewRig(server, [engine]);

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        var finish = server.FinishBodies(job.JobId).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("stopped"));
            Assert.That(finish["error"], Is.Null);
            Assert.That(finish["usage"], Is.Null);
            Assert.That(rig.Out, Does.Contain("中斷").And.Not.Contain("ede_diagnostic"));
            Assert.That(rig.Error, Does.Not.Contain("ede_diagnostic"));
        }
    }

    [Test]
    public async Task Interrupt_AlsoOverridesADiagnosticFailureWithTheShutdownMessage()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var running = new TaskCompletionSource();
        var server = new FakeAgentServer { CheckIn = OneJob(job) };
        var engine = new FakeEngine(async (_, _, cancellation) =>
        {
            running.SetResult();
            await Task.Delay(Timeout.Infinite, cancellation).ContinueWith(_ => { });
            return JobOutcome.Failed("[ede_diagnostic]");
        });
        using var rig = NewRig(server, [engine]);
        var run = rig.RunAsync(stop.Token);
        await WithTimeout(running.Task);

        // Act
        await stop.CancelAsync();
        await WithTimeout(run);

        // Assert
        var finish = server.FinishBodies(job.JobId).Single();
        Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("stopped"));
        Assert.That(finish["error"]!.GetValue<string>(), Does.Contain("被關掉").And.Not.Contain("ede_diagnostic"));
    }

    [Test]
    public async Task DuplicateJobIdAndJobsBeyondTheCapacity_AreSkippedWithANotice()
    {
        // Arrange
        var job = TestJobs.Make();
        using var stop = new CancellationTokenSource();
        var release = new TaskCompletionSource();
        var started = 0;
        var server = new FakeAgentServer
        {
            CheckIn = async (_, call, cancellation) =>
            {
                if (call >= 4)
                {
                    stop.Cancel();
                    return FakeAgentServer.Ok(new { job = (object?)null });
                }

                await Task.Delay(5, cancellation);
                return FakeAgentServer.JobResponse(call == 3 ? TestJobs.Make() : job);
            },
        };
        var engine = new FakeEngine(async (_, _, cancellation) =>
        {
            Interlocked.Increment(ref started);
            await release.Task.WaitAsync(cancellation).ContinueWith(_ => { });
            return new JobOutcome(JobStatus.Complete);
        });
        using var rig = NewRig(server, [engine], FastOptions with { MaxConcurrentJobs = 1 });

        // Act
        await WithTimeout(rig.RunAsync(stop.Token));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started, Is.EqualTo(1));
            Assert.That(rig.Error, Does.Contain("超過同時能跑的件數或編號重複"));
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "等太久了");
            await Task.Delay(10);
        }
    }
}
