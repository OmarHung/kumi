using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>同意頁用的端點（check、approve、deny）與「我的帳號」的授權清單：只能用 Cookie，失敗是 422 加 error.data。</summary>
[TestFixture]
public sealed class OAuthConsentEndpointTests
{
    private const string Check = "/v1/oauth/authorization/check";
    private const string Approve = "/v1/oauth/authorization/approve";
    private const string Deny = "/v1/oauth/authorization/deny";
    private const string Grants = "/v1/account/oauth-grants";

    private static readonly object Parameters = new
    {
        responseType = "code",
        clientId = "https://app.example.com/client.json",
        redirectUri = "http://127.0.0.1:5000/callback",
        scope = "items:read items:write",
        state = "abc",
        codeChallenge = "challenge",
        codeChallengeMethod = "S256",
        resource = "http://localhost/mcp",
    };

    [Test]
    public async Task Check_ReturnsTheRequestAndTheSignedInAccount()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(Parameters));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var sent = factory.Modules.OAuth.AuthorizeCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["clientId"]?.GetValue<string>(), Is.EqualTo("https://app.example.com/client.json"));
            Assert.That(data?["clientName"]?.GetValue<string>(), Is.EqualTo("範例代理"));
            Assert.That(data?["clientHost"]?.GetValue<string>(), Is.EqualTo("app.example.com"));
            Assert.That(data?["redirectHost"]?.GetValue<string>(), Is.EqualTo("127.0.0.1:5000"));
            Assert.That(data?["redirectIsLoopback"]?.GetValue<bool>(), Is.True);
            Assert.That(data?["redirectMatchesClient"]?.GetValue<bool>(), Is.True);
            Assert.That(
                data?["requestedScopes"]?.AsArray().Select(n => n!.GetValue<string>()),
                Is.EqualTo(new[] { "items:read", "items:write", "blueprint:read", "blueprint:write" }));
            Assert.That(data?["account"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.DisplayName));
            Assert.That(data?["account"]?["email"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.Email));
            Assert.That(data?["account"]?["organizationName"]?.GetValue<string>(), Is.EqualTo(TestUsers.OrganizationName));
            Assert.That(sent.ClientId, Is.EqualTo("https://app.example.com/client.json"));
            Assert.That(sent.CodeChallenge, Is.EqualTo("challenge"));
            Assert.That(sent.Resource, Is.EqualTo("http://localhost/mcp"));
            Assert.That(factory.Modules.OAuth.Servers.Single(), Is.EqualTo(new OAuthServer("http://localhost", "http://localhost/mcp")));
        }
    }

    [Test]
    public async Task Check_RedirectOnAnotherSite_ReportsRedirectMatchesClientFalseWithBothHosts()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.OAuth.OnCheck = _ => new OAuthAuthorizationRequest(
            "https://app.example.com/client.json", "範例代理", "app.example.com", "other.example.net", false, AccessTokenScopes.Grantable)
        {
            RedirectMatchesClient = false,
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(Parameters));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["redirectMatchesClient"]?.GetValue<bool>(), Is.False);
            Assert.That(data?["redirectHost"]?.GetValue<string>(), Is.EqualTo("other.example.net"));
            Assert.That(data?["clientHost"]?.GetValue<string>(), Is.EqualTo("app.example.com"));
        }
    }

    [Test]
    public async Task Check_Failure_Is422WithTheErrorAndRedirectInData()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.OAuth.OnCheck = _ => new OAuthFailure(
            OAuthError.InvalidTarget, "resource 不對。", "http://127.0.0.1:5000/callback?error=invalid_target&state=abc");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(Parameters));

        // Assert
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);
        var error = envelope["error"]!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)422));
            Assert.That(envelope["success"]!.GetValue<bool>(), Is.False);
            Assert.That(error["code"]!.GetValue<string>(), Is.EqualTo("oauth.request_invalid"));
            Assert.That(error["message"]!.GetValue<string>(), Is.EqualTo("resource 不對。"));
            Assert.That(error["details"]!.AsArray(), Is.Empty);
            Assert.That(error["data"]!["error"]!.GetValue<string>(), Is.EqualTo("invalid_target"));
            Assert.That(error["data"]!["redirectTo"]!.GetValue<string>(), Is.EqualTo("http://127.0.0.1:5000/callback?error=invalid_target&state=abc"));
        }
    }

    [Test]
    public async Task Check_FailureThatCannotRedirect_HasANullRedirectTo()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.OAuth.OnCheck = _ => new OAuthFailure(OAuthError.InvalidClient, "不認得這個應用。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(Parameters));

        // Assert
        var data = (await ApiRequests.ReadEnvelopeAsync(response))["error"]!["data"]!.AsObject();
        Assert.That(data.ContainsKey("redirectTo") && data["redirectTo"] is null, Is.True, "redirectTo 要寫出 null");
        Assert.That(data["error"]!.GetValue<string>(), Is.EqualTo("invalid_client"));
    }

    [Test]
    public async Task OtherErrors_DoNotCarryAData()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.DeleteAsync($"{Grants}/{Guid.NewGuid()}");

        // Assert
        var error = (await ApiRequests.ReadEnvelopeAsync(response))["error"]!.AsObject();
        Assert.That(error.ContainsKey("data"), Is.False);
    }

    [Test]
    public async Task Approve_PassesTheChosenScopesAndTheActor_AndReturnsTheRedirect()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Approve, ApiRequests.Json(WithScopes("items:read")));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["redirectTo"]?.GetValue<string>(), Does.StartWith("http://127.0.0.1:5000/callback?code=fake-code"));
            Assert.That(factory.Modules.OAuth.LastGrantedScopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead }));
            Assert.That(factory.Modules.OAuth.LastActor, Is.EqualTo(TestUsers.Member.ToActor()));
        }
    }

    [Test]
    public async Task Approve_UnknownScopeName_Is422InvalidScope()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Approve, ApiRequests.Json(WithScopes("items:read", "root")));

        // Assert
        var error = (await ApiRequests.ReadEnvelopeAsync(response))["error"]!;
        Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)422));
        Assert.That(error["data"]!["error"]!.GetValue<string>(), Is.EqualTo(OAuthError.InvalidScope));
        Assert.That(factory.Modules.OAuth.LastGrantedScopes, Is.Null);
    }

    [Test]
    public async Task Approve_ModuleFailure_Is422WithTheRedirectOfTheError()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.OAuth.OnApprove = _ => new OAuthFailure(OAuthError.AccessDenied, "太多了。", "http://127.0.0.1:5000/callback?error=access_denied");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Approve, ApiRequests.Json(WithScopes("items:read")));

        // Assert
        var data = (await ApiRequests.ReadEnvelopeAsync(response))["error"]!["data"]!;
        Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)422));
        Assert.That(data["error"]!.GetValue<string>(), Is.EqualTo("access_denied"));
        Assert.That(data["redirectTo"]!.GetValue<string>(), Is.EqualTo("http://127.0.0.1:5000/callback?error=access_denied"));
    }

    [Test]
    public async Task Approve_MoreThanTenPerMinute_Is429()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.OAuthApprovePermitsPerWindow + 1; i++)
        {
            using var response = await client.PostAsync(Approve, ApiRequests.Json(WithScopes("items:read")));
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses.Take(ApiLimits.OAuthApprovePermitsPerWindow), Has.All.EqualTo(HttpStatusCode.OK));
        Assert.That(statuses.Last(), Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [TestCase(Check)]
    [TestCase(Deny)]
    public async Task CheckAndDeny_MoreThanTwentyPerMinute_Is429(string route)
    {
        // Arrange：check 會替使用者去抓 Client ID Metadata Document，不限流的話登入的人能拿它當連出去的跳板。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.OAuthCheckPermitsPerWindow + 1; i++)
        {
            using var response = await client.PostAsync(route, ApiRequests.Json(Parameters));
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses.Take(ApiLimits.OAuthCheckPermitsPerWindow), Has.None.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(statuses.Last(), Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task CheckAndDeny_ShareTheSamePerPersonAllowance()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        for (var i = 0; i < ApiLimits.OAuthCheckPermitsPerWindow; i++)
        {
            using var response = await client.PostAsync(Check, ApiRequests.Json(Parameters));
        }

        // Act
        using var denied = await client.PostAsync(Deny, ApiRequests.Json(Parameters));

        // Assert
        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task Deny_ReturnsTheRedirectOfTheDenial()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Deny, ApiRequests.Json(Parameters));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["redirectTo"]?.GetValue<string>(), Does.Contain("error=access_denied"));
    }

    [TestCase(Check)]
    [TestCase(Approve)]
    [TestCase(Deny)]
    public async Task ConsentEndpoints_WithoutLogin_Are401(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync(route, ApiRequests.Json(Parameters));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        Assert.That(factory.Modules.OAuth.AuthorizeCalls, Is.Empty);
    }

    [TestCase(Check)]
    [TestCase(Approve)]
    [TestCase(Deny)]
    public async Task ConsentEndpoints_WithAnAccessToken_AreForbidden_SoATokenCannotAuthorizeMore(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScopes.All.ToArray());
        using var client = factory.CreateClient();
        using var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, route, secret);
        request.Content = ApiRequests.Json(Parameters);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    [Test]
    public async Task ConsentEndpoints_WithoutTheCsrfHeader_Are403()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        using var noHeader = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Approve);
        request.Headers.Add("Cookie", cookie);
        request.Content = ApiRequests.Json(WithScopes("items:read"));

        // Act
        using var response = await noHeader.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
    }

    [Test]
    public async Task ConsentEndpoints_UnknownProperty_Is400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Check, ApiRequests.Json(new { clientId = "x", surprise = 1 }));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Grants_ListsTheOwnGrantsUncached()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var grant = factory.Modules.OAuth.AddGrant("我的代理", null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Grants);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var first = data!.AsArray().Single()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(first["grantId"]!.GetValue<Guid>(), Is.EqualTo(grant.GrantId));
            Assert.That(first["clientName"]!.GetValue<string>(), Is.EqualTo("我的代理"));
            Assert.That(first["clientHost"], Is.Null);
            Assert.That(first["scopes"]!.AsArray().Single()!.GetValue<string>(), Is.EqualTo("items:read"));
            Assert.That(first["createdAt"]!.GetValue<string>(), Does.EndWith("Z"));
            Assert.That(first["lastUsedAt"], Is.Null);
        }
    }

    [Test]
    public async Task RevokeGrant_Returns204_AndAgain404WithTheGrantNotFoundCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var grant = factory.Modules.OAuth.AddGrant();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var first = await client.DeleteAsync($"{Grants}/{grant.GrantId}");
        using var second = await client.DeleteAsync($"{Grants}/{grant.GrantId}");
        using var notAGuid = await client.DeleteAsync($"{Grants}/nonsense");

        // Assert
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await ApiRequests.AssertErrorEnvelopeAsync(second, HttpStatusCode.NotFound, "oauth.grant_not_found");
        await ApiRequests.AssertErrorEnvelopeAsync(notAGuid, HttpStatusCode.NotFound, "oauth.grant_not_found");
    }

    [TestCase("GET", Grants)]
    [TestCase("DELETE", Grants + "/b9c1c8a4-0000-4000-8000-000000000001")]
    public async Task Grants_WithAnAccessToken_AreForbidden(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScopes.All.ToArray());
        using var client = factory.CreateClient();
        using var request = AccessTokenAuthenticationTests.Bearer(new HttpMethod(method), route, secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static object WithScopes(params string[] scopes) => new
    {
        responseType = "code",
        clientId = "https://app.example.com/client.json",
        redirectUri = "http://127.0.0.1:5000/callback",
        scope = "items:read items:write",
        state = "abc",
        codeChallenge = "challenge",
        codeChallengeMethod = "S256",
        resource = "http://localhost/mcp",
        scopes,
    };
}
