using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>命令列瀏覽器登入的三個端點（<c>internal/HANDOFF-setup-wizard.md</c> 3.3）：check、approve 只能用 Cookie，token 不用登入。</summary>
[TestFixture]
public sealed class CliLoginEndpointTests
{
    private const string Check = "/v1/cli-login/check";
    private const string Approve = "/v1/cli-login/approve";
    private const string Token = "/v1/cli-login/token";

    private static object Body(string[]? tokens = null, string[]? scopes = null, string[]? granted = null, int? expiresInDays = 90) => new
    {
        port = 53682,
        state = "abcdefghijklmnopqrstuv",
        challenge = new string('c', 43),
        machine = "MacBook-Pro",
        tokens = tokens ?? ["cli", "agent"],
        scopes = scopes ?? ["items:read", "blueprint:read"],
        grantedScopes = granted ?? ["items:read"],
        expiresInDays,
    };

    private static AiOptions Options(bool canAddPersonal) => new(false, [], null) { CanAddPersonal = canAddPersonal };

    [Test]
    public async Task Check_ReturnsTheRequestTheAgentScopesAndWhetherTheAgentIsAllowed()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiOptions.OnGet = _ => Options(canAddPersonal: true);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(Body()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var sent = factory.Modules.CliLogin.Checks.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(data?["machineName"]?.GetValue<string>(), Is.EqualTo("MacBook-Pro"));
            Assert.That(Strings(data?["tokens"]), Is.EqualTo(new[] { "cli", "agent" }));
            Assert.That(Strings(data?["scopes"]), Is.EqualTo(new[] { "items:read", "blueprint:read" }));
            Assert.That(Strings(data?["agentScopes"]), Is.EqualTo(new[] { "agent:run", "blueprint:read", "blueprint:write", "items:read" }));
            Assert.That(data?["agentAllowed"]?.GetValue<bool>(), Is.True);
            Assert.That(data?["account"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.DisplayName));
            Assert.That(data?["account"]?["email"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.Email));
            Assert.That(data?["account"]?["organizationName"]?.GetValue<string>(), Is.EqualTo(TestUsers.OrganizationName));
            Assert.That(sent.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(sent.Parameters.Port, Is.EqualTo(53682));
            Assert.That(sent.Parameters.Tokens, Is.EqualTo(new[] { CliLoginTokenKind.Cli, CliLoginTokenKind.Agent }));
            Assert.That(sent.Parameters.Scopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead }));
        }
    }

    [Test]
    public async Task Check_WhenTheOrganizationDoesNotAllowPersonalAi_SaysTheAgentIsNotAllowed()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiOptions.OnGet = _ => Options(canAddPersonal: false);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(Body()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["agentAllowed"]?.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task Check_ModuleRejects_Is422WithTheCodeAndTheFieldPath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.CliLogin.OnCheck = _ => new Failure(FailureKind.Invalid, IdentityErrorCodes.CliLoginRequestInvalid, "連接埠不對。")
        {
            Details = [new FailureDetail(IdentityErrorCodes.CliLoginRequestInvalid, "port", "連接埠不對。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(Body()));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, (HttpStatusCode)422, "cli_login.request_invalid");
        Assert.That(details[0]!["path"]!.GetValue<string>(), Is.EqualTo("port"));
    }

    [TestCase("tokens", "everything")]
    [TestCase("scopes", "items:everything")]
    public async Task Check_UnknownTokenKindOrScopeName_Is400(string field, string value)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var body = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(Body()))!;
        body[field] = new JsonArray(value);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(body));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.CliLogin.Checks, Is.Empty);
    }

    [Test]
    public async Task Approve_PassesTheGrantAndReturnsTheRedirect()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiOptions.OnGet = _ => Options(canAddPersonal: true);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Approve, ApiRequests.Json(Body(granted: ["items:read", "items:write"], expiresInDays: 30)));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var sent = factory.Modules.CliLogin.Approvals.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(data?["redirectTo"]?.GetValue<string>(), Is.EqualTo("http://127.0.0.1:53682/kumi-login?code=the-code&state=abcdefghijklmnopqrstuv"));
            Assert.That(sent.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(sent.Grant.CliScopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite }));
            Assert.That(sent.Grant.IncludeAgent, Is.True);
            Assert.That(sent.Grant.ExpiresInDays, Is.EqualTo(30));
        }
    }

    [Test]
    public async Task Approve_WhenThePersonalAiIsNotAllowed_PassesIncludeAgentFalse()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AiOptions.OnGet = _ => Options(canAddPersonal: false);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Approve, ApiRequests.Json(Body()));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.CliLogin.Approvals.Single().Grant.IncludeAgent, Is.False);
    }

    [TestCase(FailureKind.Forbidden, IdentityErrorCodes.CliLoginAgentNotAllowed, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Conflict, IdentityErrorCodes.AccessTokenLimitReached, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Invalid, IdentityErrorCodes.AccessTokenExpiryInvalid, (HttpStatusCode)422)]
    public async Task Approve_ModuleFailures_MapToTheirStatus(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.CliLogin.OnApprove = (_, _) => new Failure(kind, code, "失敗。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Approve, ApiRequests.Json(Body()));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Approve_UnknownGrantedScopeName_Is400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Approve, ApiRequests.Json(Body(granted: ["nope"])));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.CliLogin.Approvals, Is.Empty);
    }

    [TestCase(Check)]
    [TestCase(Approve)]
    public async Task CheckAndApprove_WithoutSigningIn_Are401(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync(route, ApiRequests.Json(Body()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [TestCase(Check)]
    [TestCase(Approve)]
    public async Task CheckAndApprove_WithAnAccessToken_AreForbidden_SoATokenCannotMintMoreTokens(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScopes.All.ToArray());
        using var client = factory.CreateClient();
        using var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, route, secret);
        request.Content = ApiRequests.Json(Body());

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    [Test]
    public async Task Approve_MoreThanTenPerWindow_Is429()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.CliLoginApprovePermitsPerWindow + 1; i++)
        {
            using var response = await client.PostAsync(Approve, ApiRequests.Json(Body()));
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses.Take(ApiLimits.CliLoginApprovePermitsPerWindow), Has.All.EqualTo(HttpStatusCode.OK));
        Assert.That(statuses.Last(), Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task Check_MoreThanTwentyPerWindow_Is429()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.CliLoginCheckPermitsPerWindow + 1; i++)
        {
            using var response = await client.PostAsync(Check, ApiRequests.Json(Body()));
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses.Take(ApiLimits.CliLoginCheckPermitsPerWindow), Has.None.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(statuses.Last(), Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task Token_WithoutSigningInOrTheCsrfHeader_ReturnsTheTokensWithTheSecretsOnce()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secrets = factory.Modules.AccessTokens;
        var cli = Created("kumi（MacBook-Pro）", "pmt_cli_secret", [AccessTokenScope.ItemsRead]);
        var agent = Created("本機代理（MacBook-Pro）", "pmt_agent_secret", CliLoginRules.AgentScopes);
        factory.Modules.CliLogin.OnRedeem = _ => new CliLoginTokens(
            new CliLoginAccount("Member Example", "member@example.com", "Example 組織"), cli, agent, AgentSkipped: false);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(
            Token, ApiRequests.Json(new { code = "the-code", codeVerifier = "the-verifier", replaceTokens = new[] { "pmt_old" } }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var sent = factory.Modules.CliLogin.Redemptions.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(secrets, Is.Not.Null);
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(data?["account"]?["email"]?.GetValue<string>(), Is.EqualTo("member@example.com"));
            Assert.That(data?["cli"]?["secret"]?.GetValue<string>(), Is.EqualTo("pmt_cli_secret"));
            Assert.That(data?["cli"]?["token"]?["machineName"]?.GetValue<string>(), Is.EqualTo("MacBook-Pro"));
            Assert.That(data?["cli"]?["token"]?["name"]?.GetValue<string>(), Is.EqualTo("kumi（MacBook-Pro）"));
            Assert.That(Strings(data?["agent"]?["token"]?["scopes"]), Is.EqualTo(new[] { "agent:run", "blueprint:read", "blueprint:write", "items:read" }));
            Assert.That(data?["agent"]?["secret"]?.GetValue<string>(), Is.EqualTo("pmt_agent_secret"));
            Assert.That(data?["agentSkipped"]?.GetValue<bool>(), Is.False);
            Assert.That(sent.Code, Is.EqualTo("the-code"));
            Assert.That(sent.CodeVerifier, Is.EqualTo("the-verifier"));
            Assert.That(sent.ReplaceTokens, Is.EqualTo(new[] { "pmt_old" }));
        }
    }

    [Test]
    public async Task Token_WhenOnlyTheAgentWasSkipped_WritesNullsAndTheFlag()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.CliLogin.OnRedeem = _ => new CliLoginTokens(
            new CliLoginAccount("Member Example", "member@example.com", "Example 組織"),
            Created("kumi（MacBook-Pro）", "pmt_cli_secret", [AccessTokenScope.ItemsRead]), null, AgentSkipped: true);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(Token, ApiRequests.Json(new { code = "c", codeVerifier = "v" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?.AsObject().ContainsKey("agent") == true && data["agent"] is null, Is.True, "agent 要寫出 null");
            Assert.That(data?["agentSkipped"]?.GetValue<bool>(), Is.True);
            Assert.That(factory.Modules.CliLogin.Redemptions.Single().ReplaceTokens, Is.Empty);
        }
    }

    [TestCase(FailureKind.Invalid, IdentityErrorCodes.CliLoginCodeInvalid, (HttpStatusCode)422)]
    [TestCase(FailureKind.Conflict, IdentityErrorCodes.AccessTokenLimitReached, HttpStatusCode.Conflict)]
    public async Task Token_ModuleFailures_MapToTheirStatus(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.CliLogin.OnRedeem = _ => new Failure(kind, code, "失敗。");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(Token, ApiRequests.Json(new { code = "c", codeVerifier = "v" }));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Token_MalformedBody_Is400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(Token, ApiRequests.Json(new { code = "c", extra = 1 }));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.CliLogin.Redemptions, Is.Empty);
    }

    [Test]
    public async Task Token_RequestsBeyondTheLimit_Are429PerSourceAddress()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.CliLoginTokenPermitsPerWindow + 1; i++)
        {
            using var response = await client.PostAsync(Token, ApiRequests.Json(new { code = "c", codeVerifier = "v" }));
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses.Take(ApiLimits.CliLoginTokenPermitsPerWindow), Has.All.EqualTo(HttpStatusCode.OK));
        Assert.That(statuses.Last(), Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    private static CreatedAccessToken Created(string name, string secret, IReadOnlyList<AccessTokenScope> scopes) =>
        new(
            new AccessTokenSummary(Guid.CreateVersion7(), name, secret[..8], scopes, new DateTimeOffset(2026, 11, 24, 1, 0, 0, TimeSpan.Zero), null, null)
            {
                MachineName = "MacBook-Pro",
            },
            secret);

    private static string[] Strings(JsonNode? array) => [.. array!.AsArray().Select(n => n!.GetValue<string>())];
}
