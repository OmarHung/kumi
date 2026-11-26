using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using static PmTool.Api.Tests.AccessTokenAuthenticationTests;

namespace PmTool.Api.Tests;

/// <summary>
/// worker 的端點（/v1/host-agent/*）：只認機器憑證（kha_）、不是任何人的身分、失敗算進 BearerFailureLimiter、
/// 資料的形狀、請求怎麼交給模組、失敗代碼、配對依 IP 限流。
/// </summary>
[TestFixture]
public sealed class HostAgentWorkerEndpointTests
{
    private const string CheckInRoute = "/v1/host-agent/check-in";
    private const string PairRoute = "/v1/host-agent/pair";

    private static string EventsRoute => $"/v1/host-agent/jobs/{FakeHostAgentWorkerService.JobId}/events";

    private static string AttachmentRoute(Guid attachmentId, string query) =>
        $"/v1/host-agent/jobs/{FakeHostAgentWorkerService.JobId}/attachments/{attachmentId}{query}";

    private static string FinishRoute => $"/v1/host-agent/jobs/{FakeHostAgentWorkerService.JobId}/finish";

    private static object CheckInBody(int capacity = 1) => new
    {
        agentVersion = "0.9.0",
        serverUrl = "http://api:8080",
        machineKey = "machine-0001",
        machineName = "host-01",
        capacity,
        engines = new object[]
        {
            new
            {
                kind = "claude_code",
                installed = true,
                version = "2.1.330",
                supported = true,
                loggedIn = true,
                billing = "api",
                loginId = FakeHostAgentAdminService.LoginId,
                models = new[] { new { id = "default", displayName = "預設", efforts = new[] { "low", "high" }, defaultEffort = (string?)"high" } },
                problem = (string?)null,
            },
            new { kind = "codex", installed = false, supported = false, loggedIn = false, billing = "unknown", models = Array.Empty<object>() },
        },
    };

    private static HttpRequestMessage Post(string route, string? secret, object body)
    {
        var request = secret is null ? new HttpRequestMessage(HttpMethod.Post, route) : WorkerBearer(HttpMethod.Post, route, secret);
        request.Content = ApiRequests.Json(body);
        return request;
    }

    /// <summary>帶機器憑證的請求：預設帶配對時的機器編號（<c>X-Kumi-Machine</c>）；<paramref name="machineKey"/> 給 null 就不帶。</summary>
    private static HttpRequestMessage WorkerBearer(
        HttpMethod method, string route, string secret, string? machineKey = FakeHostAgentWorkerService.MachineKey)
    {
        var request = Bearer(method, route, secret);
        if (machineKey is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Kumi-Machine", machineKey);
        }

        return request;
    }

    private static HttpRequestMessage Worker(string route, object body) => Post(route, FakeHostAgentWorkerService.Credential, body);

    [Test]
    public async Task CheckIn_ValidCredential_PassesTheAgentAndTheContentToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody(capacity: 2)));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var (method, agent, argument) = factory.Modules.HostWorker.Calls.Single();
        var (checkIn, capacity) = ((HostAgentCheckIn, int))argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"job":null,"logout":[],"disabled":false,"rotatedCredential":null}""")), Is.True, data?.ToJsonString());
            Assert.That(method, Is.EqualTo("CheckInAsync"));
            Assert.That(agent, Is.EqualTo(FakeHostAgentWorkerService.Identity));
            Assert.That(capacity, Is.EqualTo(2));
            Assert.That(checkIn.AgentVersion, Is.EqualTo("0.9.0"));
            Assert.That(checkIn.ServerUrl, Is.EqualTo("http://api:8080"));
            Assert.That((checkIn.MachineKey, checkIn.MachineName), Is.EqualTo(("machine-0001", "host-01")));
            var claude = checkIn.Engines[0];
            Assert.That(claude.Kind, Is.EqualTo(AiProviderKind.ClaudeCode));
            Assert.That((claude.Installed, claude.Supported, claude.LoggedIn, claude.Billing), Is.EqualTo((true, true, true, HostBilling.Api)));
            Assert.That(claude.Version, Is.EqualTo("2.1.330"));
            Assert.That(claude.LoginId, Is.EqualTo(FakeHostAgentAdminService.LoginId));
            Assert.That(claude.Models.Single().DefaultEffort, Is.EqualTo(AiEffort.High));
            var codex = checkIn.Engines[1];
            Assert.That((codex.Kind, codex.Installed, codex.Version, codex.LoginId, codex.Problem), Is.EqualTo((AiProviderKind.Codex, false, (string?)null, (Guid?)null, (string?)null)));
        }
    }

    [Test]
    public async Task CheckIn_ModuleHandsBackAJobAndALogout_ReturnsThemInTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnCheckIn = (_, _, _) =>
            new HostCheckInResult(FakeHostAgentWorkerService.Job(), [AiProviderKind.Codex], false);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        const string expected = """
            {"job":{"jobId":"77777777-7777-7777-7777-777777777771","lease":3,"engine":"claude_code","model":"default","effort":"high",
            "projectKey":"WEB","systemPrompt":"系統提示","prompt":"使用者：嗨","allowedTools":["get_blueprint","propose_item_changes"],
            "mcpUrl":"http://api:8080/mcp","maxTurns":13,"toolToken":"kjt_secret-tool-token","toolTokenExpiresAt":"2026-11-24T03:06:00Z","attachments":[]},
            "logout":["codex"],"disabled":false,"rotatedCredential":null}
            """;
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(expected)), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task CheckIn_JobWithAttachments_ReturnsThemWithTheAgreedFieldNames()
    {
        // Arrange
        var image = new LocalAgentJobAttachment(
            Guid.Parse("66666666-6666-6666-6666-666666666661"), "截圖.png", "image/png", AssistantAttachmentKind.Image, 1234, new string('b', 64));
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnCheckIn = (_, _, _) =>
            new HostCheckInResult(FakeHostAgentWorkerService.Job() with { Attachments = [image] }, [], false);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse($$"""
            [{"id":"66666666-6666-6666-6666-666666666661","fileName":"截圖.png","contentType":"image/png","kind":"image","sizeBytes":1234,"sha256":"{{new string('b', 64)}}"}]
            """);
        Assert.That(JsonNode.DeepEquals(data?["job"]?["attachments"], expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task DownloadAttachment_ValidLease_StreamsTheBytesAndPassesTheLeaseToTheModule()
    {
        // Arrange
        var attachmentId = Guid.Parse("66666666-6666-6666-6666-666666666661");
        var bytes = new byte[] { 1, 2, 3, 4 };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnOpenAttachment = (_, _, _, _) => new AssistantAttachmentContent(
            new AssistantAttachment(attachmentId, "截圖.png", "image/png", bytes.Length, AssistantAttachmentKind.Image, DateTimeOffset.UnixEpoch),
            "abc123",
            new MemoryStream(bytes));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(
            WorkerBearer(HttpMethod.Get, AttachmentRoute(attachmentId, "?lease=3"), FakeHostAgentWorkerService.Credential));
        var body = await response.Content.ReadAsByteArrayAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(response.Headers.GetValues("X-Attachment-Sha256").Single(), Is.EqualTo("abc123"));
            Assert.That(body, Is.EqualTo(bytes));
            var call = factory.Modules.HostWorker.Calls.Single(c => c.Method == "OpenJobAttachmentAsync");
            Assert.That(call.Argument, Is.EqualTo((FakeHostAgentWorkerService.JobId, 3, attachmentId)));
        }
    }

    [Test]
    public async Task DownloadAttachment_WithoutALease_Returns422WithoutReachingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(
            WorkerBearer(HttpMethod.Get, AttachmentRoute(Guid.NewGuid(), string.Empty), FakeHostAgentWorkerService.Credential));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(factory.Modules.HostWorker.Calls.Any(c => c.Method == "OpenJobAttachmentAsync"), Is.False);
        }
    }

    [Test]
    public async Task DownloadAttachment_LeaseLost_Returns409WithTheCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnOpenAttachment = (_, _, _, _) =>
            new Failure(FailureKind.Conflict, AiErrorCodes.HostJobLeaseLost, "租約不對");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(
            WorkerBearer(HttpMethod.Get, AttachmentRoute(Guid.NewGuid(), "?lease=9"), FakeHostAgentWorkerService.Credential));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, AiErrorCodes.HostJobLeaseLost);
    }

    [Test]
    public async Task DownloadAttachment_WithoutTheCredential_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(AttachmentRoute(Guid.NewGuid(), "?lease=3"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task CheckIn_DisabledAgent_ReportsDisabled()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnCheckIn = (_, _, _) => new HostCheckInResult(null, [], true);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["disabled"]?.GetValue<bool>(), Is.True);
    }

    [TestCase(-1)]
    [TestCase(17)]
    public async Task CheckIn_CapacityOutOfRange_Is422WithoutAskingTheModule(int capacity)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody(capacity)));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostAgentInvalid);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "ai.field_invalid|capacity" }));
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [TestCase("""[null]""", "engines[0]", TestName = "引擎的元素是 null")]
    [TestCase("""[{"kind":"codex","installed":true,"supported":true,"loggedIn":true,"billing":"api","models":[null]}]""", "engines[0].models[0]", TestName = "模型的元素是 null")]
    [TestCase("""[{"kind":"codex","installed":true,"supported":true,"loggedIn":true,"billing":"api","models":[{"id":"a","displayName":"A","efforts":[null]}]}]""", "engines[0].models[0].efforts[0]", TestName = "思考深度的元素是 null")]
    public async Task CheckIn_ANullArrayElement_Is422WithThePathAndTheModuleIsNotAsked(string engines, string path)
    {
        // Arrange：元素是 null 時轉換會丟 NullReferenceException，回 500；要回和其他欄位一樣的 422。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = WorkerBearer(HttpMethod.Post, CheckInRoute, FakeHostAgentWorkerService.Credential);
        request.Content = ApiRequests.Document($$"""{"agentVersion":"0.9.0","serverUrl":"http://api:8080","capacity":1,"engines":{{engines}}}""");

        // Act
        using var response = await client.SendAsync(request);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostAgentInvalid);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"ai.field_invalid|{path}" }));
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Report_ANullEvent_Is422WithTheIndexAndTheModuleIsNotAsked()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = WorkerBearer(HttpMethod.Post, EventsRoute, FakeHostAgentWorkerService.Credential);
        request.Content = ApiRequests.Document("""{"lease":3,"firstSeq":1,"events":[{"type":"text","delta":"ok"},null]}""");

        // Act
        using var response = await client.SendAsync(request);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostAgentInvalid);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "ai.field_invalid|events[1]" }));
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task CheckIn_ModuleHandsBackARotatedCredential_ReturnsItOnce()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnCheckIn = (_, _, _) => new HostCheckInResult(null, [], false) { RotatedCredential = "kha_next-credential" };
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["rotatedCredential"]?.GetValue<string>(), Is.EqualTo("kha_next-credential"));
    }

    [Test]
    public async Task CheckIn_MachineKeyMismatch_Is401LikeABadCredential()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnCheckIn = (_, _, _) => new Failure(FailureKind.Forbidden, AiErrorCodes.HostMachineMismatch, "機器編號不同。");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [TestCase(null, TestName = "沒帶機器編號")]
    [TestCase("machine-9999", TestName = "機器編號和配對時的不同")]
    public async Task EveryWorkerEndpoint_WithoutTheRightMachineKey_Is401AndTheModuleIsNotAsked(string? machineKey)
    {
        // Arrange：機器編號綁在每個 kha_ 請求上，不只是登記：領登入碼、回報、結束也都要擋。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var routes = new[]
        {
            CheckInRoute, EventsRoute, FinishRoute, "/v1/host-agent/logins/claim",
            $"/v1/host-agent/logins/{FakeHostAgentAdminService.LoginId}/complete",
        };

        foreach (var route in routes)
        {
            // Act
            var request = WorkerBearer(HttpMethod.Post, route, FakeHostAgentWorkerService.Credential, machineKey);
            request.Content = ApiRequests.Json(new { });
            using var response = await client.SendAsync(request);

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        }

        Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
    }

    [Test]
    public async Task WorkerEndpoints_WithTheWrongMachineKey_CountAgainstTheFailureLimit()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.BearerFailuresPerWindow; i++)
        {
            var wrong = WorkerBearer(HttpMethod.Post, EventsRoute, FakeHostAgentWorkerService.Credential, "machine-9999");
            wrong.Content = ApiRequests.Json(new { });
            using var failed = await client.SendAsync(wrong);
        }

        // Act
        var blocked = WorkerBearer(HttpMethod.Post, EventsRoute, FakeHostAgentWorkerService.Credential, "machine-9999");
        blocked.Content = ApiRequests.Json(new { });
        using var response = await client.SendAsync(blocked);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task CheckIn_ModuleFailure_IsPassedThrough()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnCheckIn = (_, _, _) => new Failure(FailureKind.Invalid, AiErrorCodes.HostAgentInvalid, "引擎重複。");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostAgentInvalid);
    }

    [Test]
    public async Task WorkerEndpoints_WithoutACredential_Are401AndNothingReachesTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, null, CheckInBody()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
            Assert.That(factory.Modules.HostWorker.AuthenticateCalls, Is.Zero, "沒帶標頭不查資料庫");
        }
    }

    [Test]
    public async Task WorkerEndpoints_AnUnknownCredential_Is401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, "kha_not-a-real-credential", CheckInBody()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task WorkerEndpoints_APersonalAccessTokenOrAToolGrant_IsNotAMachineCredential()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var personal = factory.Modules.AccessTokens.Issue(TestUsers.Owner, [.. AccessTokenScopes.All]);
        using var client = factory.CreateClient();

        // Act
        using var withPersonal = await client.SendAsync(Post(CheckInRoute, personal, CheckInBody()));
        using var withGrant = await client.SendAsync(Post(CheckInRoute, FakeHostToolGrants.Token, CheckInBody()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withPersonal.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(withGrant.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(factory.Modules.HostWorker.AuthenticateCalls, Is.Zero, "不是 kha_ 開頭的不當成憑證去查");
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task WorkerEndpoints_ASessionCookie_Is401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.SendAsync(Post(CheckInRoute, null, CheckInBody()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task MachineCredential_OnAnyOtherEndpoint_Is401AndNeverBecomesAPerson()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        string[] routes = ["/v1/session", "/v1/projects", "/v1/items/WEB-1", "/v1/ai/options", "/v1/account/local-agents"];

        foreach (var route in routes)
        {
            // Act
            using var response = await client.SendAsync(Bearer(HttpMethod.Get, route, FakeHostAgentWorkerService.Credential));

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        }

        using var agentCheckIn = await client.SendAsync(Post("/v1/agent/check-in", FakeHostAgentWorkerService.Credential, new { }));
        await ApiRequests.AssertErrorEnvelopeAsync(agentCheckIn, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.Projects.ListCalls, Is.Empty);
            Assert.That(factory.Modules.HostWorker.AuthenticateCalls, Is.Zero);
            Assert.That(factory.Modules.AccessTokens.FindCalls, Is.Empty, "機器憑證不當成存取權杖去查");
        }
    }

    [Test]
    public async Task WorkerEndpoints_FailedCredentials_CountAgainstTheBearerFailureLimit()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.BearerFailuresPerWindow; i++)
        {
            using var failed = await client.SendAsync(Post(CheckInRoute, $"kha_wrong-{i}", CheckInBody()));
            Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        var lookups = factory.Modules.HostWorker.AuthenticateCalls;

        // Act
        using var blocked = await client.SendAsync(Post(CheckInRoute, "kha_wrong-again", CheckInBody()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(blocked, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocked.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(factory.Modules.HostWorker.AuthenticateCalls, Is.EqualTo(lookups), "超過次數之後不再查");
        }
    }

    [Test]
    public async Task WorkerEndpoints_ValidCredentials_DoNotUseUpTheFailureAllowanceOrTheAnonymousLimit()
    {
        // Arrange：長輪詢加上回報很容易超過沒登入的每分鐘 60 次，機器憑證依代理算，另有自己的額度。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 100; i++)
        {
            using var response = await client.SendAsync(Worker(CheckInRoute, CheckInBody(capacity: 0)));
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Pair_ValidCode_ReturnsTheCredentialOnceWithoutAnyAuthorization()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var body = new { code = "K7QD-3MZP-W9XA", machineKey = "machine-0001", machineName = "host-01", agentVersion = "0.9.0" };

        // Act
        using var response = await client.SendAsync(Post(PairRoute, null, body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var request = (HostPairingRequest)factory.Modules.HostWorker.Calls.Single().Argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                JsonNode.DeepEquals(data, JsonNode.Parse($$"""{"agentId":"{{FakeHostAgentAdminService.AgentId}}","agentName":"主機一號","credential":"{{FakeHostAgentWorkerService.Credential}}"}""")),
                Is.True,
                data?.ToJsonString());
            Assert.That((request.Code, request.MachineKey, request.MachineName, request.AgentVersion), Is.EqualTo(("K7QD-3MZP-W9XA", "machine-0001", "host-01", "0.9.0")));
        }
    }

    [Test]
    public async Task Pair_WrongCode_Is422AndCountsAgainstTheFailureLimit()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var body = new { code = "AAAA-AAAA-AAAA", machineKey = "machine-0001", machineName = "host-01", agentVersion = "0.9.0" };

        // Act
        using var response = await client.SendAsync(Post(PairRoute, null, body));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostPairingInvalid);
    }

    [Test]
    public async Task Pair_SourceAlreadyBlockedByFailures_IsRefusedBeforeTheModuleIsAsked()
    {
        // Arrange：每分鐘 10 次的限流之外，猜錯也記進失敗額度（同一個來源 30 次）；用不同的來源避開 10 次的限制太麻煩，
        // 所以直接用其他 Bearer 端點把額度灌滿，確認配對也看同一份。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.BearerFailuresPerWindow; i++)
        {
            using var failed = await client.SendAsync(Post(CheckInRoute, $"kha_wrong-{i}", CheckInBody()));
        }

        var body = new { code = "K7QD-3MZP-W9XA", machineKey = "machine-0001", machineName = "host-01", agentVersion = "0.9.0" };

        // Act
        using var blocked = await client.SendAsync(Post(PairRoute, null, body));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(blocked, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Pair_MoreThanTenPerMinuteFromOneAddress_Is429ButAnotherAddressIsNot()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        using var client = factory.CreateClient();
        var body = new { code = "K7QD-3MZP-W9XA", machineKey = "machine-0001", machineName = "host-01", agentVersion = "0.9.0" };
        HttpRequestMessage Pairing(string ip)
        {
            var request = Post(PairRoute, null, body);
            request.Headers.Add(ApiFactory.RemoteIpHeader, ip);
            return request;
        }

        for (var i = 0; i < ApiLimits.HostPairPermitsPerWindow; i++)
        {
            using var ok = await client.SendAsync(Pairing("203.0.113.9"));
            Assert.That(ok.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Act
        using var limited = await client.SendAsync(Pairing("203.0.113.9"));
        using var other = await client.SendAsync(Pairing("203.0.113.10"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(limited, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Report_PassesTheLeaseSequenceAndConvertedEvents()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var body = new
        {
            lease = 3,
            firstSeq = 5,
            events = new object[]
            {
                new { type = "text", delta = "你好" },
                new { type = "tool", callId = "c1", name = "get_item", status = "done" },
            },
        };

        // Act
        using var response = await client.SendAsync(Worker(EventsRoute, body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var (jobId, lease, firstSeq, events) = ((Guid, int, int, IReadOnlyList<LocalAgentEvent>))factory.Modules.HostWorker.Calls.Single().Argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"cancelled":false,"nextSeq":7}""")), Is.True, data?.ToJsonString());
            Assert.That((jobId, lease, firstSeq), Is.EqualTo((FakeHostAgentWorkerService.JobId, 3, 5)));
            Assert.That(events[0], Is.EqualTo(new LocalAgentText("你好")));
            Assert.That(((LocalAgentTool)events[1]).Tool.Name, Is.EqualTo("get_item"));
        }
    }

    [Test]
    public async Task Report_AMalformedEvent_Is422WithTheHostCodeAndTheModuleIsNotAsked()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var body = new { lease = 3, firstSeq = 1, events = new object[] { new { type = "text", delta = "ok" }, new { type = "magic" } } };

        // Act
        using var response = await client.SendAsync(Worker(EventsRoute, body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostAgentInvalid);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "ai.field_invalid|events[1]" }));
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Report_LeaseLost_Is409AndACancelledAckIsStillA200()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnReport = (_, _, lease, _, _) => lease == 1
            ? new HostReportAck(true, 9)
            : new Failure(FailureKind.Conflict, AiErrorCodes.HostJobLeaseLost, "租約不對。");
        using var client = factory.CreateClient();

        // Act
        using var lost = await client.SendAsync(Worker(EventsRoute, new { lease = 2, firstSeq = 1, events = Array.Empty<object>() }));
        using var cancelled = await client.SendAsync(Worker(EventsRoute, new { lease = 1, firstSeq = 1, events = Array.Empty<object>() }));
        var ack = await ApiRequests.AssertSuccessEnvelopeAsync(cancelled, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(lost, HttpStatusCode.Conflict, AiErrorCodes.HostJobLeaseLost);
            Assert.That(ack?["cancelled"]?.GetValue<bool>(), Is.True);
            Assert.That(ack?["nextSeq"]?.GetValue<int>(), Is.EqualTo(9));
        }
    }

    [Test]
    public async Task Report_UnknownJob_Is404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnReport = (_, _, _, _, _) => new Failure(FailureKind.NotFound, AiErrorCodes.AgentJobNotFound, "找不到這件工作。");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(EventsRoute, new { lease = 1, firstSeq = 1, events = Array.Empty<object>() }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, AiErrorCodes.AgentJobNotFound);
    }

    [Test]
    public async Task Finish_Returns204AndPassesTheLeaseStatusErrorAndUsage()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var body = new { lease = 3, status = "failed", error = "引擎中斷", usage = new { inputTokens = 100, outputTokens = 50 } };

        // Act
        using var response = await client.SendAsync(Worker(FinishRoute, body));

        // Assert
        var (jobId, lease, finish) = ((Guid, int, LocalAgentFinish))factory.Modules.HostWorker.Calls.Single().Argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That((jobId, lease), Is.EqualTo((FakeHostAgentWorkerService.JobId, 3)));
            Assert.That(finish, Is.EqualTo(new LocalAgentFinish(AssistantMessageStatus.Failed, "引擎中斷", new AssistantUsage(100, 50))));
        }
    }

    [Test]
    public async Task Finish_WithoutALease_Is400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker(FinishRoute, new { status = "complete" }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task ClaimLogin_ReturnsTheClaimAndPassesTheCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker("/v1/host-agent/logins/claim", new { code = "H4TT-9KQ2-MM7C" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.HostWorker.Calls.Single().Argument, Is.EqualTo("H4TT-9KQ2-MM7C"));
            Assert.That(
                JsonNode.DeepEquals(data, JsonNode.Parse($$"""{"loginId":"{{FakeHostAgentAdminService.LoginId}}","engine":"claude_code","expiresAt":"2026-11-24T03:15:00Z"}""")),
                Is.True,
                data?.ToJsonString());
        }
    }

    [Test]
    public async Task ClaimLogin_Rejected_Is422()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnClaim = (_, _) => new Failure(FailureKind.Invalid, AiErrorCodes.HostLoginRejected, "登入碼不對。");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker("/v1/host-agent/logins/claim", new { code = "wrong" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostLoginRejected);
    }

    [Test]
    public async Task CompleteLogin_PassesTheReportAndReturnsTheLogin()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var route = $"/v1/host-agent/logins/{FakeHostAgentAdminService.LoginId}/complete";

        // Act
        using var response = await client.SendAsync(Worker(route, new { loggedIn = true, billing = "api" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var (loginId, report) = ((Guid, HostLoginReport))factory.Modules.HostWorker.Calls.Single().Argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loginId, Is.EqualTo(FakeHostAgentAdminService.LoginId));
            Assert.That(report, Is.EqualTo(new HostLoginReport(true, HostBilling.Api, null)));
            Assert.That(data?["engine"]?.GetValue<string>(), Is.EqualTo("claude_code"));
        }
    }

    [Test]
    public async Task CompleteLogin_SubscriptionBilling_IsRejectedByTheModuleAs422()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostWorker.OnComplete = (_, _, _) =>
            new Failure(FailureKind.Invalid, AiErrorCodes.HostLoginRejected, "訂閱帳號依條款不能給全組織共用。");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Worker($"/v1/host-agent/logins/{FakeHostAgentAdminService.LoginId}/complete", new { loggedIn = true, billing = "subscription" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostLoginRejected);
    }

    [Test]
    public async Task Body_OverTheLimit_Is413()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var request = WorkerBearer(HttpMethod.Post, EventsRoute, FakeHostAgentWorkerService.Credential);
        request.Content = ApiRequests.Document("{\"lease\":1,\"firstSeq\":1,\"events\":[]}" + new string(' ', 1024 * 1024));

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
            Assert.That(factory.Modules.HostWorker.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task WorkerEndpoints_DoNotNeedTheCsrfHeader()
    {
        // Arrange：用戶端是命令列的服務，不帶防偽標頭；憑證在標頭裡，瀏覽器不會自己帶，所以不需要。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var withCredential = await client.SendAsync(Worker(CheckInRoute, CheckInBody()));
        using var pairing = await client.SendAsync(Post(PairRoute, null, new { code = "x", machineKey = "machine-0001", machineName = "h", agentVersion = "1" }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withCredential.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(pairing.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
        }
    }
}
