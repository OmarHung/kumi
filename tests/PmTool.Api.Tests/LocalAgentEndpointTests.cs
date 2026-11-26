using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 本機代理的端點（/v1/agent/*）與「我的帳號」的電腦清單：只收 agent:run 權杖（Cookie 回 403）、資料的形狀、
/// 請求怎麼交給模組、失敗代碼、內文大小、限流、長輪詢在呼叫端斷線時結束。
/// </summary>
[TestFixture]
public sealed class LocalAgentEndpointTests
{
    private const string CheckInRoute = "/v1/agent/check-in";
    private const string AccountRoute = "/v1/account/local-agents";

    private static string EventsRoute => $"/v1/agent/jobs/{FakeLocalAgentService.JobId}/events";

    private static string AttachmentRoute(Guid attachmentId) => $"/v1/agent/jobs/{FakeLocalAgentService.JobId}/attachments/{attachmentId}";

    private static string FinishRoute => $"/v1/agent/jobs/{FakeLocalAgentService.JobId}/finish";

    private static object CheckInBody(int capacity = 1, string machineKey = "machine-0001") => new
    {
        machineKey,
        machineName = "辦公室的 Mac",
        agentVersion = "0.9.0",
        engines = new[]
        {
            new
            {
                kind = "claude_code",
                version = "2.1.330",
                models = new[]
                {
                    new { id = "default", displayName = "預設", efforts = Array.Empty<string>(), defaultEffort = (string?)null },
                    new { id = "sonnet", displayName = "Sonnet", efforts = new[] { "low", "high" }, defaultEffort = (string?)"high" },
                },
            },
        },
        capacity,
    };

    private static HttpRequestMessage Post(string route, string secret, object body)
    {
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, route, secret);
        request.Content = ApiRequests.Json(body);
        return request;
    }

    private static string AgentToken(ApiFactory factory, params AccessTokenScope[] scopes) =>
        factory.Modules.AccessTokens.Issue(TestUsers.Member, scopes.Length == 0 ? [AccessTokenScope.AgentRun] : scopes);

    [Test]
    public async Task CheckIn_WithAnAgentRunToken_PassesThePersonAndTheContentToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, AgentToken(factory), CheckInBody(capacity: 2)));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var call = factory.Modules.LocalAgents.CheckInCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["job"], Is.Null);
            Assert.That(data?.AsObject().ContainsKey("job"), Is.True, "job 要寫出 null");
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(call.Capacity, Is.EqualTo(2));
            Assert.That(call.CheckIn.MachineKey, Is.EqualTo("machine-0001"));
            Assert.That(call.CheckIn.MachineName, Is.EqualTo("辦公室的 Mac"));
            Assert.That(call.CheckIn.AgentVersion, Is.EqualTo("0.9.0"));
            var engine = call.CheckIn.Engines.Single();
            Assert.That(engine.Kind, Is.EqualTo(AiProviderKind.ClaudeCode));
            Assert.That(engine.Version, Is.EqualTo("2.1.330"));
            Assert.That(engine.Models.Select(m => (m.Id, m.DisplayName)), Is.EqualTo(new[] { ("default", "預設"), ("sonnet", "Sonnet") }));
            Assert.That(engine.Models[1].Efforts, Is.EqualTo(new[] { AiEffort.Low, AiEffort.High }));
            Assert.That(engine.Models[1].DefaultEffort, Is.EqualTo(AiEffort.High));
            Assert.That(engine.Models[0].DefaultEffort, Is.Null);
        }
    }

    [Test]
    public async Task CheckIn_ModuleHandsBackAJob_ReturnsItInTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.LocalAgents.OnCheckIn = (_, _, _, _) =>
            Task.FromResult<Result<LocalAgentJob?>>(FakeLocalAgentService.Job());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, AgentToken(factory), CheckInBody()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        const string expected = """
            {"job":{"jobId":"44444444-4444-4444-4444-444444444441","engine":"claude_code","model":"sonnet","effort":"high",
            "projectKey":"WEB","systemPrompt":"系統提示","prompt":"使用者：嗨","allowedTools":["get_blueprint","propose_changeset"],
            "mcpUrl":"https://kumi.example.com/mcp","maxTurns":13,"attachments":[]}}
            """;
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(expected)), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task CheckIn_JobWithAttachments_ReturnsThemWithTheAgreedFieldNames()
    {
        // Arrange
        var image = new LocalAgentJobAttachment(
            Guid.Parse("66666666-6666-6666-6666-666666666661"), "截圖.png", "image/png", AssistantAttachmentKind.Image, 1234, new string('a', 64));
        var pdf = image with { Id = Guid.Parse("66666666-6666-6666-6666-666666666662"), FileName = "規格.pdf", ContentType = "application/pdf", Kind = AssistantAttachmentKind.Pdf };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.LocalAgents.OnCheckIn = (_, _, _, _) =>
            Task.FromResult<Result<LocalAgentJob?>>(FakeLocalAgentService.Job() with { Attachments = [image, pdf] });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, AgentToken(factory), CheckInBody()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse($$"""
            [{"id":"66666666-6666-6666-6666-666666666661","fileName":"截圖.png","contentType":"image/png","kind":"image","sizeBytes":1234,"sha256":"{{new string('a', 64)}}"},
             {"id":"66666666-6666-6666-6666-666666666662","fileName":"規格.pdf","contentType":"application/pdf","kind":"pdf","sizeBytes":1234,"sha256":"{{new string('a', 64)}}"}]
            """);
        Assert.That(JsonNode.DeepEquals(data?["job"]?["attachments"], expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task DownloadAttachment_Granted_StreamsTheBytesAsAnOctetStreamWithTheHash()
    {
        // Arrange
        var attachmentId = Guid.Parse("66666666-6666-6666-6666-666666666661");
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.LocalAgents.OnOpenAttachment = (_, _, _) => new AssistantAttachmentContent(
            new AssistantAttachment(attachmentId, "截圖.png", "image/png", bytes.Length, AssistantAttachmentKind.Image, DateTimeOffset.UnixEpoch),
            "abc123",
            new MemoryStream(bytes));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, AttachmentRoute(attachmentId), AgentToken(factory)));
        var body = await response.Content.ReadAsByteArrayAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(response.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("attachment"));
            Assert.That(response.Headers.GetValues("X-Attachment-Sha256").Single(), Is.EqualTo("abc123"));
            Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
            Assert.That(body, Is.EqualTo(bytes));
            Assert.That(factory.Modules.LocalAgents.OpenAttachmentCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), FakeLocalAgentService.JobId, attachmentId)));
        }
    }

    [Test]
    public async Task DownloadAttachment_ModuleSaysNotFound_Returns404WithTheCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(
            AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, AttachmentRoute(Guid.NewGuid()), AgentToken(factory)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await response.Content.ReadAsStringAsync()), Does.Contain("ai.attachment_not_found"));
        }
    }

    [Test]
    public async Task DownloadAttachment_WithTheWebSessionCookie_Returns403AndNeverReachesTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(AttachmentRoute(Guid.NewGuid()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        Assert.That(factory.Modules.LocalAgents.OpenAttachmentCalls, Is.Empty);
    }

    [Test]
    public async Task CheckIn_JobWithoutEffort_WritesNull()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.LocalAgents.OnCheckIn = (_, _, _, _) =>
            Task.FromResult<Result<LocalAgentJob?>>(FakeLocalAgentService.Job() with { Effort = null });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, AgentToken(factory), CheckInBody()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["job"]?.AsObject().ContainsKey("effort") == true && data["job"]!["effort"] is null, Is.True, data?.ToJsonString());
    }

    [TestCase(CheckInRoute, TestName = "登記")]
    [TestCase("/v1/agent/jobs/44444444-4444-4444-4444-444444444441/events", TestName = "回報")]
    [TestCase("/v1/agent/jobs/44444444-4444-4444-4444-444444444441/finish", TestName = "結束")]
    public async Task AgentEndpoint_WithTheWebSessionCookie_Returns403AndNeverReachesTheModule(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(route, ApiRequests.Json(route == CheckInRoute ? CheckInBody() : new { events = Array.Empty<object>(), status = "complete" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.LocalAgents.CheckInCalls, Is.Empty);
            Assert.That(factory.Modules.LocalAgents.ReportCalls, Is.Empty);
            Assert.That(factory.Modules.LocalAgents.FinishCalls, Is.Empty);
        }
    }

    [Test]
    public async Task AgentEndpoint_WithoutLoginOrWithOtherScopesOnly_IsRejectedBeforeTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var other = factory.Modules.AccessTokens.Issue(
            TestUsers.Member, AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite);

        // Act
        var anonymousRequest = new HttpRequestMessage(HttpMethod.Post, CheckInRoute) { Content = ApiRequests.Json(CheckInBody()) };
        anonymousRequest.Headers.Add(ApiRequests.CsrfHeader, "1");
        using var anonymous = await client.SendAsync(anonymousRequest);
        using var otherScopes = await client.SendAsync(Post(CheckInRoute, other, CheckInBody()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(otherScopes.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(factory.Modules.LocalAgents.CheckInCalls, Is.Empty);
        }
    }

    [Test]
    public async Task AgentEndpoint_TokenFromAnOAuthGrant_CannotBeUsedHereEither()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var secret = factory.Modules.AccessTokens.IssueForGrant(TestUsers.Member, Guid.NewGuid(), AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite);

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, secret, CheckInBody()));

        // Assert
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.LocalAgents.CheckInCalls, Is.Empty);
    }

    [TestCase(-1)]
    [TestCase(17)]
    public async Task CheckIn_CapacityOutOfRange_Returns422AgentInvalidWithThePath(int capacity)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, AgentToken(factory), CheckInBody(capacity)));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AgentInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{AiErrorCodes.FieldInvalid}|capacity" }));
            Assert.That(factory.Modules.LocalAgents.CheckInCalls, Is.Empty);
        }
    }

    [TestCase("""[null]""", "engines[0]", TestName = "引擎的元素是 null")]
    [TestCase("""[{"kind":"claude_code","version":"1","models":[null]}]""", "engines[0].models[0]", TestName = "模型的元素是 null")]
    [TestCase("""[{"kind":"claude_code","version":"1","models":[{"id":"a","displayName":"A","efforts":[null]}]}]""", "engines[0].models[0].efforts[0]", TestName = "思考深度的元素是 null")]
    public async Task CheckIn_ANullArrayElement_Returns422AgentInvalidWithThePathAndNothingReachesTheModule(string engines, string path)
    {
        // Arrange：元素是 null 時轉換會丟 NullReferenceException，回 500；要回和其他欄位一樣的 422。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, CheckInRoute, AgentToken(factory));
        request.Content = ApiRequests.Document($$"""{"machineKey":"machine-0001","machineName":"a","agentVersion":"1","engines":{{engines}},"capacity":1}""");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AgentInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{AiErrorCodes.FieldInvalid}|{path}" }));
            Assert.That(factory.Modules.LocalAgents.CheckInCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Report_ANullEvent_Returns422AgentInvalidWithTheIndexAndNothingReachesTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, EventsRoute, AgentToken(factory));
        request.Content = ApiRequests.Document("""{"events":[{"type":"text","delta":"好"},null]}""");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AgentInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{AiErrorCodes.FieldInvalid}|events[1]" }));
            Assert.That(factory.Modules.LocalAgents.ReportCalls, Is.Empty);
        }
    }

    [TestCase("""{"machineKey":"machine-0001"}""", TestName = "少了欄位")]
    [TestCase("""{"machineKey":"machine-0001","machineName":"a","agentVersion":"1","engines":[{"kind":"gemini","version":"1","models":[]}],"capacity":1}""", TestName = "引擎種類不認得")]
    [TestCase("""{"machineKey":"machine-0001","machineName":"a","agentVersion":"1","engines":[],"capacity":1,"extra":1}""", TestName = "多出來的屬性")]
    [TestCase("""{"machineKey":"machine-0001","machineName":"a","agentVersion":"1","engines":null,"capacity":1}""", TestName = "引擎是 null")]
    [TestCase("not json", TestName = "不是 JSON")]
    public async Task CheckIn_MalformedBody_Returns400WithoutCallingTheModule(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, CheckInRoute, AgentToken(factory));
        request.Content = ApiRequests.Document(body);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(factory.Modules.LocalAgents.CheckInCalls, Is.Empty);
        }
    }

    [Test]
    public async Task CheckIn_ModuleFailures_MapToTheirStatusAndCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var token = AgentToken(factory);
        var responses = new List<(HttpStatusCode Status, string Code)>();

        // Act
        foreach (var failure in new[]
        {
            new Failure(FailureKind.Forbidden, AiErrorCodes.PersonalDisabled, "組織目前不允許使用自己的 AI 帳號。"),
            new Failure(FailureKind.Invalid, AiErrorCodes.AgentInvalid, "電腦名稱不對。"),
        })
        {
            factory.Modules.LocalAgents.OnCheckIn = (_, _, _, _) => Task.FromResult<Result<LocalAgentJob?>>(failure);
            using var response = await client.SendAsync(Post(CheckInRoute, token, CheckInBody()));
            var envelope = await ApiRequests.ReadEnvelopeAsync(response);
            responses.Add((response.StatusCode, envelope["error"]!["code"]!.GetValue<string>()));
        }

        // Assert
        Assert.That(
            responses,
            Is.EqualTo(new[] { (HttpStatusCode.Forbidden, AiErrorCodes.PersonalDisabled), (HttpStatusCode.UnprocessableEntity, AiErrorCodes.AgentInvalid) }));
    }

    [Test]
    public async Task CheckIn_CallerDisconnectsWhileWaiting_TheModuleSeesTheCancellationAtOnce()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var seen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Modules.LocalAgents.OnCheckIn = async (_, _, _, token) =>
        {
            waiting.SetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                seen.TrySetResult(false);
            }
            catch (OperationCanceledException)
            {
                seen.TrySetResult(true);
            }

            return (LocalAgentJob?)null;
        };
        using var client = factory.CreateClient();
        using var cancel = new CancellationTokenSource();

        // Act
        var pending = client.SendAsync(Post(CheckInRoute, AgentToken(factory), CheckInBody()), cancel.Token);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancel.CancelAsync();

        // Assert
        Assert.That(await seen.Task.WaitAsync(TimeSpan.FromSeconds(10)), Is.True, "呼叫端斷線，模組的長輪詢要立刻結束");
        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Report_TextAndToolEvents_AreConvertedInOrderAndTheAckIsReturned()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.LocalAgents.OnReport = (_, _, _) => new LocalAgentReportAck(true);
        using var client = factory.CreateClient();
        var body = new
        {
            events = new object[]
            {
                new { type = "text", delta = "先讀設定" },
                new { callId = "toolu_1", type = "tool", name = "propose_changeset", status = "done", changeSetNumber = 12, error = (string?)null },
                new { type = "tool", callId = "toolu_2", name = "get_blueprint", status = "failed", error = "沒有權限" },
            },
        };

        // Act
        using var response = await client.SendAsync(Post(EventsRoute, AgentToken(factory), body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var call = factory.Modules.LocalAgents.ReportCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"cancelled":true}""")), Is.True, data?.ToJsonString());
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(call.JobId, Is.EqualTo(FakeLocalAgentService.JobId));
            Assert.That(
                call.Events,
                Is.EqualTo(new LocalAgentEvent[]
                {
                    new LocalAgentText("先讀設定"),
                    new LocalAgentTool(new AssistantToolPart("toolu_1", "propose_changeset", AssistantToolStatus.Done, 12, null)),
                    new LocalAgentTool(new AssistantToolPart("toolu_2", "get_blueprint", AssistantToolStatus.Failed, null, "沒有權限")),
                }));
        }
    }

    [Test]
    public async Task Report_ToolEventWithItemProposalId_IsPassedToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var proposalId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        using var client = factory.CreateClient();
        var body = new
        {
            events = new object[] { new { type = "tool", callId = "toolu_1", name = "propose_item_changes", status = "done", itemProposalId = proposalId } },
        };

        // Act
        using var response = await client.SendAsync(Post(EventsRoute, AgentToken(factory), body));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(
            factory.Modules.LocalAgents.ReportCalls.Single().Events.Single(),
            Is.EqualTo(new LocalAgentTool(new AssistantToolPart("toolu_1", "propose_item_changes", AssistantToolStatus.Done, null, null) { ItemProposalId = proposalId })));
    }

    [TestCase("\"turbo\"")]
    [TestCase("null")]
    public async Task CheckIn_UnknownDefaultEffort_IsTreatedAsNoneNotAnError(string json)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var body = $$"""{"machineKey":"machine-0001","machineName":"電腦","agentVersion":"0.9.0","capacity":0,"engines":[{"kind":"claude_code","version":"2.1.330","models":[{"id":"sonnet","displayName":"Sonnet","efforts":["low","high"],"defaultEffort":{{json}}}]}]}""";
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, CheckInRoute, AgentToken(factory));
        request.Content = ApiRequests.Document(body);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Modules.LocalAgents.CheckInCalls.Single().CheckIn.Engines.Single().Models.Single().DefaultEffort, Is.Null);
        }
    }

    [Test]
    public async Task Report_NoEvents_IsAHeartbeat()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(EventsRoute, AgentToken(factory), new { events = Array.Empty<object>() }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["cancelled"]?.GetValue<bool>(), Is.False);
            Assert.That(factory.Modules.LocalAgents.ReportCalls.Single().Events, Is.Empty);
        }
    }

    [TestCase("""{"type":"image","delta":"x"}""", TestName = "進度種類不認得")]
    [TestCase("""{"type":"text"}""", TestName = "text 沒有 delta")]
    [TestCase("""{"type":"text","delta":"x","callId":"c"}""", TestName = "text 夾帶工具的欄位")]
    [TestCase("""{"type":"text","delta":"x","itemProposalId":"44444444-4444-4444-4444-444444444444"}""", TestName = "text 夾帶 itemProposalId")]
    [TestCase("""{"type":"tool","callId":"c","name":"get_blueprint"}""", TestName = "tool 沒有 status")]
    [TestCase("""{"type":"tool","name":"get_blueprint","status":"done"}""", TestName = "tool 沒有 callId")]
    [TestCase("""{"type":"tool","callId":"c","name":"get_blueprint","status":"done","delta":"x"}""", TestName = "tool 夾帶 delta")]
    public async Task Report_EventDoesNotMatchItsType_Returns422AgentInvalidWithTheIndexAndNothingReachesTheModule(string eventJson)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, EventsRoute, AgentToken(factory));
        request.Content = ApiRequests.Document($$"""{"events":[{"type":"text","delta":"好"},{{eventJson}}]}""");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AgentInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{AiErrorCodes.FieldInvalid}|events[1]" }));
            Assert.That(factory.Modules.LocalAgents.ReportCalls, Is.Empty);
        }
    }

    [TestCase("""{"events":[{"type":"tool","callId":"c","name":"x","status":"paused"}]}""", TestName = "工具狀態不認得")]
    [TestCase("""{"events":[{"type":"text","delta":"x","unknown":1}]}""", TestName = "多出來的屬性")]
    [TestCase("""{"events":null}""", TestName = "events 是 null")]
    [TestCase("""{}""", TestName = "沒有 events")]
    public async Task Report_MalformedBody_Returns400(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, EventsRoute, AgentToken(factory));
        request.Content = ApiRequests.Document(body);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(factory.Modules.LocalAgents.ReportCalls, Is.Empty);
    }

    [Test]
    public async Task Report_JobNotFound_Returns404WithTheAgentCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.LocalAgents.OnReport = (_, _, _) =>
            new Failure(FailureKind.NotFound, AiErrorCodes.AgentJobNotFound, "找不到這件工作，或它已經結束。");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(EventsRoute, AgentToken(factory), new { events = Array.Empty<object>() }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, AiErrorCodes.AgentJobNotFound);
    }

    [Test]
    public async Task Report_BodyOverOneMegabyte_Returns413WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, EventsRoute, AgentToken(factory));
        request.Content = ApiRequests.Document($$"""{"events":[{"type":"text","delta":"{{new string('x', 1024 * 1024)}}"}]}""");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
            Assert.That(factory.Modules.LocalAgents.ReportCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Report_BodyJustOverTheDefaultLimit_IsStillAccepted()
    {
        // Arrange：一般端點的上限是 512 KB，本機代理一批進度最多 200 則 x 8000 字，要放得下。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var events = Enumerable.Range(0, 80).Select(_ => new { type = "text", delta = new string('x', AiRules.MaxLocalAgentTextDelta) }).ToArray();

        // Act
        using var response = await client.SendAsync(Post(EventsRoute, AgentToken(factory), new { events }));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Finish_Returns204AndPassesStatusErrorAndUsage()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var body = new { status = "failed", error = "沒有登入", usage = new { inputTokens = 1200, outputTokens = 80 } };

        // Act
        using var response = await client.SendAsync(Post(FinishRoute, AgentToken(factory), body));

        // Assert
        var call = factory.Modules.LocalAgents.FinishCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(call.JobId, Is.EqualTo(FakeLocalAgentService.JobId));
            Assert.That(call.Finish, Is.EqualTo(new LocalAgentFinish(AssistantMessageStatus.Failed, "沒有登入", new AssistantUsage(1200, 80))));
        }
    }

    [Test]
    public async Task Finish_ErrorAndUsageOmittedOrNull_AreAccepted()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var omitted = await client.SendAsync(Post(FinishRoute, AgentToken(factory), new { status = "complete" }));
        using var nulls = await client.SendAsync(Post(FinishRoute, AgentToken(factory), new { status = "stopped", error = (string?)null, usage = (object?)null }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(omitted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(nulls.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(
                factory.Modules.LocalAgents.FinishCalls.Select(c => c.Finish),
                Is.EqualTo(new[]
                {
                    new LocalAgentFinish(AssistantMessageStatus.Complete, null, null),
                    new LocalAgentFinish(AssistantMessageStatus.Stopped, null, null),
                }));
        }
    }

    [TestCase("""{"status":"done"}""", TestName = "狀態不認得")]
    [TestCase("""{}""", TestName = "沒有狀態")]
    [TestCase("""{"status":"complete","usage":{"inputTokens":1}}""", TestName = "用量少了欄位")]
    public async Task Finish_MalformedBody_Returns400(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, FinishRoute, AgentToken(factory));
        request.Content = ApiRequests.Document(body);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(factory.Modules.LocalAgents.FinishCalls, Is.Empty);
    }

    [Test]
    public async Task Finish_JobNotFound_Returns404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.LocalAgents.OnFinish = (_, _, _) =>
            new Failure(FailureKind.NotFound, AiErrorCodes.AgentJobNotFound, "找不到這件工作，或它已經結束。");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(FinishRoute, AgentToken(factory), new { status = "complete" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, AiErrorCodes.AgentJobNotFound);
    }

    [Test]
    public async Task AgentEndpoints_RequestsBeyondThePermitLimit_Return429()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var token = AgentToken(factory);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.LocalAgentPermitsPerWindow; i++)
        {
            using var accepted = await client.SendAsync(Post(CheckInRoute, token, CheckInBody(capacity: 0)));
            statuses.Add(accepted.StatusCode);
        }

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, token, CheckInBody(capacity: 0)));

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
    }

    [Test]
    public async Task Machines_List_ReturnsTheMachinesInTheContractShapeForTheSessionPerson()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(AccountRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        const string expected = """
            [{"id":"55555555-5555-5555-5555-555555555551","name":"辦公室的 Mac","agentVersion":"0.9.0",
            "engines":[{"kind":"claude_code","version":"2.1.330"},{"kind":"codex","version":"0.163.0"}],
            "online":true,"lastSeenAt":"2026-11-23T03:00:00Z"}]
            """;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(expected)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.LocalAgentDirectory.ListCalls, Is.EqualTo(new[] { TestUsers.Member.ToActor() }));
        }
    }

    [Test]
    public async Task Machines_Remove_Returns204AndPassesTheId_OrNotFound()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var missing = Guid.Parse("55555555-5555-5555-5555-555555555599");
        factory.Modules.LocalAgentDirectory.OnRemove = (_, id) =>
            id == missing ? new Failure(FailureKind.NotFound, AiErrorCodes.ConnectionNotFound, "找不到這台電腦。") : Unit.Value;

        // Act
        using var removed = await client.DeleteAsync($"{AccountRoute}/{FakeLocalAgentDirectory.MachineId}");
        using var notFound = await client.DeleteAsync($"{AccountRoute}/{missing}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(factory.Modules.LocalAgentDirectory.RemoveCalls.First(), Is.EqualTo((TestUsers.Member.ToActor(), FakeLocalAgentDirectory.MachineId)));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(notFound, HttpStatusCode.NotFound, AiErrorCodes.ConnectionNotFound);
    }

    [Test]
    public async Task Machines_WithoutLogin_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(AccountRoute);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
