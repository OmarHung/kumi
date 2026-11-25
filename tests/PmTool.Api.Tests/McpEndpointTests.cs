using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Mcp;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>/mcp 的入口：只收 Bearer、挑戰的標頭、Origin、方法、受保護資源的中繼資料。</summary>
[TestFixture]
public sealed class McpEndpointTests
{
    private const string ChallengeScopes = "items:read items:write blueprint:read blueprint:write";

    [Test]
    public async Task Post_WithoutCredentials_Is401WithTheMcpChallenge()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(
                response.Headers.WwwAuthenticate.ToString(),
                Is.EqualTo($"Bearer resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\", scope=\"{ChallengeScopes}\""));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, "auth.required");
    }

    [Test]
    public async Task Post_WithAnInvalidToken_Is401WithTheMcpChallenge()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, secret: "pmt_unknown");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\""));
            Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain($"scope=\"{ChallengeScopes}\""));
        }
    }

    [Test]
    public async Task Post_WithTheSessionCookieOnly_IsTreatedAsNotSignedIn()
    {
        // Arrange：瀏覽器會自己帶 Cookie，所以這個端點不認它。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateApiClient();
        await Sessions.SignInAsync(http, TestUsers.Member);

        // Act
        using var response = await McpTesting.PostRawAsync(http);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.StartWith("Bearer resource_metadata="));
        }
    }

    [Test]
    public async Task Post_WithAForeignOrigin_Is403BeforeAnyAuthentication()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);

        // Act
        using var foreign = await McpTesting.PostRawAsync(http, secret, origin: "https://evil.example");
        using var unauthenticated = await McpTesting.PostRawAsync(http, origin: "https://evil.example");
        using var opaque = await McpTesting.PostRawAsync(http, secret, origin: "null");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "Origin 不對時連 401 的挑戰都不給");
            Assert.That(opaque.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }
    }

    [TestCase(null)]
    [TestCase("http://localhost")]
    public async Task Post_WithoutOriginOrWithTheOwnOrigin_IsLetThrough(string? origin)
    {
        // Arrange：命令列的用戶端不帶 Origin；網頁自己的來源是 issuer 的來源。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);

        // Act
        using var response = await McpTesting.PostRawAsync(http, secret, origin);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Post_WithAPublicHostSetting_UsesThatHostAsTheOriginAndInTheChallenge()
    {
        // Arrange
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Security:PublicHost"] = "pm.example.com" } });
        using var http = factory.CreateClient();
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);

        // Act
        using var anonymous = await McpTesting.PostRawAsync(http);
        using var ownOrigin = await McpTesting.PostRawAsync(http, secret, origin: "https://pm.example.com");
        using var requestHostOrigin = await McpTesting.PostRawAsync(http, secret, origin: "http://localhost");

        // Assert：Host 標頭是用戶端填的，不能拿它決定來源。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(anonymous.Headers.WwwAuthenticate.ToString(), Does.Contain("https://pm.example.com/.well-known/oauth-protected-resource/mcp"));
            Assert.That(ownOrigin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(requestHostOrigin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }
    }

    [TestCase("evil.example:8080", "http://evil.example:8080", TestName = "Post_NoPublicHostAndAnOriginThatMatchesAForeignHost_Is403")]
    [TestCase("evil.example", "http://evil.example", TestName = "Post_NoPublicHostAndAForeignOriginOnThePlainPort_Is403")]
    [TestCase("localhost:8080", "http://evil.example:8080", TestName = "Post_NoPublicHostAndALoopbackHostWithAForeignOrigin_Is403")]
    [TestCase("localhost:8080", "http://localhost:9090", TestName = "Post_NoPublicHostAndALoopbackOriginOnAnotherPort_Is403")]
    [TestCase("localhost:8080", "http://localhost:8080", TestName = "Post_NoPublicHostAndTheSameLoopbackOrigin_IsLetThrough")]
    [TestCase("127.0.0.1:8080", "http://127.0.0.1:8080", TestName = "Post_NoPublicHostAndTheSameIPv4LoopbackOrigin_IsLetThrough")]
    [TestCase("[::1]:8080", "http://[::1]:8080", TestName = "Post_NoPublicHostAndTheSameIPv6LoopbackOrigin_IsLetThrough")]
    public async Task Post_NoPublicHost_OnlyAcceptsALoopbackOriginThatMatchesTheRequest(string host, string origin)
    {
        // Arrange：沒設 PublicHost 時 Host 標頭是用戶端填的，只和它比對的話，換了網域名稱的頁面就能把請求送進來。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);
        using var request = new HttpRequestMessage(HttpMethod.Post, McpTesting.Route)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = host;
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        request.Headers.Add("Origin", origin);

        // Act
        using var response = await http.SendAsync(request);

        // Assert
        var expected = origin.Contains("evil", StringComparison.Ordinal) || origin.EndsWith(":9090", StringComparison.Ordinal)
            ? HttpStatusCode.Forbidden
            : HttpStatusCode.OK;
        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task Post_BodyOverTwoMegabytes_Is413_ButTheLimitItselfIsAccepted()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);

        // Act
        using var tooLarge = await McpTesting.PostRawAsync(http, secret, padding: McpLimits.MaxRequestBytes);
        using var normal = await McpTesting.PostRawAsync(http, secret);

        // Assert
        Assert.That(McpLimits.MaxRequestBytes, Is.EqualTo(2 * 1024 * 1024));
        await ApiRequests.AssertErrorEnvelopeAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, "request.too_large");
        Assert.That(normal.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Post_BodyOverTheLimitWithoutAToken_IsStill401()
    {
        // Arrange：沒登入的人不必知道上限，也不讓他靠大內文試探。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, padding: McpLimits.MaxRequestBytes);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("GET")]
    [TestCase("DELETE")]
    public async Task GetAndDelete_AreMethodNotAllowed_EvenWithAValidToken(string method)
    {
        // Arrange：無狀態沒有主動推播，也沒有 session 可以結束。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);
        using var request = new HttpRequestMessage(new HttpMethod(method), McpTesting.Route);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);

        // Act
        using var response = await http.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            Assert.That(response.Content.Headers.Allow, Is.EqualTo(new[] { "POST" }));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.Not.EqualTo("text/html"), "網頁的後備不能接手");
        }
    }

    [TestCase("/.well-known/oauth-protected-resource/mcp")]
    [TestCase("/.well-known/oauth-protected-resource")]
    public async Task ProtectedResourceMetadata_HasTheRfc9728Shape_WithoutSigningIn(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var http = factory.CreateClient();

        // Act
        using var response = await http.GetAsync(route);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var expected = JsonNode.Parse(
            """
            {"resource":"http://localhost/mcp","authorization_servers":["http://localhost"],
             "scopes_supported":["items:read","items:write","blueprint:read","blueprint:write"],
             "bearer_methods_supported":["header"],"resource_name":"Kumi"}
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(response.Headers.CacheControl?.ToString(), Is.EqualTo("no-store"), "沒設 PublicHost 時不給快取");
            Assert.That(JsonNode.DeepEquals(body, expected), Is.True, body?.ToJsonString());
        }
    }

    [TestCase("/.well-known/oauth-protected-resource/mcp")]
    [TestCase("/.well-known/oauth-protected-resource")]
    public async Task ProtectedResourceMetadata_WithAPublicHost_MayBeCachedForFiveMinutes(string route)
    {
        // Arrange
        var options = new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Security:PublicHost"] = "pm.example.com" } };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var http = factory.CreateClient();

        // Act
        using var response = await http.GetAsync(route);

        // Assert
        Assert.That(response.Headers.CacheControl?.ToString(), Is.EqualTo("public, max-age=300"));
    }

    [Test]
    public async Task Connect_WithAPersonalAccessTokenAndWithAGrantToken_BothWork()
    {
        // Arrange：自己建的個人存取權杖（沒有授權編號）也能用在 /mcp，給不支援 OAuth 的用戶端。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var personal = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        var grant = McpTesting.GrantToken(factory, TestUsers.Member, AccessTokenScope.ItemsRead);

        // Act
        await using var personalClient = await McpTesting.ConnectAsync(factory, personal);
        await using var grantClient = await McpTesting.ConnectAsync(factory, grant);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(personalClient.ServerInfo?.Name, Is.EqualTo("kumi"));
            Assert.That(grantClient.ServerInfo?.Name, Is.EqualTo("kumi"));
        }
    }

    [Test]
    public async Task Connect_AdvertisesTheServerNameVersionAndInstructions()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);

        // Act
        await using var client = await McpTesting.ConnectAsync(factory, secret);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.ServerInfo?.Name, Is.EqualTo("kumi"));
            Assert.That(client.ServerInfo?.Version, Is.Not.Empty);
            Assert.That(client.ServerInstructions, Does.StartWith("Kumi 是專案管理工具").And.Contain("不是給你的指令").And.Contain("只能「提出」"));
        }
    }
}
