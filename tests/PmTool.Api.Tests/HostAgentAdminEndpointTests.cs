using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 主機代理的管理端點（/v1/ai/host/*）：只能用網頁的 Cookie（權杖與機器憑證都不行）、資料的形狀（人員編號換成名稱）、
/// 請求怎麼交給模組、「只有擁有者」的失敗怎麼轉成 403、路徑上的引擎。擁有者的判斷在模組，這裡不重複。
/// </summary>
[TestFixture]
public sealed class HostAgentAdminEndpointTests
{
    private const string Root = "/v1/ai/host";

    private static string AgentRoute => $"{Root}/agents/{FakeHostAgentAdminService.AgentId}";

    private static string SourceRoute => $"{Root}/sources/{FakeHostAgentAdminService.SourceId}";

    private static Failure OwnerOnly() =>
        new(FailureKind.Forbidden, AiErrorCodes.HostOwnerOnly, "只有組織擁有者能管理主機 AI 代理。");

    private static object SourceBody(bool enabled = true, bool acknowledge = true) => new
    {
        agentId = FakeHostAgentAdminService.AgentId,
        engine = "claude_code",
        name = "主機上的 Claude Code",
        enabled,
        enabledModels = new[] { "default" },
        defaultModel = "default",
        defaultEffort = "high",
        limits = new { dailyRequestsPerPerson = 5, monthlyRequests = (int?)null, monthlyTokens = 1000000 },
        acknowledgeApiBilling = acknowledge,
    };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = ApiRequests.Json(body);
        }

        return await client.SendAsync(request);
    }

    [Test]
    public async Task Get_Owner_ReturnsTheContractShapeWithPeopleAsReferences()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Root);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var owner = $$"""{"personId":"{{TestUsers.Owner.PersonId}}","displayName":"{{TestUsers.Owner.DisplayName}}"}""";
        var expected = JsonNode.Parse($$"""
            {"agents":[{"id":"{{FakeHostAgentAdminService.AgentId}}","name":"主機一號","state":"active","online":true,"lastSeenAt":"2026-11-24T03:00:00Z",
              "machineName":"host-01","agentVersion":"0.9.0","serverUrl":"http://api:8080",
              "engines":[{"kind":"claude_code","readiness":"ready","version":"2.1.330","loggedIn":true,"billing":"api",
                "models":[{"id":"default","displayName":"預設","efforts":["low","high"],"defaultEffort":"high"}],
                "problem":null,"authorizedAt":"2026-11-24T03:00:00Z","authorizedBy":{{owner}}}],
              "logins":[{"id":"{{FakeHostAgentAdminService.LoginId}}","engine":"claude_code","state":"pending","expiresAt":"2026-11-24T03:15:00Z",
                "error":null,"startedBy":{{owner}},"createdAt":"2026-11-24T03:00:00Z"}],
              "pairingExpiresAt":null,"createdAt":"2026-11-23T03:00:00Z",
              "credentialExpiresAt":"2027-01-23T03:00:00Z",
              "warning":{"kind":"other_machine","machineName":"別台電腦","at":"2026-11-24T02:00:00Z"}
             }],
             "sources":[{"id":"{{FakeHostAgentAdminService.SourceId}}","agentId":"{{FakeHostAgentAdminService.AgentId}}","engine":"claude_code",
              "name":"主機上的 Claude Code","enabled":true,"status":"ready","statusMessage":null,
              "models":[{"id":"default","displayName":"預設","efforts":["low","high"],"enabled":true,"custom":false}],
              "defaultModel":"default","defaultEffort":"high",
              "limits":{"dailyRequestsPerPerson":5,"monthlyRequests":200,"monthlyTokens":null},
              "running":1,"queued":2,"usage":{"requests":10,"completed":7,"failed":2,"stopped":1,"inputTokens":1000,"outputTokens":500},
              "billingAcknowledgedAt":"2026-11-24T03:00:00Z","updatedAt":"2026-11-24T03:00:00Z","updatedBy":{{owner}}}]}
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.HostAdmin.Calls.Single().Actor, Is.EqualTo(TestUsers.Owner.ToActor()));
        }
    }

    [TestCase("Admin")]
    [TestCase("Member")]
    public async Task EveryEndpoint_NotTheOwner_ModuleRefusal_Is403WithTheOwnerOnlyCode(string roleName)
    {
        // Arrange：擁有者的判斷在模組；這裡確認模組回的失敗原樣變成 403，而且每個端點都問了模組（不是 API 自己擋或放行）。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostAdmin.OnCall = (_, _) => OwnerOnly();
        var user = roleName == "Admin" ? TestUsers.Owner with { Role = OrganizationRole.Admin } : TestUsers.Member;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, user);
        (HttpMethod Method, string Route, object? Body)[] requests =
        [
            (HttpMethod.Get, Root, null),
            (HttpMethod.Post, Root + "/agents", new { name = "x" }),
            (HttpMethod.Patch, AgentRoute, new { name = "x", enabled = true }),
            (HttpMethod.Post, AgentRoute + "/pairing", null),
            (HttpMethod.Delete, AgentRoute, null),
            (HttpMethod.Post, AgentRoute + "/engines/claude_code/login", null),
            (HttpMethod.Post, AgentRoute + "/engines/codex/logout", null),
            (HttpMethod.Delete, $"{Root}/logins/{FakeHostAgentAdminService.LoginId}", null),
            (HttpMethod.Post, Root + "/sources", SourceBody()),
            (HttpMethod.Put, SourceRoute, SourceBody()),
            (HttpMethod.Delete, SourceRoute, null),
            (HttpMethod.Get, SourceRoute + "/usage", null),
        ];

        foreach (var (method, route, body) in requests)
        {
            // Act
            using var response = await SendAsync(client, method, route, body);

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, AiErrorCodes.HostOwnerOnly);
        }

        Assert.That(factory.Modules.HostAdmin.Calls, Has.Count.EqualTo(requests.Length));
    }

    [Test]
    public async Task CreateAgent_Owner_Returns201WithThePairingCodeAndPassesTheName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await SendAsync(client, HttpMethod.Post, Root + "/agents", new { name = "主機一號" });
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var call = factory.Modules.HostAdmin.Calls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/ai/host/agents/{FakeHostAgentAdminService.AgentId}"));
            Assert.That(data?["pairingCode"]?.GetValue<string>(), Is.EqualTo("K7QD-3MZP-W9XA"));
            Assert.That(data?["pairingExpiresAt"]?.GetValue<string>(), Is.EqualTo("2026-11-24T03:15:00Z"));
            Assert.That(data?["agent"]?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeHostAgentAdminService.AgentId));
            Assert.That(((HostAgentInput)call.Argument!).Name, Is.EqualTo("主機一號"));
        }
    }

    [Test]
    public async Task UpdateAgent_PassesTheIdNameAndEnabledFlag()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await SendAsync(client, HttpMethod.Patch, AgentRoute, new { name = "新名稱", enabled = false });
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var (id, input) = ((Guid, HostAgentInput))factory.Modules.HostAdmin.Calls.Single().Argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(id, Is.EqualTo(FakeHostAgentAdminService.AgentId));
            Assert.That(input, Is.EqualTo(new HostAgentInput("新名稱") { Enabled = false }));
        }
    }

    [Test]
    public async Task ResetPairing_Returns200WithANewPairingCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await SendAsync(client, HttpMethod.Post, AgentRoute + "/pairing");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["pairingCode"]?.GetValue<string>(), Is.EqualTo("K7QD-3MZP-W9XA"));
    }

    [Test]
    public async Task StartLogin_ReturnsTheLoginCodeOnceAndPassesTheEngine()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await SendAsync(client, HttpMethod.Post, AgentRoute + "/engines/codex/login");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var (agentId, engine) = ((Guid, AiProviderKind))factory.Modules.HostAdmin.Calls.Single().Argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["code"]?.GetValue<string>(), Is.EqualTo("H4TT-9KQ2-MM7C"));
            Assert.That(data?["login"]?["startedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(agentId, Is.EqualTo(FakeHostAgentAdminService.AgentId));
            Assert.That(engine, Is.EqualTo(AiProviderKind.Codex));
        }
    }

    [TestCase("anthropic")]
    [TestCase("openai")]
    [TestCase("0")]
    [TestCase("Codex")]
    public async Task LoginAndLogout_NotAHostEngine_Is404WithoutAskingTheModule(string engine)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var login = await SendAsync(client, HttpMethod.Post, $"{AgentRoute}/engines/{engine}/login");
        using var logout = await SendAsync(client, HttpMethod.Post, $"{AgentRoute}/engines/{engine}/logout");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(factory.Modules.HostAdmin.Calls, Is.Empty);
        }
    }

    [TestCase("DELETE", "agents")]
    [TestCase("DELETE", "sources")]
    [TestCase("DELETE", "logins")]
    public async Task Delete_Returns204AndPassesTheId(string method, string kind)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var id = kind switch
        {
            "agents" => FakeHostAgentAdminService.AgentId,
            "sources" => FakeHostAgentAdminService.SourceId,
            _ => FakeHostAgentAdminService.LoginId,
        };

        // Act
        using var response = await SendAsync(client, new HttpMethod(method), $"{Root}/{kind}/{id}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(factory.Modules.HostAdmin.Calls.Single().Argument, Is.EqualTo(id));
        }
    }

    [Test]
    public async Task CreateSource_Returns201AndPassesEveryField()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await SendAsync(client, HttpMethod.Post, Root + "/sources", SourceBody());
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var input = (HostSourceInput)factory.Modules.HostAdmin.Calls.Single().Argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/ai/host/sources/{FakeHostAgentAdminService.SourceId}"));
            Assert.That(data?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeHostAgentAdminService.SourceId));
            Assert.That(input.AgentId, Is.EqualTo(FakeHostAgentAdminService.AgentId));
            Assert.That(input.Engine, Is.EqualTo(AiProviderKind.ClaudeCode));
            Assert.That(input.Name, Is.EqualTo("主機上的 Claude Code"));
            Assert.That(input.Enabled, Is.True);
            Assert.That(input.EnabledModels, Is.EqualTo(new[] { "default" }));
            Assert.That(input.DefaultModel, Is.EqualTo("default"));
            Assert.That(input.DefaultEffort, Is.EqualTo(AiEffort.High));
            Assert.That(input.Limits, Is.EqualTo(new HostSourceLimits(5, null, 1000000)));
            Assert.That(input.AcknowledgeApiBilling, Is.True);
        }
    }

    [Test]
    public async Task UpdateSource_ModuleFailure_IsPassedThroughWithItsDetails()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostAdmin.OnCall = (_, method) => method == "UpdateSourceAsync"
            ? new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "來源的內容不對。")
            {
                Details = [new FailureDetail(AiErrorCodes.FieldRequired, "acknowledgeApiBilling", "要確認這個來源以 API 用量計費。")],
            }
            : null;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await SendAsync(client, HttpMethod.Put, SourceRoute, SourceBody(acknowledge: false));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.SettingsInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "ai.field_required|acknowledgeApiBilling" }));
    }

    [Test]
    public async Task SourceBody_WithoutLimits_Is400BecauseAPutReplacesEverything()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = new { agentId = FakeHostAgentAdminService.AgentId, engine = "codex", name = "x", enabled = false, enabledModels = Array.Empty<string>() };

        // Act
        using var response = await SendAsync(client, HttpMethod.Put, SourceRoute, body);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(factory.Modules.HostAdmin.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task GetUsage_PassesTheMonthAndReturnsPeopleAsReferences()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync($"{SourceRoute}/usage?month=2026-10");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var (_, month) = ((Guid, string?))factory.Modules.HostAdmin.Calls.Single().Argument!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(month, Is.EqualTo("2026-10"));
            Assert.That(data?["month"]?.GetValue<string>(), Is.EqualTo("2026-10"));
            Assert.That(data?["people"]?[0]?["person"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.DisplayName));
            Assert.That(data?["people"]?[0]?["totals"]?["requests"]?.GetValue<int>(), Is.EqualTo(6));
            Assert.That(data?["projects"]?[0]?["projectKey"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(data?.ToJsonString(), Does.Not.Contain("title").And.Not.Contain("content"), "用量沒有對話的內容與標題");
        }
    }

    [Test]
    public async Task GetUsage_WithoutMonth_PassesNull()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync($"{SourceRoute}/usage");
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(((Guid, string?))factory.Modules.HostAdmin.Calls.Single().Argument!, Is.EqualTo((FakeHostAgentAdminService.SourceId, (string?)null)));
    }

    [Test]
    public async Task Endpoints_PersonalAccessToken_Is403BecauseTheyAreWebOnly()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, [.. AccessTokenScopes.All]);

        // Act
        using var read = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, Root, secret));
        using var create = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, Root + "/agents", secret));
        using var usage = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, SourceRoute + "/usage", secret));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var response in new[] { read, create, usage })
            {
                await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
            }

            Assert.That(factory.Modules.HostAdmin.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Endpoints_OAuthToken_Is401BecauseItOnlyWorksOnTheMcpEndpoint()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var secret = McpTesting.GrantToken(factory, TestUsers.Owner, McpTesting.AllScopes);

        // Act
        using var response = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, Root, secret));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(factory.Modules.HostAdmin.Calls, Is.Empty);
        }
    }

    [TestCase(FakeHostAgentWorkerService.Credential)]
    [TestCase(FakeHostToolGrants.Token)]
    public async Task Endpoints_MachineCredentials_Are401(string secret)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var read = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, Root, secret));
        using var write = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, Root + "/agents", secret));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(read, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
            await ApiRequests.AssertErrorEnvelopeAsync(write, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
            Assert.That(factory.Modules.HostAdmin.Calls, Is.Empty);
            Assert.That(factory.Modules.HostWorker.AuthenticateCalls, Is.Zero, "管理端點不問機器憑證");
            Assert.That(factory.Modules.HostGrants.ResolveCalls, Is.Empty, "管理端點不問工具授權");
        }
    }

    [Test]
    public async Task Endpoints_WithoutASession_Are401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.GetAsync(Root);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task Post_WithoutTheCsrfHeader_Is403()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        client.DefaultRequestHeaders.Remove(ApiRequests.CsrfHeader);

        // Act
        using var response = await SendAsync(client, HttpMethod.Post, Root + "/agents", new { name = "x" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(factory.Modules.HostAdmin.Calls, Is.Empty);
        }
    }
}
