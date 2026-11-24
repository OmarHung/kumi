using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>授權伺服器的協定端點：中繼資料、token、動態註冊、撤銷（<c>internal/HANDOFF-mcp.md</c> 4.6）。</summary>
[TestFixture]
public sealed class OAuthProtocolEndpointTests
{
    private static HttpContent Form(params (string Name, string Value)[] fields) =>
        new FormUrlEncodedContent(fields.Select(f => KeyValuePair.Create(f.Name, f.Value)));

    private static HttpContent AuthorizationCodeForm(string code = "the-code") => Form(
        ("grant_type", "authorization_code"),
        ("code", code),
        ("redirect_uri", "http://127.0.0.1:5000/callback"),
        ("client_id", "pmc_client"),
        ("code_verifier", "verifier"),
        ("resource", "http://localhost/mcp"));

    [Test]
    public async Task Metadata_ListsTheEndpointsAndCapabilities()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/.well-known/oauth-authorization-server");
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True, "沒設 PublicHost 時 issuer 來自 Host 標頭，不能讓前面的快取存起來");
            Assert.That(json["issuer"]!.GetValue<string>(), Is.EqualTo("http://localhost"));
            Assert.That(json["authorization_endpoint"]!.GetValue<string>(), Is.EqualTo("http://localhost/oauth/authorize"));
            Assert.That(json["token_endpoint"]!.GetValue<string>(), Is.EqualTo("http://localhost/oauth/token"));
            Assert.That(json["registration_endpoint"]!.GetValue<string>(), Is.EqualTo("http://localhost/oauth/register"));
            Assert.That(json["revocation_endpoint"]!.GetValue<string>(), Is.EqualTo("http://localhost/oauth/revoke"));
            Assert.That(Strings(json["response_types_supported"]), Is.EqualTo(new[] { "code" }));
            Assert.That(Strings(json["grant_types_supported"]), Is.EqualTo(new[] { "authorization_code", "refresh_token" }));
            Assert.That(Strings(json["code_challenge_methods_supported"]), Is.EqualTo(new[] { "S256" }));
            Assert.That(Strings(json["token_endpoint_auth_methods_supported"]), Is.EqualTo(new[] { "none" }));
            Assert.That(Strings(json["revocation_endpoint_auth_methods_supported"]), Is.EqualTo(new[] { "none" }));
            Assert.That(Strings(json["scopes_supported"]), Is.EqualTo(new[] { "items:read", "items:write", "blueprint:read", "blueprint:write" }));
            Assert.That(json["client_id_metadata_document_supported"]!.GetValue<bool>(), Is.True);
            Assert.That(json["authorization_response_iss_parameter_supported"]!.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task Metadata_WithAPublicHost_UsesItForTheIssuer()
    {
        // Arrange：Host 標頭是用戶端自己填的，設了 Security:PublicHost 就不能拿它決定 issuer。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Security:PublicHost"] = "pm.example.com" } });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/.well-known/oauth-authorization-server");
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        // Assert
        Assert.That(json["issuer"]!.GetValue<string>(), Is.EqualTo("https://pm.example.com"));
        Assert.That(json["token_endpoint"]!.GetValue<string>(), Is.EqualTo("https://pm.example.com/oauth/token"));
        Assert.That(response.Headers.CacheControl?.Public, Is.True);
        Assert.That(response.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.FromSeconds(300)));
    }

    [Test]
    public async Task Metadata_IsNotCoveredByTheWebPageFallback_AndUnknownWellKnownIsA404Envelope()
    {
        // Arrange
        using var factory = WebHostingTests.CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var metadata = await client.GetAsync("/.well-known/oauth-authorization-server");
        using var unknown = await client.GetAsync("/.well-known/openid-configuration");

        // Assert
        Assert.That(metadata.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        await ApiRequests.AssertErrorEnvelopeAsync(unknown, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
    }

    [TestCase("/oauth/authorize?response_type=code&client_id=x")]
    [TestCase("/oauth/authorize")]
    public async Task AuthorizePage_IsTheWebAppAndNotAnApi(string route)
    {
        // Arrange
        using var factory = WebHostingTests.CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
    }

    [Test]
    public async Task Token_AuthorizationCode_PassesTheFieldsAndReturnsTheTokenResponse()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/token", AuthorizationCodeForm());
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        // Assert
        var sent = factory.Modules.OAuth.LastExchange!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(response.Headers.Pragma.ToString(), Is.EqualTo("no-cache"));
            Assert.That(json["access_token"]!.GetValue<string>(), Is.EqualTo("pmt_access"));
            Assert.That(json["token_type"]!.GetValue<string>(), Is.EqualTo("Bearer"));
            Assert.That(json["expires_in"]!.GetValue<int>(), Is.EqualTo(3600));
            Assert.That(json["refresh_token"]!.GetValue<string>(), Is.EqualTo("pmr_refresh"));
            Assert.That(json["scope"]!.GetValue<string>(), Is.EqualTo("items:read"));
            Assert.That(sent.Code, Is.EqualTo("the-code"));
            Assert.That(sent.RedirectUri, Is.EqualTo("http://127.0.0.1:5000/callback"));
            Assert.That(sent.ClientId, Is.EqualTo("pmc_client"));
            Assert.That(sent.CodeVerifier, Is.EqualTo("verifier"));
            Assert.That(sent.Resource, Is.EqualTo("http://localhost/mcp"));
            Assert.That(factory.Modules.OAuth.Servers.Single().Resource, Is.EqualTo("http://localhost/mcp"));
        }
    }

    [Test]
    public async Task Token_RefreshToken_PassesTheFieldsAndRotates()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(
            "/oauth/token",
            Form(("grant_type", "refresh_token"), ("refresh_token", "pmr_old"), ("client_id", "pmc_client"), ("scope", "items:read")));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        // Assert
        var sent = factory.Modules.OAuth.LastRefresh!;
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(json["refresh_token"]!.GetValue<string>(), Is.EqualTo("pmr_refresh2"));
        Assert.That((sent.RefreshToken, sent.ClientId, sent.Scope, sent.Resource), Is.EqualTo(("pmr_old", "pmc_client", "items:read", (string?)null)));
    }

    [TestCase(OAuthError.InvalidGrant, HttpStatusCode.BadRequest)]
    [TestCase(OAuthError.InvalidTarget, HttpStatusCode.BadRequest)]
    [TestCase(OAuthError.InvalidRequest, HttpStatusCode.BadRequest)]
    [TestCase(OAuthError.InvalidClient, HttpStatusCode.Unauthorized)]
    [TestCase(OAuthError.TemporarilyUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Token_Failure_IsTheRfcErrorFormatWithTheRightStatus(string error, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.OAuth.OnExchange = _ => new OAuthFailure(error, "說明文字");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/token", AuthorizationCodeForm());
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(status));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True, "失敗的回應也不能被快取");
            Assert.That(json["error"]!.GetValue<string>(), Is.EqualTo(error));
            Assert.That(json["error_description"]!.GetValue<string>(), Is.EqualTo("說明文字"));
            Assert.That(json.ContainsKey("success"), Is.False, "不是 API 的信封");
        }
    }

    [Test]
    public async Task Token_UnknownGrantType_IsUnsupportedGrantType()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/token", Form(("grant_type", "password"), ("username", "a")));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(json["error"]!.GetValue<string>(), Is.EqualTo(OAuthError.UnsupportedGrantType));
        Assert.That(factory.Modules.OAuth.LastExchange, Is.Null);
    }

    [Test]
    public async Task Token_WithoutGrantType_IsInvalidRequest()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/token", Form(("client_id", "x")));

        // Assert
        Assert.That(JsonNode.Parse(await response.Content.ReadAsStringAsync())!["error"]!.GetValue<string>(), Is.EqualTo(OAuthError.InvalidRequest));
    }

    [Test]
    public async Task Token_JsonBody_IsInvalidRequest()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/token", ApiRequests.Json(new { grant_type = "authorization_code" }));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(JsonNode.Parse(await response.Content.ReadAsStringAsync())!["error"]!.GetValue<string>(), Is.EqualTo(OAuthError.InvalidRequest));
    }

    [Test]
    public async Task Token_RepeatedParameter_IsInvalidRequest()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        using var content = new StringContent("grant_type=authorization_code&code=a&code=b", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        // Act
        using var response = await client.PostAsync("/oauth/token", content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(factory.Modules.OAuth.LastExchange, Is.Null);
    }

    [Test]
    public async Task Token_OversizedBody_IsRejected()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        using var content = new StringContent("grant_type=authorization_code&code=" + new string('a', 20_000), System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        // Act
        using var response = await client.PostAsync("/oauth/token", content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(factory.Modules.OAuth.LastExchange, Is.Null);
    }

    [Test]
    public async Task Token_GetIsNotAllowed_AndNeedsNoCsrfHeaderNorLogin()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var get = await client.GetAsync("/oauth/token");
        using var post = await client.PostAsync("/oauth/token", AuthorizationCodeForm());

        // Assert
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Token_RequestsBeyondTheLimit_Are429PerSourceAddress()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.OAuthTokenPermitsPerWindow + 1; i++)
        {
            using var response = await client.PostAsync("/oauth/token", AuthorizationCodeForm());
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses.Take(ApiLimits.OAuthTokenPermitsPerWindow), Has.All.EqualTo(HttpStatusCode.OK));
        Assert.That(statuses.Last(), Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task Register_ReturnsTheClientWithoutASecret()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var body = new
        {
            client_name = "範例代理",
            redirect_uris = new[] { "http://127.0.0.1:5000/callback" },
            token_endpoint_auth_method = "none",
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            application_type = "native",
            scope = "ignored extra field",
        };

        // Act
        using var response = await client.PostAsync("/oauth/register", ApiRequests.Json(body));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

        // Assert
        var sent = factory.Modules.OAuth.LastRegistration!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(json["client_id"]!.GetValue<string>(), Is.EqualTo("pmc_fake"));
            Assert.That(json["client_id_issued_at"]!.GetValue<long>(), Is.EqualTo(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()));
            Assert.That(json["client_name"]!.GetValue<string>(), Is.EqualTo("範例代理"));
            Assert.That(Strings(json["redirect_uris"]), Is.EqualTo(new[] { "http://127.0.0.1:5000/callback" }));
            Assert.That(json["token_endpoint_auth_method"]!.GetValue<string>(), Is.EqualTo("none"));
            Assert.That(json.ContainsKey("client_secret"), Is.False);
            Assert.That(sent.ClientName, Is.EqualTo("範例代理"));
            Assert.That(sent.GrantTypes, Is.EqualTo(new[] { "authorization_code", "refresh_token" }));
        }
    }

    [TestCase("not json")]
    [TestCase("[1]")]
    [TestCase("{\"client_name\": 5}")]
    [TestCase("{\"redirect_uris\": \"http://127.0.0.1/cb\"}")]
    [TestCase("{\"redirect_uris\": [1]}")]
    public async Task Register_MalformedBody_IsInvalidClientMetadata(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/register", ApiRequests.Document(body));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(json["error"]!.GetValue<string>(), Is.EqualTo(OAuthError.InvalidClientMetadata));
        Assert.That(factory.Modules.OAuth.LastRegistration, Is.Null);
    }

    [Test]
    public async Task Register_ModuleRejects_IsAnOAuthError()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.OAuth.OnRegister = _ => new OAuthFailure(OAuthError.InvalidRedirectUri, "網址不合規定");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/register", ApiRequests.Json(new { client_name = "x", redirect_uris = new[] { "ftp://x" } }));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(json["error"]!.GetValue<string>(), Is.EqualTo(OAuthError.InvalidRedirectUri));
        Assert.That(json["error_description"]!.GetValue<string>(), Is.EqualTo("網址不合規定"));
    }

    [Test]
    public async Task Register_MoreThanTenPerHour_Is429()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.OAuthRegisterPermitsPerHour + 1; i++)
        {
            using var response = await client.PostAsync("/oauth/register", ApiRequests.Json(new { client_name = "x", redirect_uris = new[] { "http://127.0.0.1/cb" } }));
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses.Take(ApiLimits.OAuthRegisterPermitsPerHour), Has.All.EqualTo(HttpStatusCode.Created));
        Assert.That(statuses.Last(), Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [TestCase("pmr_anything")]
    [TestCase("pmt_unknown")]
    [TestCase("garbage")]
    public async Task Revoke_AlwaysAnswers200_AndPassesTheTokenOn(string token)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/revoke", Form(("token", token)));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.OAuth.RevokedTokens, Is.EqualTo(new[] { token }));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task Revoke_WithoutAToken_Still200AndRevokesNothing()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/oauth/revoke", Form(("other", "x")));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.OAuth.RevokedTokens, Is.Empty);
    }

    private static string[] Strings(JsonNode? node) => [.. node!.AsArray().Select(n => n!.GetValue<string>())];
}
