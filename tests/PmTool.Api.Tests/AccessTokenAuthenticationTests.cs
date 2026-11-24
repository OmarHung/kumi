using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>用 <c>Authorization: Bearer pmt_…</c> 驗證：有效與各種無效、和 Cookie 同時出現、防偽標頭、限流。</summary>
[TestFixture]
public sealed class AccessTokenAuthenticationTests
{
    private const string SessionRoute = "/v1/session";

    [Test]
    public async Task Bearer_ValidToken_ActsAsTheTokenOwnerWithoutCookieOrCsrfHeader()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["user"]?["email"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.Email));
        Assert.That(Sessions.SetCookieLineOf(response), Is.Null, "權杖的請求不發 Cookie");
    }

    [Test]
    public async Task Bearer_Session_ReportsTheScopesOfTheToken()
    {
        // Arrange（kumi 靠它分辨存的是命令列的權杖還是本機代理的：internal/HANDOFF-setup-wizard.md 第 8 節）
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var scopes = data?["scopes"]?.AsArray().Select(node => node?.GetValue<string>()).ToArray();
        Assert.That(scopes, Is.EquivalentTo(new[] { "agent:run", "items:read" }));
    }

    [Test]
    public async Task Cookie_Session_HasNoScopes()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(SessionRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?.AsObject().ContainsKey("scopes"), Is.True);
        Assert.That(data?["scopes"], Is.Null, "網頁的工作階段沒有範圍的限制");
    }

    [Test]
    public async Task Bearer_ValidToken_PassesThePersonOfTheTokenToTheModules()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Bearer(HttpMethod.Get, "/v1/projects", secret));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Projects.ListCalls, Is.EqualTo(new[] { TestUsers.Owner.ToActor() }));
    }

    [TestCase("pmt_unknown")]
    [TestCase("not-a-token")]
    [TestCase("")]
    public async Task Bearer_UnknownToken_Returns401(string secret)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task Bearer_RevokedExpiredOrDisabledAccount_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var revoked = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        var expired = factory.Modules.AccessTokens.Issue(TestUsers.Member, 1, AccessTokenScope.ItemsRead);
        var disabled = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.ItemsRead);
        factory.Modules.AccessTokens.Revoke(revoked);
        factory.Modules.Time.Advance(TimeSpan.FromDays(2));
        factory.Modules.Authentication.Disable(TestUsers.Owner.AccountId);
        using var client = factory.CreateClient();

        // Act
        var statuses = new List<HttpStatusCode>();
        foreach (var secret in new[] { revoked, expired, disabled })
        {
            using var response = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Bearer_TokenRevokedAfterUse_StopsWorkingOnTheNextRequest()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        using var before = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));

        // Act
        factory.Modules.AccessTokens.Revoke(secret);
        using var after = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));

        // Assert
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Bearer_WithAValidCookieToo_OnlyTheTokenCounts()
    {
        // Arrange：Cookie 是擁有者的，權杖是成員的。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Owner);
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);

        // Act
        using var response = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["user"]?["email"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.Email));
    }

    [Test]
    public async Task Bearer_InvalidTokenWithAValidCookieToo_Returns401InsteadOfFallingBackToTheCookie()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Owner);

        // Act
        using var response = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, "pmt_unknown"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task Bearer_WriteWithoutCsrfHeader_IsNotBlockedByTheCsrfCheck()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsWrite);
        using var client = factory.CreateClient();
        var request = Bearer(HttpMethod.Delete, "/v1/items/WEB-1", secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Items.DeleteCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Cookie_WriteWithoutCsrfHeader_StillNeedsIt()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var cookie = await Sessions.SignInForCookieAsync(factory.CreateApiClient(), TestUsers.Member);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/items/WEB-1");
        request.Headers.Add("Cookie", cookie);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
    }

    [Test]
    public async Task Bearer_InvalidTokenOnAWriteWithoutCsrfHeader_Returns401NotCsrfRequired()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Bearer(HttpMethod.Delete, "/v1/items/WEB-1", "pmt_unknown"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task Bearer_OtherAuthorizationSchemes_AreIgnoredAndTheCookieIsUsed()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Owner);
        using var request = new HttpRequestMessage(HttpMethod.Get, SessionRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");

        // Act
        using var response = await client.SendAsync(request);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["user"]?["email"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.Email));
    }

    [Test]
    public async Task Bearer_PublicEndpoints_DoNotCareAboutTheToken()
    {
        // Arrange：驗證端點本來就不需要登入；帶了無效的權杖也照常回答。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        using var request = Bearer(HttpMethod.Post, ApiRequests.BlueprintRoute, "pmt_unknown");
        request.Content = ApiRequests.Document("{}");

        // Act
        using var validate = await client.SendAsync(request);
        using var health = await client.SendAsync(Bearer(HttpMethod.Get, "/health", "pmt_unknown"));

        // Assert
        Assert.That(validate.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(validate.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Bearer_RateLimitIsPerPersonNotPerToken()
    {
        // Arrange：同一個人的兩把權杖共用額度，別人的不受影響。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var first = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        var second = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        var other = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.SignedInPermitsPerWindow; i++)
        {
            using var ok = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, first));
        }

        // Act
        using var exhausted = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, second));
        using var untouched = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, other));

        // Assert
        Assert.That(exhausted.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(untouched.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    internal static HttpRequestMessage Bearer(HttpMethod method, string route, string secret)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {secret}");
        return request;
    }

    internal static async Task<JsonNode?> DataOfAsync(HttpResponseMessage response, HttpStatusCode status) =>
        await ApiRequests.AssertSuccessEnvelopeAsync(response, status);
}
