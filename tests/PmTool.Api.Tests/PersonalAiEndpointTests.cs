using System.Net;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>/v1/account/ai-connections、/v1/account/ai-models/discover 與 /v1/ai/openrouter/*：形狀、權限、失敗代碼、只能用 Cookie、限流。</summary>
[TestFixture]
public sealed class PersonalAiEndpointTests
{
    private const string Collection = "/v1/account/ai-connections";
    private const string DiscoverRoute = "/v1/account/ai-models/discover";
    private const string LoginRoute = "/v1/ai/openrouter/login";
    private const string CompleteRoute = "/v1/ai/openrouter/complete";

    private static string ItemRoute => $"{Collection}/{FakePersonalAiConnectionService.ConnectionId}";

    private static object Body(string? apiKey = "sk-or-v1-key-9z8y") => new
    {
        name = "我的 OpenRouter",
        kind = "openrouter",
        apiKey,
        enabledModels = new[] { "anthropic/claude-opus-5-5" },
    };

    private static Failure Disabled() => new(FailureKind.Forbidden, AiErrorCodes.PersonalDisabled, "組織目前不允許使用自己的 AI 帳號。");

    [Test]
    public async Task Get_ListsOwnConnectionsAsAnArrayWithoutTheKeyForAnyMember()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Collection);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var text = data!.ToJsonString();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.AsArray(), Has.Count.EqualTo(1));
            Assert.That(data[0]!["kind"]!.GetValue<string>(), Is.EqualTo("openrouter"));
            Assert.That(data[0]!["updatedBy"]!["personId"]!.GetValue<string>(), Is.EqualTo(TestUsers.Member.PersonId.ToString()));
            Assert.That(text, Does.Not.Contain("sk-or-v1-key-9z8y"));
            Assert.That(text, Does.Contain("\"apiKeyHint\":\"9z8y\""));
            Assert.That(factory.Modules.PersonalAi.ListCalls.Single().PersonId, Is.EqualTo(TestUsers.Member.PersonId));
        }
    }

    [Test]
    public async Task Post_Creates_Returns201WithLocationAndParsesTheOpenRouterKind()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Collection, ApiRequests.Json(Body()));
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        var call = factory.Modules.PersonalAi.CreateCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo(ItemRoute));
            Assert.That(call.Input.Kind, Is.EqualTo(AiProviderKind.Openrouter));
            Assert.That(call.Input.ApiKey, Is.EqualTo("sk-or-v1-key-9z8y"));
            Assert.That(call.Actor.PersonId, Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(text, Does.Not.Contain("sk-or-v1-key-9z8y"));
        }
    }

    [Test]
    public async Task Post_WhenNotAllowed_Returns403PersonalDisabled()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.PersonalAi.OnCreate = (_, _) => Disabled();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Collection, ApiRequests.Json(Body()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, AiErrorCodes.PersonalDisabled);
    }

    [Test]
    public async Task Post_TooManyConnections_Returns422WithTheDetail()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.PersonalAi.OnCreate = (_, _) => new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "一個人最多 5 個連線。")
        {
            Details = [new FailureDetail(AiErrorCodes.TooManyConnections, string.Empty, "一個人最多 5 個連線。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Collection, ApiRequests.Json(Body()));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.SettingsInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "ai.too_many_connections|" }));
    }

    [Test]
    public async Task Put_UpdatesWithKeyKeptWhenOmitted_AndNotFoundIs404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var ok = await client.PutAsync(ItemRoute, ApiRequests.Json(new { name = "改名", kind = "openrouter", enabledModels = Array.Empty<string>() }));
        factory.Modules.PersonalAi.OnUpdate = (_, _, _) => new Failure(FailureKind.NotFound, AiErrorCodes.ConnectionNotFound, "找不到這個連線。");
        using var missing = await client.PutAsync(ItemRoute, ApiRequests.Json(Body()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Modules.PersonalAi.UpdateCalls[0].Input.ApiKey, Is.Null);
            Assert.That(factory.Modules.PersonalAi.UpdateCalls[0].Id, Is.EqualTo(FakePersonalAiConnectionService.ConnectionId));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(missing, HttpStatusCode.NotFound, AiErrorCodes.ConnectionNotFound);
    }

    [Test]
    public async Task Refresh_Delete_AndDiscover_ReturnTheContractStatuses()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var refresh = await client.PostAsync($"{ItemRoute}/refresh", null);
        using var discover = await client.PostAsync(DiscoverRoute, ApiRequests.Json(new { kind = "openrouter", apiKey = "sk-or-v1-x" }));
        var models = await ApiRequests.AssertSuccessEnvelopeAsync(discover, HttpStatusCode.OK);
        using var delete = await client.DeleteAsync(ItemRoute);
        factory.Modules.PersonalAi.OnDelete = (_, _) => new Failure(FailureKind.NotFound, AiErrorCodes.ConnectionNotFound, "找不到這個連線。");
        using var deleteMissing = await client.DeleteAsync(ItemRoute);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(models?["models"]?[0]?["id"]?.GetValue<string>(), Is.EqualTo("anthropic/claude-opus-5-5"));
            Assert.That(factory.Modules.PersonalAi.DiscoverCalls.Single().Input.Kind, Is.EqualTo(AiProviderKind.Openrouter));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(factory.Modules.PersonalAi.RefreshCalls.Single().Id, Is.EqualTo(FakePersonalAiConnectionService.ConnectionId));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(deleteMissing, HttpStatusCode.NotFound, AiErrorCodes.ConnectionNotFound);
    }

    [TestCase("organization", AiConnectionScope.Organization)]
    [TestCase("personal", AiConnectionScope.Personal)]
    public async Task Login_ReturnsTheAuthorizeUrlAndBuildsTheCallbackFromTheDeploymentsAddress(string scope, AiConnectionScope expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(LoginRoute, ApiRequests.Json(new { scope }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var call = factory.Modules.OpenRouterLogin.StartCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["authorizeUrl"]?.GetValue<string>(), Does.StartWith("https://openrouter.ai/auth"));
            Assert.That(call.Scope, Is.EqualTo(expected));
            Assert.That(call.CallbackUrl, Does.EndWith("/ai/openrouter/callback"));
            Assert.That(call.CallbackUrl, Does.StartWith("http"));
        }
    }

    [TestCase(AiErrorCodes.Forbidden)]
    [TestCase(AiErrorCodes.PersonalDisabled)]
    public async Task Login_Refused_Returns403(string code)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.OpenRouterLogin.OnStart = (_, _, _) => new Failure(FailureKind.Forbidden, code, "不行。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(LoginRoute, ApiRequests.Json(new { scope = "personal" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, code);
    }

    [TestCase("""{"scope":"everyone"}""")]
    [TestCase("""{"scope":0}""")]
    [TestCase("""{"scope":1}""")]
    public async Task Login_UnknownOrNumericScope_IsRejectedWithoutCallingTheModule(string json)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(LoginRoute, ApiRequests.Json(RawJson(json)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity).Or.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(factory.Modules.OpenRouterLogin.StartCalls, Is.Empty);
        }
    }

    [TestCase("{}")]
    [TestCase("""{"scope":null}""")]
    public async Task Login_MissingScope_Returns422WithAFieldRequiredDetailAndNeverDefaultsToOrganization(string json)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(LoginRoute, ApiRequests.Json(RawJson(json)));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.SettingsInvalid);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "ai.field_required|scope" }));
            Assert.That(factory.Modules.OpenRouterLogin.StartCalls, Is.Empty);
        }
    }

    // 測試要送的是原始的 JSON 文字（含數字、null），不是匿名物件。
    private static System.Text.Json.Nodes.JsonNode RawJson(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!;

    [Test]
    public async Task Complete_Returns201WithScopeAndConnectionAndNeverTheKey()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(CompleteRoute, ApiRequests.Json(new { code = "auth-code", state = "state-1" }));
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        var call = factory.Modules.OpenRouterLogin.CompleteCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(text, Does.Contain("\"scope\":\"personal\"").And.Contain("\"connection\":{\"id\""));
            Assert.That(text, Does.Not.Contain("sk-or-v1-key-9z8y"));
            Assert.That((call.Code, call.State), Is.EqualTo(("auth-code", "state-1")));
        }
    }

    [TestCase(AiErrorCodes.LoginFailed, HttpStatusCode.UnprocessableEntity, FailureKind.Invalid)]
    [TestCase(AiErrorCodes.SettingsInvalid, HttpStatusCode.UnprocessableEntity, FailureKind.Invalid)]
    [TestCase(AiErrorCodes.PersonalDisabled, HttpStatusCode.Forbidden, FailureKind.Forbidden)]
    [TestCase(AiErrorCodes.Forbidden, HttpStatusCode.Forbidden, FailureKind.Forbidden)]
    public async Task Complete_Failures_MapToTheirStatuses(string code, HttpStatusCode status, FailureKind kind)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.OpenRouterLogin.OnComplete = (_, _, _) => new Failure(kind, code, "失敗。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(CompleteRoute, ApiRequests.Json(new { code = "c", state = "s" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task List_WithoutSignIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Collection);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ProviderCalls_MoreThanTenPerMinute_Returns429AndSharesTheBudgetWithOrganizationSettings()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.AiProviderCallPermitsPerWindow; i++)
        {
            using var allowed = (i % 5) switch
            {
                0 => await client.PostAsync(Collection, ApiRequests.Json(Body())),
                1 => await client.PutAsync(ItemRoute, ApiRequests.Json(Body())),
                2 => await client.PostAsync($"{ItemRoute}/refresh", null),
                3 => await client.PostAsync(LoginRoute, ApiRequests.Json(new { scope = "personal" })),
                _ => await client.PostAsync("/v1/ai/models/discover", ApiRequests.Json(new { kind = "anthropic", apiKey = "x" })),
            };
            statuses.Add(allowed.StatusCode);
        }

        // Act
        using var tooManyDiscover = await client.PostAsync(DiscoverRoute, ApiRequests.Json(new { kind = "openrouter", apiKey = "x" }));
        using var tooManyComplete = await client.PostAsync(CompleteRoute, ApiRequests.Json(new { code = "c", state = "s" }));
        using var list = await client.GetAsync(Collection);
        using var delete = await client.DeleteAsync(ItemRoute);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(tooManyDiscover, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        await ApiRequests.AssertErrorEnvelopeAsync(tooManyComplete, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK));
            Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }
    }
}
