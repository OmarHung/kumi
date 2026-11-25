using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>/v1/ai/settings、/v1/ai/options：權限、形狀（金鑰不出現）、預設與開關。</summary>
[TestFixture]
public sealed class AiSettingsEndpointTests
{
    private const string Route = "/v1/ai/settings";
    private const string OptionsRoute = "/v1/ai/options";

    private static string ConnectionId => FakeAiSettingsService.ConnectionId.ToString();

    private static Failure Forbidden() =>
        new(FailureKind.Forbidden, AiErrorCodes.Forbidden, "只有組織的擁有者或管理員能看、能改 AI 設定。");

    [Test]
    public async Task Get_Admin_ReturnsTheContractShapeWithTheUpdatersName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var updatedBy = $$"""{"personId":"{{TestUsers.Owner.PersonId}}","displayName":"{{TestUsers.Owner.DisplayName}}"}""";
        var expected = JsonNode.Parse($$"""
            {"enabled":true,"allowPersonalConnections":false,
             "default":{"connectionId":"{{ConnectionId}}","model":"claude-opus-5-5","effort":"high"},
             "connections":[{"id":"{{ConnectionId}}","name":"Anthropic","kind":"anthropic","baseUrl":null,"hasApiKey":true,"apiKeyHint":"a1b2",
               "models":[{"id":"claude-opus-5-5","displayName":"Claude Opus 5.5","efforts":["low","high","max"],"enabled":true,"custom":false}],
               "modelsCheckedAt":"2026-11-23T03:00:00Z",
               "usage":{"month":"2026-11","inputTokens":120000,"outputTokens":8000,"replies":12},
               "createdAt":"2026-11-22T03:00:00Z","updatedAt":"2026-11-23T03:00:00Z","updatedBy":{{updatedBy}}}],
             "updatedAt":"2026-11-23T03:00:00Z","updatedBy":{{updatedBy}},
             "limits":{"dailyRequestsPerPerson":200,"monthlyRequests":null,"monthlyTokens":null},
             "monthUsage":{"month":"2026-11","requests":12,"tokens":128000} }
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Get_NeverConfigured_HasNullDefaultAndTimestampsAndNoConnections()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiSettings.OnGet = _ => new AiSettings(false, null, [], null, null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["enabled"]?.GetValue<bool>(), Is.False);
            Assert.That(data?["default"], Is.Null);
            Assert.That(data?["updatedAt"], Is.Null);
            Assert.That(data?["updatedBy"], Is.Null);
            Assert.That(data?["connections"]?.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task Get_UpdaterNoLongerInTheOrganization_ShowsThePlaceholderName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var gone = Guid.CreateVersion7();
        factory.Modules.AiSettings.OnGet = _ => FakeAiSettingsService.StoredSettings(updatedBy: gone);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["updatedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("（已不在組織）"));
            Assert.That(data?["connections"]?[0]?["updatedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("（已不在組織）"));
        }
    }

    [Test]
    public async Task Get_Response_NeverContainsAKeyOrItsProperty()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain("sk-ant-key"));
            Assert.That(text.Replace("hasApiKey", string.Empty, StringComparison.Ordinal).Replace("apiKeyHint", string.Empty, StringComparison.Ordinal).ToLowerInvariant(), Does.Not.Contain("apikey"));
            Assert.That(text, Does.Contain("\"hasApiKey\":true").And.Contain("\"apiKeyHint\":\"a1b2\""));
        }
    }

    [TestCase(nameof(TestUsers.Owner), true)]
    [TestCase(nameof(TestUsers.Member), false)]
    [TestCase(nameof(TestUsers.Guest), false)]
    public async Task Get_PassesWhetherTheSessionIsAnOrganizationAdminToTheModule(string who, bool isAdmin)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var user = who switch
        {
            nameof(TestUsers.Owner) => TestUsers.Owner,
            nameof(TestUsers.Member) => TestUsers.Member,
            _ => TestUsers.Guest,
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, user);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        var actor = factory.Modules.AiSettings.GetCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actor.IsOrganizationAdmin, Is.EqualTo(isAdmin));
            Assert.That(actor.PersonId, Is.EqualTo(user.PersonId));
            Assert.That(actor.OrganizationId, Is.EqualTo(user.OrganizationId));
        }
    }

    [TestCase("GET", "/v1/ai/settings")]
    [TestCase("PUT", "/v1/ai/settings")]
    [TestCase("POST", "/v1/ai/connections")]
    [TestCase("PUT", "/v1/ai/connections/11111111-1111-1111-1111-111111111111")]
    [TestCase("POST", "/v1/ai/connections/11111111-1111-1111-1111-111111111111/refresh")]
    [TestCase("DELETE", "/v1/ai/connections/11111111-1111-1111-1111-111111111111")]
    [TestCase("POST", "/v1/ai/models/discover")]
    public async Task AdminOnlyRoutes_ModuleSaysForbidden_Returns403WithTheAiCode(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var ai = factory.Modules.AiSettings;
        ai.OnGet = _ => Forbidden();
        ai.OnSave = (_, _) => Forbidden();
        ai.OnCreate = (_, _) => Forbidden();
        ai.OnUpdate = (_, _, _) => Forbidden();
        ai.OnRefresh = (_, _) => Forbidden();
        ai.OnDelete = (_, _) => Forbidden();
        ai.OnDiscover = (_, _) => Forbidden();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = method is "GET" or "DELETE" ? null : ApiRequests.Json(RequestBodyFor(route)),
        };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, AiErrorCodes.Forbidden);
    }

    private static object RequestBodyFor(string route) => route switch
    {
        "/v1/ai/settings" => new { enabled = false },
        "/v1/ai/models/discover" => new { kind = "openai" },
        _ => new { name = "Anthropic", kind = "anthropic", enabledModels = Array.Empty<string>() },
    };

    [Test]
    public async Task Put_SavesTheDefaultWithTheSnakeCaseEffortAndEchoesTheSettings()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = new { enabled = true, @default = new { connectionId = FakeAiSettingsService.ConnectionId, model = "claude-opus-5-5", effort = "xhigh" } };

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var call = factory.Modules.AiSettings.SaveCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Input, Is.EqualTo(new AiSettingsInput(true, new AiSelection(FakeAiSettingsService.ConnectionId, "claude-opus-5-5", AiEffort.Xhigh))));
            Assert.That(call.Actor.IsOrganizationAdmin, Is.True);
            Assert.That(data?["default"]?["effort"]?.GetValue<string>(), Is.EqualTo("xhigh"));
        }
    }

    [TestCase("""{"enabled":false}""", false, null)]
    [TestCase("""{"enabled":false,"default":null}""", false, null)]
    [TestCase("""{"enabled":true,"default":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m"}}""", true, null)]
    [TestCase("""{"enabled":true,"default":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m","effort":null}}""", true, null)]
    [TestCase("""{"enabled":true,"default":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m","effort":"none"}}""", true, AiEffort.None)]
    [TestCase("""{"enabled":true,"default":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m","effort":"max"}}""", true, AiEffort.Max)]
    public async Task Put_DefaultAndEffortMayBeOmittedOrNull(string body, bool enabled, AiEffort? effort)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Document(body));

        // Assert
        var input = factory.Modules.AiSettings.SaveCalls.Single().Input;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(input.Enabled, Is.EqualTo(enabled));
            Assert.That(input.Default?.Effort, Is.EqualTo(effort));
        }
    }

    [TestCase("""{}""", "", TestName = "Put_EnabledMissing_Returns400")]
    [TestCase("""{"enabled":true,"default":{"model":"m"}}""", "default", TestName = "Put_ConnectionIdMissing_Returns400")]
    [TestCase("""{"enabled":true,"default":{"connectionId":"not-a-guid","model":"m"}}""", "connectionId", TestName = "Put_ConnectionIdNotAGuid_Returns400")]
    [TestCase("""{"enabled":true,"default":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m","effort":"ultra"}}""", "effort", TestName = "Put_UnknownEffort_Returns400")]
    [TestCase("""{"enabled":true,"default":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"m","effort":"XHIGH"}}""", "effort", TestName = "Put_EffortInTheWrongCase_Returns400")]
    [TestCase("""{"enabled":true,"extra":1}""", "extra", TestName = "Put_UnknownProperty_Returns400")]
    public async Task Put_BadBody_Returns400WithoutCallingTheModule(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details, Has.Count.EqualTo(1));
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo(path).Or.Contain(path));
            Assert.That(factory.Modules.AiSettings.SaveCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Put_ModuleRejectsTheDefault_Returns422WithTheDetailPaths()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiSettings.OnSave = (_, _) => new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "AI 設定有幾個欄位不對。")
        {
            Details =
            [
                new FailureDetail(AiErrorCodes.ModelNotAvailable, "default.model", "這個模型沒有開放給成員用。"),
                new FailureDetail(AiErrorCodes.ModelNotAvailable, "default.effort", "這個模型沒有這個思考深度。"),
            ],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(new { enabled = true }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.SettingsInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "ai.model_not_available|default.model", "ai.model_not_available|default.effort" }));
    }

    [Test]
    public async Task Options_AnyMember_GetsTheContractShapeAndIsNotRestrictedToAdmins()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiOptions.OnGet = _ => new AiOptions(
            true,
            [new AiConnectionOption(FakeAiSettingsService.ConnectionId, "Anthropic", AiProviderKind.Anthropic, [new AiModelOption("claude-opus-5-5", "Claude Opus 5.5", [AiEffort.Low, AiEffort.Xhigh]) { DefaultEffort = AiEffort.High }]),
             new AiConnectionOption(Guid.Parse("22222222-2222-2222-2222-222222222222"), "我的 Ollama", AiProviderKind.OpenaiCompatible, [new AiModelOption("llama3", "llama3", [])]) { Personal = true }],
            new AiSelection(FakeAiSettingsService.ConnectionId, "claude-opus-5-5", null)) { CanAddPersonal = true };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Guest);

        // Act
        using var response = await client.GetAsync(OptionsRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse($$$"""
            {"available":true,"canAddPersonal":true,
             "connections":[{"id":"{{{ConnectionId}}}","name":"Anthropic","kind":"anthropic","models":[{"id":"claude-opus-5-5","displayName":"Claude Opus 5.5","efforts":["low","xhigh"],"defaultEffort":"high"}],"personal":false,"hosted":false,"notice":null},
                            {"id":"22222222-2222-2222-2222-222222222222","name":"我的 Ollama","kind":"openai_compatible","models":[{"id":"llama3","displayName":"llama3","efforts":[],"defaultEffort":null}],"personal":true,"hosted":false,"notice":null}],
             "default":{"connectionId":"{{{ConnectionId}}}","model":"claude-opus-5-5","effort":null}}
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.AiOptions.GetCalls.Single().IsOrganizationAdmin, Is.False);
        }
    }

    [Test]
    public async Task Options_HostedSource_CarriesTheHostedFlagAndTheNoticeForEveryRole()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var sourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        factory.Modules.AiOptions.OnGet = _ => new AiOptions(
            true,
            [new AiConnectionOption(sourceId, "主機上的 Claude Code", AiProviderKind.ClaudeCode, [new AiModelOption("default", "預設", [])])
                { Hosted = true, Notice = "主機代理目前離線，請組織擁有者檢查代理服務。" }],
            null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(OptionsRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var option = data?["connections"]?[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(option?["hosted"]?.GetValue<bool>(), Is.True);
            Assert.That(option?["notice"]?.GetValue<string>(), Does.Contain("離線"));
            Assert.That(option?["kind"]?.GetValue<string>(), Is.EqualTo("claude_code"));
            Assert.That(option?["personal"]?.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task Options_NotAvailable_IsEmptyWithANullDefault()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(OptionsRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse("""{"available":false,"canAddPersonal":false,"connections":[],"default":null}""");
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Put_AllowPersonalConnections_IsPassedToTheModuleAndEchoed()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiSettings.OnSave = (_, input) => FakeAiSettingsService.StoredSettings(input) with { AllowPersonalConnections = input.AllowPersonalConnections };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(new { enabled = false, allowPersonalConnections = true }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.AiSettings.SaveCalls.Single().Input.AllowPersonalConnections, Is.True);
            Assert.That(data?["allowPersonalConnections"]?.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task Put_Limits_ArePassedToTheModuleAndEchoed()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(new
        {
            enabled = false,
            limits = new { dailyRequestsPerPerson = 50, monthlyRequests = (int?)null, monthlyTokens = 3000000L },
        }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.AiSettings.SaveCalls.Single().Input.Limits, Is.EqualTo(new AiUsageLimits(50, null, 3000000)));
            Assert.That(data?["limits"]?["dailyRequestsPerPerson"]?.GetValue<int>(), Is.EqualTo(50));
            Assert.That(data?["limits"]?["monthlyRequests"], Is.Null);
            Assert.That(data?["limits"]?["monthlyTokens"]?.GetValue<long>(), Is.EqualTo(3000000));
        }
    }

    [Test]
    public async Task Put_LimitsOmitted_PassesNullSoTheModuleKeepsTheStoredOnes()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(new { enabled = false }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Modules.AiSettings.SaveCalls.Single().Input.Limits, Is.Null);
        }
    }

    [Test]
    public async Task Put_LimitOutOfRange_Returns422WithThePath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiSettings.OnSave = (_, _) => new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "AI 設定有幾個欄位不對。")
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "limits.monthlyTokens", "要是 1～100000000000 之間的整數，或留空代表不限。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(new { enabled = false, limits = new { dailyRequestsPerPerson = (int?)null, monthlyRequests = (int?)null, monthlyTokens = 0L } }));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.SettingsInvalid);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Put_AllowPersonalConnectionsOmitted_KeepsTheStoredValue(bool stored)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiSettings.OnGet = _ => FakeAiSettingsService.StoredSettings() with { AllowPersonalConnections = stored };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(new { enabled = false }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Modules.AiSettings.SaveCalls.Single().Input.AllowPersonalConnections, Is.EqualTo(stored));
        }
    }

    [Test]
    public async Task Put_AllowPersonalConnectionsExplicitFalse_OverridesTheStoredValueWithoutReadingIt()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiSettings.OnGet = _ => FakeAiSettingsService.StoredSettings() with { AllowPersonalConnections = true };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(new { enabled = false, allowPersonalConnections = false }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.AiSettings.SaveCalls.Single().Input.AllowPersonalConnections, Is.False);
            Assert.That(factory.Modules.AiSettings.GetCalls, Is.Empty);
        }
    }

    [TestCase("GET", "/v1/ai/settings")]
    [TestCase("GET", "/v1/ai/options")]
    [TestCase("POST", "/v1/ai/connections")]
    public async Task NotSignedIn_Returns401(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = method == "GET" ? null : ApiRequests.Json(new { }) };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [TestCase("GET", "/v1/ai/settings")]
    [TestCase("PUT", "/v1/ai/settings")]
    [TestCase("GET", "/v1/ai/options")]
    [TestCase("POST", "/v1/ai/connections")]
    [TestCase("PUT", "/v1/ai/connections/11111111-1111-1111-1111-111111111111")]
    [TestCase("POST", "/v1/ai/connections/11111111-1111-1111-1111-111111111111/refresh")]
    [TestCase("DELETE", "/v1/ai/connections/11111111-1111-1111-1111-111111111111")]
    [TestCase("POST", "/v1/ai/models/discover")]
    public async Task AccessToken_EvenWithEveryScope_IsRefusedBecauseTheseRoutesAreWebOnly(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(new HttpMethod(method), route, secret);
        if (method is "POST" or "PUT")
        {
            request.Content = ApiRequests.Json(new { });
        }

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }
}
