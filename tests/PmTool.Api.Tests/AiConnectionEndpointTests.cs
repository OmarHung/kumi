using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>/v1/ai/connections 與 /v1/ai/models/discover：本文、金鑰的三種寫法、狀態碼、失敗、限流。</summary>
[TestFixture]
public sealed class AiConnectionEndpointTests
{
    private const string Collection = "/v1/ai/connections";
    private const string DiscoverRoute = "/v1/ai/models/discover";

    private static void AssertInput(
        AiConnectionInput input, string name, AiProviderKind kind, string? baseUrl, string? apiKey, string[] enabled, string[] custom)
    {
        Assert.That(input.Name, Is.EqualTo(name));
        Assert.That(input.Kind, Is.EqualTo(kind));
        Assert.That(input.BaseUrl, Is.EqualTo(baseUrl));
        Assert.That(input.ApiKey, Is.EqualTo(apiKey));
        Assert.That(input.EnabledModels, Is.EqualTo(enabled));
        Assert.That(input.CustomModels, Is.EqualTo(custom));
    }

    private static string ItemRoute => $"{Collection}/{FakeAiSettingsService.ConnectionId}";

    private static object AnthropicBody(string? apiKey = "sk-ant-key-a1b2") => new
    {
        name = "Anthropic",
        kind = "anthropic",
        apiKey,
        enabledModels = new[] { "claude-opus-5-5" },
    };

    [Test]
    public async Task Post_Creates_Returns201WithLocationAndTheConnectionWithoutTheKey()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Collection, ApiRequests.Json(AnthropicBody()));
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        var call = factory.Modules.AiSettings.CreateCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/ai/connections/{FakeAiSettingsService.ConnectionId}"));
            AssertInput(call.Input, "Anthropic", AiProviderKind.Anthropic, null, "sk-ant-key-a1b2", ["claude-opus-5-5"], []);
            Assert.That(text, Does.Not.Contain("sk-ant-key-a1b2"));
            Assert.That(text, Does.Contain("\"hasApiKey\":true").And.Contain("\"apiKeyHint\":\"a1b2\"").And.Contain("\"updatedBy\":{\"personId\""));
        }
    }

    [Test]
    public async Task Post_CompatibleEndpointWithSnakeCaseKindAndCustomModels_ParsesEverything()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = new
        {
            name = "Ollama",
            kind = "openai_compatible",
            baseUrl = "http://ollama.example.com:11434/v1",
            enabledModels = new[] { "llama3" },
            customModels = new[] { "llama3" },
        };

        // Act
        using var response = await client.PostAsync(Collection, ApiRequests.Json(body));

        // Assert
        var input = factory.Modules.AiSettings.CreateCalls.Single().Input;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            AssertInput(input, "Ollama", AiProviderKind.OpenaiCompatible, "http://ollama.example.com:11434/v1", null, ["llama3"], ["llama3"]);
        }
    }

    [TestCase("""{"name":"A","kind":"anthropic","enabledModels":[]}""", null, TestName = "Put_ApiKeyOmitted_MeansKeepTheStoredOne")]
    [TestCase("""{"name":"A","kind":"anthropic","enabledModels":[],"apiKey":null}""", null, TestName = "Put_ApiKeyNull_MeansKeepTheStoredOne")]
    [TestCase("""{"name":"A","kind":"anthropic","enabledModels":[],"apiKey":""}""", "", TestName = "Put_ApiKeyEmpty_MeansClearIt")]
    [TestCase("""{"name":"A","kind":"anthropic","enabledModels":[],"apiKey":"sk-new"}""", "sk-new", TestName = "Put_ApiKeyGiven_MeansReplaceIt")]
    public async Task Put_ApiKey_IsPassedOnExactlyAsSent(string body, string? expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(ItemRoute, ApiRequests.Document(body));

        // Assert
        var call = factory.Modules.AiSettings.UpdateCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(call.Id, Is.EqualTo(FakeAiSettingsService.ConnectionId));
            Assert.That(call.Input.ApiKey, Is.EqualTo(expected));
            Assert.That(call.Input.CustomModels, Is.Empty);
        }
    }

    [TestCase("""{"kind":"anthropic","enabledModels":[]}""", "", TestName = "Post_NameMissing_Returns400")]
    [TestCase("""{"name":"A","enabledModels":[]}""", "", TestName = "Post_KindMissing_Returns400")]
    [TestCase("""{"name":"A","kind":"anthropic"}""", "", TestName = "Post_EnabledModelsMissing_Returns400")]
    [TestCase("""{"name":"A","kind":"azure","enabledModels":[]}""", "kind", TestName = "Post_UnknownKind_Returns400")]
    [TestCase("""{"name":"A","kind":"openai-compatible","enabledModels":[]}""", "kind", TestName = "Post_KindWithADash_Returns400")]
    [TestCase("""{"name":"A","kind":"anthropic","enabledModels":[],"apiKey":5}""", "apiKey", TestName = "Post_ApiKeyNotAString_Returns400")]
    [TestCase("""{"name":"A","kind":"anthropic","enabledModels":[],"hasApiKey":true}""", "hasApiKey", TestName = "Post_UnknownProperty_Returns400")]
    public async Task Post_BadBody_Returns400WithoutCallingTheModule(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Collection, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details, Has.Count.EqualTo(1));
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo(path).Or.Contain(path));
            Assert.That(factory.Modules.AiSettings.CreateCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Post_FieldsRejected_Returns422WithDetails()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiSettings.OnCreate = (_, _) => new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "連線的設定有幾個欄位不對。")
        {
            Details =
            [
                new FailureDetail(AiErrorCodes.FieldInvalid, "name", "已經有同名的連線。"),
                new FailureDetail(AiErrorCodes.EndpointNotAllowed, "baseUrl", "這個位址是內網或本機。"),
                new FailureDetail(AiErrorCodes.TooManyConnections, string.Empty, "一個組織最多 20 個連線。"),
            ],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Collection, ApiRequests.Json(AnthropicBody()));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.SettingsInvalid);

        // Assert
        Assert.That(
            ApiRequests.Describe(details),
            Is.EqualTo(new[] { "ai.field_invalid|name", "ai.endpoint_not_allowed|baseUrl", "ai.too_many_connections|" }));
    }

    [TestCase("POST", Collection)]
    [TestCase("PUT", "/v1/ai/connections/11111111-1111-1111-1111-111111111111")]
    [TestCase("POST", "/v1/ai/connections/11111111-1111-1111-1111-111111111111/refresh")]
    [TestCase("POST", DiscoverRoute)]
    public async Task ProviderFailed_Returns422WithTheReasonInOurWords(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failure = new Failure(FailureKind.Invalid, AiErrorCodes.ProviderFailed, "金鑰不對，或這把金鑰沒有權限。");
        var ai = factory.Modules.AiSettings;
        ai.OnCreate = (_, _) => failure;
        ai.OnUpdate = (_, _, _) => failure;
        ai.OnRefresh = (_, _) => failure;
        ai.OnDiscover = (_, _) => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = route == DiscoverRoute ? ApiRequests.Json(new { kind = "anthropic", apiKey = "x" }) : method == "POST" && route.EndsWith("refresh", StringComparison.Ordinal) ? null : ApiRequests.Json(AnthropicBody()),
        };

        // Act
        using var response = await client.SendAsync(request);
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(envelope["error"]?["code"]?.GetValue<string>(), Is.EqualTo(AiErrorCodes.ProviderFailed));
            Assert.That(envelope["error"]?["message"]?.GetValue<string>(), Is.EqualTo("金鑰不對，或這把金鑰沒有權限。"));
        }
    }

    [TestCase("PUT")]
    [TestCase("POST")]
    public async Task ConnectionChangedMeanwhile_Returns409WithTheConflictCode(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var conflict = new Failure(FailureKind.Conflict, AiErrorCodes.SettingsConflict, "這個連線剛被別人改過，請重新整理再試。");
        var ai = factory.Modules.AiSettings;
        ai.OnUpdate = (_, _, _) => conflict;
        ai.OnRefresh = (_, _) => conflict;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var route = method == "POST" ? $"{ItemRoute}/refresh" : ItemRoute;
        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = method == "PUT" ? ApiRequests.Json(AnthropicBody()) : null };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.Conflict, AiErrorCodes.SettingsConflict);
    }

    [TestCase("PUT")]
    [TestCase("POST")]
    [TestCase("DELETE")]
    public async Task UnknownConnection_Returns404WithTheConnectionCode(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var notFound = new Failure(FailureKind.NotFound, AiErrorCodes.ConnectionNotFound, "找不到這個連線。");
        var ai = factory.Modules.AiSettings;
        ai.OnUpdate = (_, _, _) => notFound;
        ai.OnRefresh = (_, _) => notFound;
        ai.OnDelete = (_, _) => notFound;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var route = method == "POST" ? $"{ItemRoute}/refresh" : ItemRoute;
        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = method == "PUT" ? ApiRequests.Json(AnthropicBody()) : null };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, AiErrorCodes.ConnectionNotFound);
    }

    [Test]
    public async Task Refresh_Returns200WithTheConnection()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync($"{ItemRoute}/refresh", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.AiSettings.RefreshCalls.Single().Id, Is.EqualTo(FakeAiSettingsService.ConnectionId));
            Assert.That(data?["id"]?.GetValue<string>(), Is.EqualTo(FakeAiSettingsService.ConnectionId.ToString()));
        }
    }

    [Test]
    public async Task Delete_Returns204WithNoBody()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.DeleteAsync(ItemRoute);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
            Assert.That(factory.Modules.AiSettings.DeleteCalls.Single().Id, Is.EqualTo(FakeAiSettingsService.ConnectionId));
        }
    }

    [Test]
    public async Task ConnectionIdThatIsNotAGuid_Returns404BeforeTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.DeleteAsync($"{Collection}/not-a-guid");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(factory.Modules.AiSettings.DeleteCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Discover_ReturnsTheModelsWithEffortsAndPassesTheFieldsAsSent()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = new { kind = "openai_compatible", baseUrl = "http://ollama.example.com/v1", apiKey = (string?)null, connectionId = FakeAiSettingsService.ConnectionId };

        // Act
        using var response = await client.PostAsync(DiscoverRoute, ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse("""{"models":[{"id":"claude-opus-5-5","displayName":"Claude Opus 5.5","efforts":["low","high","max"]}]}""");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(
                factory.Modules.AiSettings.DiscoverCalls.Single().Input,
                Is.EqualTo(new AiDiscoveryInput(AiProviderKind.OpenaiCompatible, "http://ollama.example.com/v1", null, FakeAiSettingsService.ConnectionId)));
        }
    }

    [Test]
    public async Task Discover_OnlyKindIsRequired()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(DiscoverRoute, ApiRequests.Json(new { kind = "google", apiKey = "AIza-x" }));

        // Assert
        Assert.That(factory.Modules.AiSettings.DiscoverCalls.Single().Input, Is.EqualTo(new AiDiscoveryInput(AiProviderKind.Google, null, "AIza-x", null)));
    }

    [Test]
    public async Task Discover_UnknownConnectionId_Returns404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiSettings.OnDiscover = (_, _) => new Failure(FailureKind.NotFound, AiErrorCodes.ConnectionNotFound, "找不到這個連線。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(DiscoverRoute, ApiRequests.Json(new { kind = "anthropic", connectionId = Guid.CreateVersion7() }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, AiErrorCodes.ConnectionNotFound);
    }

    [Test]
    public async Task ProviderCalls_MoreThanTenPerMinute_Returns429ForThatPersonOnlyAndSharesOneBudget()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var owner = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var other = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.AiProviderCallPermitsPerWindow; i++)
        {
            // 四種會連供應商的端點輪流用：同一份額度。
            using var allowed = (i % 4) switch
            {
                0 => await owner.PostAsync(Collection, ApiRequests.Json(AnthropicBody())),
                1 => await owner.PutAsync(ItemRoute, ApiRequests.Json(AnthropicBody())),
                2 => await owner.PostAsync($"{ItemRoute}/refresh", null),
                _ => await owner.PostAsync(DiscoverRoute, ApiRequests.Json(new { kind = "anthropic", apiKey = "x" })),
            };
            statuses.Add(allowed.StatusCode);
        }

        // Act
        using var tooMany = await owner.PostAsync(DiscoverRoute, ApiRequests.Json(new { kind = "anthropic", apiKey = "x" }));
        using var otherPerson = await other.PostAsync(DiscoverRoute, ApiRequests.Json(new { kind = "anthropic", apiKey = "x" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(tooMany, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK));
            Assert.That(otherPerson.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Modules.AiSettings.DiscoverCalls, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task Limit_DoesNotApplyToReadingSavingDeletingOrOptions()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        for (var i = 0; i <= ApiLimits.AiProviderCallPermitsPerWindow; i++)
        {
            using var discover = await client.PostAsync(DiscoverRoute, ApiRequests.Json(new { kind = "anthropic", apiKey = "x" }));
        }

        // Act
        using var get = await client.GetAsync("/v1/ai/settings");
        using var save = await client.PutAsync("/v1/ai/settings", ApiRequests.Json(new { enabled = false }));
        using var options = await client.GetAsync("/v1/ai/options");
        using var delete = await client.DeleteAsync(ItemRoute);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(options.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }
    }

    [Test]
    public async Task Request_ToStringNeverShowsTheKey()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var response = await client.PostAsync(Collection, ApiRequests.Json(AnthropicBody("sk-ant-key-a1b2")));

        // Act
        var logged = factory.Modules.AiSettings.CreateCalls.Single().Input.ToString();

        // Assert
        Assert.That(logged, Does.Not.Contain("sk-ant-key-a1b2"));
    }
}
