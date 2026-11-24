using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Endpoints;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>送訊息：事件串流的格式、逐段送出（不緩衝）、心跳、中斷時取消、送出之前的失敗、請求內文與限流。</summary>
[TestFixture]
public sealed class AssistantStreamTests
{
    private static readonly string MessagesRoute = $"{AssistantEndpointTests.Item}/messages";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static object Body(string text = "把驗收拆成內部驗收和客戶驗收", string effort = "high") => new
    {
        text,
        selection = new { connectionId = FakeAssistantService.ConnectionId, model = "claude-opus-5-5", effort },
    };

    [Test]
    public async Task Send_Success_IsAnEventStreamWithTheHeadersAndEventsInOrder()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await SendAsync(client, Body());
        var frames = await new SseReader(response).ReadToEndAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/event-stream; charset=utf-8"));
            Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
            Assert.That(response.Headers.GetValues("X-Accel-Buffering"), Is.EqualTo(new[] { "no" }));
            Assert.That(frames.Select(f => f.Event), Is.EqualTo(new[] { "start", "text", "tool", "tool", "done" }));
        }
    }

    [Test]
    public async Task Send_Events_CarryTheContractShapes()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, Body());
        var frames = await new SseReader(response).ReadToEndAsync();

        // Assert
        var assistantId = "33333333-3333-3333-3333-333333333332";
        using (Assert.EnterMultipleScope())
        {
            AssertJson(frames[0].Data, $$"""
                {"userMessage":{"id":"33333333-3333-3333-3333-333333333331","role":"user","createdAt":"2026-11-23T03:00:00Z",
                  "parts":[{"type":"text","text":"把驗收拆成內部驗收和客戶驗收"}],
                  "status":null,"error":null,"selection":null,"connectionName":null,"usage":null,"itemKey":null},
                 "assistantMessageId":"{{assistantId}}"}
                """);
            AssertJson(frames[1].Data, """{"delta":"好，我先讀目前的設定。"}""");
            AssertJson(frames[2].Data, """{"type":"tool","callId":"c1","name":"propose_changeset","status":"running","changeSetNumber":null,"error":null,"itemProposalId":null}""");
            AssertJson(frames[3].Data, """{"type":"tool","callId":"c1","name":"propose_changeset","status":"done","changeSetNumber":12,"error":null,"itemProposalId":null}""");
            var done = JsonNode.Parse(frames[4].Data!);
            Assert.That(done?["message"]?["id"]?.GetValue<string>(), Is.EqualTo(assistantId));
            Assert.That(done?["message"]?["status"]?.GetValue<string>(), Is.EqualTo("complete"));
            Assert.That(done?["message"]?["parts"]?[1]?["changeSetNumber"]?.GetValue<long>(), Is.EqualTo(12));
        }
    }

    [Test]
    public async Task Send_HostedSource_QueuedAndStartedEventsAreWrittenInOrderBeforeTheText()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnStart = _ => new AssistantRun(
            FakeAssistantService.UserMessage(),
            Guid.NewGuid(),
            Events(
                new AssistantRunQueued(3),
                new AssistantRunQueued(1),
                new AssistantRunStarted(),
                new AssistantTextDelta("開始"),
                new AssistantRunCompleted(FakeAssistantService.AssistantReply())));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, Body());
        var frames = await new SseReader(response).ReadToEndAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(frames.Select(f => f.Event), Is.EqualTo(new[] { "start", "queued", "queued", "started", "text", "done" }));
            AssertJson(frames[1].Data, """{"position":3}""");
            AssertJson(frames[2].Data, """{"position":1}""");
            AssertJson(frames[3].Data, "{}");
        }
    }

    [Test]
    public async Task Send_EveryEvent_IsOneEventLineOneDataLineAndABlankLine()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnStart = _ => new AssistantRun(
            FakeAssistantService.UserMessage(),
            Guid.NewGuid(),
            Events(new AssistantTextDelta("第一行\n第二行 \"引號\""), new AssistantRunCompleted(FakeAssistantService.AssistantReply())));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, Body());
        var raw = await response.Content.ReadAsStringAsync();

        // Assert：資料裡的換行是 JSON 的跳脫，不會把一個事件切成兩行 data。
        var blocks = raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw, Does.EndWith("\n\n"));
            Assert.That(blocks, Has.Length.EqualTo(3));
            foreach (var block in blocks)
            {
                var lines = block.Split('\n');
                Assert.That(lines, Has.Length.EqualTo(2), block);
                Assert.That(lines[0], Does.StartWith("event: "));
                Assert.That(lines[1], Does.StartWith("data: {"));
            }

            Assert.That(blocks[1], Does.Contain("第一行\\n第二行 \\\"引號\\\""));
        }
    }

    [Test]
    public async Task Send_Events_ArriveOneByOneWhileTheReplyIsStillRunning()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var run = new ControlledRun();
        factory.Modules.Assistant.OnStart = _ => run.ToRun();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var response = await SendAsync(client, Body());
        var reader = new SseReader(response);

        // Act
        var start = await reader.ReadFrameAsync(Patience);
        run.Send(new AssistantTextDelta("第一段"));
        var first = await reader.ReadFrameAsync(Patience);
        run.Send(new AssistantTextDelta("第二段"));
        var second = await reader.ReadFrameAsync(Patience);
        run.Finish();
        var done = await reader.ReadFrameAsync(Patience);
        var end = await reader.ReadFrameAsync(Patience);

        // Assert：每一段在結束之前就讀得到，不是最後一次拿到。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(start?.Event, Is.EqualTo("start"));
            Assert.That(first?.Event, Is.EqualTo("text"));
            Assert.That(JsonNode.Parse(first!.Data!)?["delta"]?.GetValue<string>(), Is.EqualTo("第一段"));
            Assert.That(JsonNode.Parse(second!.Data!)?["delta"]?.GetValue<string>(), Is.EqualTo("第二段"));
            Assert.That(done?.Event, Is.EqualTo("done"));
            Assert.That(end, Is.Null);
        }
    }

    [Test]
    public async Task Send_QuietReply_GetsAPingCommentEveryInterval()
    {
        // Arrange
        var run = new ControlledRun();
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions
            {
                ConfigureServices = services => services.AddSingleton(new AssistantStreamSettings(TimeSpan.FromMilliseconds(100))),
            });
        factory.Modules.Assistant.OnStart = _ => run.ToRun();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var response = await SendAsync(client, Body());
        var reader = new SseReader(response);
        await reader.ReadFrameAsync(Patience);

        // Act
        var firstPing = await reader.ReadFrameAsync(Patience);
        var secondPing = await reader.ReadFrameAsync(Patience);
        run.Send(new AssistantTextDelta("還在"));
        var text = await reader.ReadFrameAsync(Patience);
        run.Finish();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPing?.Comment, Is.EqualTo("ping"));
            Assert.That(secondPing?.Comment, Is.EqualTo("ping"));
            Assert.That(text?.Event, Is.EqualTo("text"));
        }
    }

    [Test]
    public async Task Send_ClientLeavesMidReply_CancelsTheReadTokenAndDisposesTheEvents()
    {
        // Arrange：TestServer 不會把「用戶端關掉連線」轉成 RequestAborted，所以換成測試自己控制的 token，取消它就等於用戶端離開。
        var request = new CapturedRequest();
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { ConfigureServices = services => services.AddSingleton<IStartupFilter>(new CaptureRequestFilter(request)) });
        var run = new ControlledRun();
        factory.Modules.Assistant.OnStart = _ => run.ToRun();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var response = await SendAsync(client, Body());
        var reader = new SseReader(response);
        await reader.ReadFrameAsync(Patience);
        run.Send(new AssistantTextDelta("寫到一半"));
        await reader.ReadFrameAsync(Patience);

        // Act
        await request.AbortAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Task.WhenAny(run.Cancelled, Task.Delay(Patience)), Is.SameAs(run.Cancelled), "讀的 token 沒有被取消");
            Assert.That(await Task.WhenAny(run.Disposed, Task.Delay(Patience)), Is.SameAs(run.Disposed), "事件沒有被 Dispose");
        }
    }

    [Test]
    public async Task Send_RequestBody_IsPassedAsTheMessageWithItsSelection()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await SendAsync(client, Body("你好", effort: "xhigh"));
        await response.Content.ReadAsStringAsync();

        // Assert
        var call = factory.Modules.Assistant.StartCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(call.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(call.ConversationId, Is.EqualTo(FakeAssistantService.ConversationId));
            Assert.That(call.Input.Text, Is.EqualTo("你好"));
            Assert.That(call.Input.Selection, Is.EqualTo(new AiSelection(FakeAssistantService.ConnectionId, "claude-opus-5-5", AiEffort.Xhigh)));
        }
    }

    [Test]
    public async Task Send_ItemKey_IsPassedToTheModuleAndOmittedMeansNone()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var selection = new { connectionId = FakeAssistantService.ConnectionId, model = "claude-opus-5-5", effort = "high" };

        // Act
        using var withItem = await SendAsync(client, new { text = "整理重點", selection, itemKey = "WEB-12" });
        await withItem.Content.ReadAsStringAsync();
        using var withoutItem = await SendAsync(client, Body());
        await withoutItem.Content.ReadAsStringAsync();

        // Assert
        var calls = factory.Modules.Assistant.StartCalls;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls[0].Input.ItemKey, Is.EqualTo("WEB-12"));
            Assert.That(calls[1].Input.ItemKey, Is.Null);
        }
    }

    [Test]
    public async Task Send_ItemNotFound_Is404WithTheItemCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnStart = _ => new Failure(FailureKind.NotFound, AiErrorCodes.ItemNotFound, "找不到項目。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, Body());

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, AiErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task Send_UserMessageAndProposalToolPart_CarryItemKeyAndItemProposalId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var proposalId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var tool = new AssistantToolPart("c1", "propose_item_changes", AssistantToolStatus.Done, null, null) { ItemProposalId = proposalId };
        factory.Modules.Assistant.OnStart = _ => new AssistantRun(
            FakeAssistantService.UserMessage() with { ItemKey = "WEB-12" },
            Guid.Parse("33333333-3333-3333-3333-333333333332"),
            Events(
                new AssistantToolUpdate(tool),
                new AssistantRunCompleted(FakeAssistantService.AssistantReply() with { Parts = [tool] })));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, Body());
        var frames = await new SseReader(response).ReadToEndAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.Parse(frames[0].Data!)?["userMessage"]?["itemKey"]?.GetValue<string>(), Is.EqualTo("WEB-12"));
            Assert.That(JsonNode.Parse(frames[1].Data!)?["itemProposalId"]?.GetValue<Guid>(), Is.EqualTo(proposalId));
            Assert.That(JsonNode.Parse(frames[2].Data!)?["message"]?["parts"]?[0]?["itemProposalId"]?.GetValue<Guid>(), Is.EqualTo(proposalId));
        }
    }

    [TestCase("""{"text":"a","selection":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m"}}""")]
    [TestCase("""{"text":"a","selection":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m","effort":null}}""")]
    public async Task Send_EffortOmittedOrNull_MeansTheModelsDefault(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(MessagesRoute, ApiRequests.Document(body));
        await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(factory.Modules.Assistant.StartCalls.Single().Input.Selection.Effort, Is.Null);
    }

    [TestCase("""{"selection":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m"}}""")]
    [TestCase("""{"text":"a"}""")]
    [TestCase("""{"text":"a","selection":null}""")]
    [TestCase("""{"text":"a","selection":{"connectionId":"x","model":"m"}}""")]
    [TestCase("""{"text":"a","selection":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m","effort":"loud"}}""")]
    [TestCase("""{"text":"a","selection":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m"},"extra":1}""")]
    [TestCase("""{"text":5,"selection":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m"}}""")]
    [TestCase("not json")]
    [TestCase("")]
    public async Task Send_BadBody_Is400AndNeverReachesTheModule(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(MessagesRoute, ApiRequests.Document(body));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.Assistant.StartCalls, Is.Empty);
    }

    [Test]
    public async Task Send_NotJsonMediaType_IsRejectedBeforeTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(MessagesRoute, ApiRequests.Document("{}", "text/plain"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
        Assert.That(factory.Modules.Assistant.StartCalls, Is.Empty);
    }

    [TestCase(FailureKind.Forbidden, AiErrorCodes.AssistantForbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, AiErrorCodes.ConversationNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.ConversationBusy, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.TooManyRuns, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.ConversationTooLong, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Invalid, AiErrorCodes.MessageInvalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.Invalid, AiErrorCodes.SelectionInvalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.Invalid, AiErrorCodes.Unavailable, HttpStatusCode.UnprocessableEntity)]
    public async Task Send_FailureBeforeTheReplyStarts_IsAPlainJsonErrorNotAStream(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnStart = _ => new Failure(kind, code, "原因說明");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, Body());

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Send_MoreThanTenMessagesAMinute_Returns429ForThatPersonOnly()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var member = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var owner = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        for (var i = 0; i < ApiLimits.AssistantMessagePermitsPerWindow; i++)
        {
            using var allowed = await SendAsync(member, Body());
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await allowed.Content.ReadAsStringAsync();
        }

        // Act
        using var tooMany = await SendAsync(member, Body());
        using var otherPerson = await SendAsync(owner, Body());
        using var reading = await member.GetAsync(AssistantEndpointTests.Collection);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(tooMany, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(otherPerson.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(reading.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Modules.Assistant.StartCalls, Has.Count.EqualTo(ApiLimits.AssistantMessagePermitsPerWindow + 1));
        }
    }

    // 不等整個內文：串流要讀到標頭就開始看。
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, object body, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MessagesRoute) { Content = ApiRequests.Json(body) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static void AssertJson(string? actual, string expected) =>
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(actual!), JsonNode.Parse(expected)), Is.True, actual);

    private static async IAsyncEnumerable<AssistantRunEvent> Events(params AssistantRunEvent[] events)
    {
        foreach (var runEvent in events)
        {
            yield return runEvent;
            await Task.Yield();
        }
    }

    /// <summary>換掉請求的 <c>RequestAborted</c>，測試才能在需要的時候讓它取消。</summary>
    private sealed class CapturedRequest
    {
        private readonly CancellationTokenSource _aborted = new();

        public CancellationToken Token => _aborted.Token;

        public Task AbortAsync() => _aborted.CancelAsync();
    }

    private sealed class CaptureRequestFilter(CapturedRequest captured) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, pipeline) =>
            {
                if (context.Request.Path.Value?.EndsWith("/messages", StringComparison.Ordinal) == true)
                {
                    context.RequestAborted = captured.Token;
                }

                return pipeline();
            });
            next(app);
        };
    }

    internal sealed record SseFrame(string? Event, string? Data, string? Comment);

    /// <summary>一段一段讀事件串流，每一段有期限：讀不到就是伺服器沒有送出來（被緩衝了）。</summary>
    internal sealed class SseReader(HttpResponseMessage response)
    {
        private StreamReader? _reader;

        /// <summary>下一個事件或註解；串流結束是 null。</summary>
        public async Task<SseFrame?> ReadFrameAsync(TimeSpan timeout)
        {
            _reader ??= new StreamReader(await response.Content.ReadAsStreamAsync());
            using var cancellation = new CancellationTokenSource(timeout);
            string? eventName = null;
            string? data = null;
            string? comment = null;
            while (true)
            {
                var line = await _reader.ReadLineAsync(cancellation.Token);
                if (line is null)
                {
                    return null;
                }

                if (line.Length == 0)
                {
                    return new SseFrame(eventName, data, comment);
                }

                if (line.StartsWith(": ", StringComparison.Ordinal))
                {
                    comment = line[2..];
                }
                else if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    eventName = line["event: ".Length..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line["data: ".Length..];
                }
            }
        }

        public async Task<IReadOnlyList<SseFrame>> ReadToEndAsync()
        {
            var frames = new List<SseFrame>();
            while (await ReadFrameAsync(Patience) is { } frame)
            {
                frames.Add(frame);
            }

            return frames;
        }
    }
}
