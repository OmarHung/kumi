using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;
using PmTool.Cli.Host;

namespace PmTool.Cli.Tests.Host;

[TestFixture]
public sealed class HostJobReporterTests
{
    private static readonly Guid JobId = Guid.Parse("0190a000-0000-7000-8000-0000000000bb");
    private static readonly string EventsPath = $"/v1/host-agent/jobs/{JobId}/events";

    private readonly AgentOptions _options = new()
    {
        Delay = (_, _) => Task.CompletedTask,
        HeartbeatInterval = TimeSpan.FromHours(1),
    };

    private (HostJobReporter Reporter, FakeServer Server, Func<int> Stops, HttpClient Http) Make(Func<RecordedRequest, HttpResponseMessage> respond)
    {
        var server = new FakeServer().On("POST", EventsPath, respond);
        var http = new HttpClient(server, disposeHandler: false);
        var stops = 0;
        var reporter = new HostJobReporter(new ApiClient(http, "https://pm.example.com", "kha_x"), JobId, 7, _options, () => Interlocked.Increment(ref stops));
        return (reporter, server, () => stops, http);
    }

    private static HttpResponseMessage Ack(RecordedRequest request, bool cancelled = false)
    {
        var body = request.Json!;
        return FakeServer.Envelope(HttpStatusCode.OK, new { cancelled, nextSeq = body["firstSeq"]!.GetValue<int>() + body["events"]!.AsArray().Count });
    }

    [Test]
    public async Task Flush_SendsTheLeaseAndConsecutiveSequenceNumbersAcrossBatches()
    {
        // Arrange
        var (reporter, server, _, http) = Make(request => Ack(request));
        using var _ = http;

        // Act
        reporter.Emit(new TextEvent("你好"));
        reporter.Emit(new ToolEvent("c1", "get_blueprint", "running"));
        await reporter.FlushRemainingAsync(CancellationToken.None);
        reporter.Emit(new TextEvent("再見"));
        await reporter.FlushRemainingAsync(CancellationToken.None);

        // Assert
        var bodies = server.To("POST", EventsPath).Select(request => request.Json!).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bodies.Select(body => body["lease"]!.GetValue<int>()), Is.All.EqualTo(7));
            Assert.That(bodies.Select(body => body["firstSeq"]!.GetValue<int>()), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(bodies[0]["events"]!.AsArray(), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Flush_AfterAServerError_ResendsTheSameBatchUnchangedEvenIfMoreTextWasEmittedMeanwhile()
    {
        // Arrange
        var calls = 0;
        var (reporter, server, _, http) = Make(request => Interlocked.Increment(ref calls) == 1
            ? FakeAgentServerFailure()
            : Ack(request));
        using var _ = http;

        // Act
        reporter.Emit(new TextEvent("ab"));
        await reporter.FlushRemainingAsync(CancellationToken.None);
        reporter.Emit(new TextEvent("cd"));
        await reporter.FlushRemainingAsync(CancellationToken.None);

        // Assert
        var bodies = server.To("POST", EventsPath).Select(request => request.Json!).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bodies, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(bodies[1]["firstSeq"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(bodies[1]["events"]![0]!["delta"]!.GetValue<string>(), Is.EqualTo("ab"), "重送的是原本那一批，不併進新的文字");
            Assert.That(bodies[^1]["firstSeq"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(bodies[^1]["events"]![0]!["delta"]!.GetValue<string>(), Is.EqualTo("cd"));
        }
    }

    [Test]
    public async Task Flush_WhenTheServerOnlyStoredPartOfTheBatch_ResendsTheRestFromNextSeq()
    {
        // Arrange
        var calls = 0;
        var (reporter, server, _, http) = Make(request => Interlocked.Increment(ref calls) == 1
            ? FakeServer.Envelope(HttpStatusCode.OK, new { cancelled = false, nextSeq = 2 })
            : Ack(request));
        using var _ = http;

        // Act
        reporter.Emit(new ToolEvent("c1", "get_blueprint", "running"));
        reporter.Emit(new ToolEvent("c2", "get_blueprint", "running"));
        reporter.Emit(new ToolEvent("c3", "get_blueprint", "running"));
        await reporter.FlushRemainingAsync(CancellationToken.None);

        // Assert
        var bodies = server.To("POST", EventsPath).Select(request => request.Json!).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bodies, Has.Count.EqualTo(2));
            Assert.That(bodies[1]["firstSeq"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(bodies[1]["events"]!.AsArray().Select(item => item!["callId"]!.GetValue<string>()), Is.EqualTo(new[] { "c2", "c3" }));
        }
    }

    [TestCase(HttpStatusCode.Conflict, "ai.host_job_lease_lost")]
    [TestCase(HttpStatusCode.NotFound, "ai.agent_job_not_found")]
    [TestCase(HttpStatusCode.Unauthorized, "auth.required")]
    public async Task Flush_WhenTheLeaseIsLostTheJobIsUnknownOrTheCredentialIsRevoked_StopsTheEngineAndSendsNothingMore(HttpStatusCode status, string code)
    {
        // Arrange
        var (reporter, server, stops, http) = Make(_ => HostTestKit.Failure(status, code, "x"));
        using var _ = http;

        // Act
        reporter.Emit(new TextEvent("x"));
        await reporter.FlushRemainingAsync(CancellationToken.None);
        await reporter.FlushRemainingAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stops(), Is.GreaterThanOrEqualTo(1));
            Assert.That(server.To("POST", EventsPath), Has.Count.EqualTo(1), "停下之後不再重送同一批");
        }
    }

    [Test]
    public async Task Flush_WhenTheServerSaysCancelled_StopsTheEngine()
    {
        // Arrange
        var (reporter, _, stops, http) = Make(request => Ack(request, cancelled: true));
        using var _ = http;

        // Act
        reporter.Emit(new TextEvent("x"));
        await reporter.FlushRemainingAsync(CancellationToken.None);

        // Assert
        Assert.That(stops(), Is.EqualTo(1));
    }

    [Test]
    public async Task Flush_WhenABatchIsRejected_DropsItAndReusesItsSequenceNumbers()
    {
        // Arrange
        var calls = 0;
        var (reporter, server, _, http) = Make(request => Interlocked.Increment(ref calls) == 1
            ? HostTestKit.Failure(HttpStatusCode.UnprocessableEntity, "ai.host_agent_invalid", "內容不對")
            : Ack(request));
        using var _ = http;

        // Act
        reporter.Emit(new TextEvent("壞的"));
        await reporter.FlushRemainingAsync(CancellationToken.None);
        reporter.Emit(new TextEvent("好的"));
        await reporter.FlushRemainingAsync(CancellationToken.None);

        // Assert
        var bodies = server.To("POST", EventsPath).Select(request => request.Json!).ToList();
        Assert.That(bodies.Select(body => body["firstSeq"]!.GetValue<int>()), Is.All.EqualTo(1), "被拒絕的不算收過，後面的流水號接在同一個位置");
    }

    [Test]
    public async Task Pump_SendsAnEmptyHeartbeatBatchWithTheNextSequenceNumber()
    {
        // Arrange
        var (reporter, server, _, http) = Make(request => Ack(request));
        using var _ = http;
        reporter.Emit(new TextEvent("a"));
        await reporter.FlushRemainingAsync(CancellationToken.None);
        var options = new AgentOptions { FlushInterval = TimeSpan.FromMilliseconds(10), HeartbeatInterval = TimeSpan.Zero };
        var beating = new HostJobReporter(new ApiClient(http, "https://pm.example.com", "kha_x"), JobId, 7, options, () => { });
        using var stop = new CancellationTokenSource();

        // Act
        var pump = beating.PumpAsync(stop.Token);
        while (server.To("POST", EventsPath).Count < 2)
        {
            await Task.Delay(10);
        }

        await stop.CancelAsync();
        await pump;

        // Assert
        var heartbeat = server.To("POST", EventsPath)[1].Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(heartbeat["events"]!.AsArray(), Is.Empty);
            Assert.That(heartbeat["firstSeq"]!.GetValue<int>(), Is.EqualTo(1));
        }
    }

    private static HttpResponseMessage FakeAgentServerFailure() => HostTestKit.Failure(HttpStatusCode.InternalServerError, "server.error", "出錯了");

    [Test]
    public async Task Flush_WhenBatchesKeepBeingRejected_StopsTheEngineAfterThreeInARow()
    {
        // Arrange
        var (reporter, _, stops, http) = Make(_ => HostTestKit.Failure(HttpStatusCode.UnprocessableEntity, "ai.host_agent_invalid", "流水號對不上"));
        using var _ = http;

        // Act
        for (var i = 0; i < 3; i++)
        {
            reporter.Emit(new TextEvent($"第 {i} 批"));
            await reporter.FlushRemainingAsync(CancellationToken.None);
        }

        // Assert
        Assert.That(stops(), Is.EqualTo(1));
    }
}
