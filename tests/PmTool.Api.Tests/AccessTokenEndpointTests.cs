using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>權杖管理的三個端點：只能用 Cookie 登入呼叫。</summary>
[TestFixture]
public sealed class AccessTokenEndpointTests
{
    private const string Route = "/v1/account/tokens";

    [Test]
    public async Task Create_ReturnsTheTokenAndTheSecretOnce_AndTheListNeverShowsTheSecret()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var created = await client.PostAsync(Route, ApiRequests.Json(new { name = "CI", scopes = new[] { "items:read", "blueprint:write" }, expiresInDays = 90 }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        using var listed = await client.GetAsync(Route);
        var list = await ApiRequests.AssertSuccessEnvelopeAsync(listed, HttpStatusCode.OK);

        // Assert
        var secret = data?["secret"]?.GetValue<string>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(secret, Does.StartWith("pmt_"));
            Assert.That(data?["token"]?["name"]?.GetValue<string>(), Is.EqualTo("CI"));
            Assert.That(data?["token"]?["displayPrefix"]?.GetValue<string>(), Is.EqualTo(secret?[..8]));
            Assert.That(data?["token"]?["scopes"]?.AsArray().Select(n => n!.GetValue<string>()), Is.EqualTo(new[] { "items:read", "blueprint:write" }));
            Assert.That(data?["token"]?["lastUsedAt"], Is.Null);
            Assert.That(data?["token"]?.AsObject().ContainsKey("machineName") == true && data["token"]!["machineName"] is null, Is.True, "machineName 要寫出 null");
            Assert.That(data?["token"]?.AsObject().ContainsKey("createdFrom") == true && data["token"]!["createdFrom"] is null, Is.True, "createdFrom 要寫出 null");
            Assert.That(data?["token"]?["expiresAt"]?.GetValue<string>(), Does.EndWith("Z"));
            Assert.That(created.Headers.Location, Is.Null);
            Assert.That(list?.ToJsonString(), Does.Not.Contain("secret").And.Not.Contain(secret));
            Assert.That(list?.AsArray(), Has.Count.EqualTo(1));
            Assert.That(list?[0]?["tokenId"]?.GetValue<Guid>(), Is.EqualTo(data?["token"]?["tokenId"]?.GetValue<Guid>()));
        }
    }

    [Test]
    public async Task List_CarriesTheAddressTheTokenWasCreatedFrom()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AccessTokens.IssueCreatedFrom(TestUsers.Member, "203.0.113.5", AccessTokenScope.ItemsRead);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var listed = await client.GetAsync(Route);
        var list = await ApiRequests.AssertSuccessEnvelopeAsync(listed, HttpStatusCode.OK);

        // Assert
        Assert.That(list?[0]?["createdFrom"]?.GetValue<string>(), Is.EqualTo("203.0.113.5"));
    }

    [Test]
    public async Task List_ATokenFromABrowserLogin_CarriesTheMachineName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AccessTokens.IssueFromMachine(TestUsers.Member, "MacBook-Pro", AccessTokenScope.ItemsRead);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var listed = await client.GetAsync(Route);
        var list = await ApiRequests.AssertSuccessEnvelopeAsync(listed, HttpStatusCode.OK);

        // Assert
        Assert.That(list?[0]?["machineName"]?.GetValue<string>(), Is.EqualTo("MacBook-Pro"));
    }

    [Test]
    public async Task CreateAndList_AreNeverCached()
    {
        // Arrange：建立的回應有只會出現一次的明文。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var created = await client.PostAsync(Route, ApiRequests.Json(new { name = "CI", scopes = new[] { "items:read" }, expiresInDays = 30 }));
        using var listed = await client.GetAsync(Route);

        // Assert
        Assert.That(created.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(listed.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task Create_NullExpiry_Is422WithThePathOfTheField()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { name = "x", scopes = new[] { "items:read" }, expiresInDays = (int?)null }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, IdentityErrorCodes.AccessTokenExpiryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{IdentityErrorCodes.AccessTokenExpiryInvalid}|expiresInDays" }));
    }

    [Test]
    public async Task Create_TheSecretWorksAsABearerToken()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var created = await client.PostAsync(Route, ApiRequests.Json(new { name = "x", scopes = new[] { "items:read" }, expiresInDays = 30 }));
        var secret = (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))?["secret"]?.GetValue<string>();
        using var anonymous = factory.CreateClient();

        // Act
        using var response = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/projects", secret!));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("""{"name":"","scopes":["items:read"],"expiresInDays":30}""", IdentityErrorCodes.AccessTokenNameRequired, "name")]
    [TestCase("""{"name":"x","scopes":[],"expiresInDays":30}""", IdentityErrorCodes.AccessTokenScopesRequired, "scopes")]
    [TestCase("""{"name":"x","scopes":["items:read"],"expiresInDays":0}""", IdentityErrorCodes.AccessTokenExpiryInvalid, "expiresInDays")]
    [TestCase("""{"name":"x","scopes":["agent:run"],"expiresInDays":91}""", IdentityErrorCodes.AccessTokenExpiryInvalid, "expiresInDays")]
    public async Task Create_ModuleRejects_Returns422WithTheCodeAndTheFieldPath(string body, string code, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, code);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{code}|{path}" }));
    }

    [Test]
    public async Task Create_UnknownScopeName_Returns400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { name = "x", scopes = new[] { "items:admin" }, expiresInDays = 30 }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("scopes"));
    }

    [Test]
    public async Task Create_MissingOrExtraProperties_Return400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var missing = await client.PostAsync(Route, ApiRequests.Document("""{"name":"x","scopes":["items:read"]}"""));
        using var extra = await client.PostAsync(Route, ApiRequests.Document("""{"name":"x","scopes":["items:read"],"expiresInDays":1,"admin":true}"""));

        // Assert
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(extra.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Create_TooManyActiveTokens_Returns409()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        for (var i = 0; i < AccessTokenRules.MaxActiveTokensPerAccount; i++)
        {
            using var ok = await client.PostAsync(Route, ApiRequests.Json(new { name = $"t{i}", scopes = new[] { "items:read" }, expiresInDays = 30 }));
        }

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { name = "one too many", scopes = new[] { "items:read" }, expiresInDays = 30 }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, IdentityErrorCodes.AccessTokenLimitReached);
    }

    [Test]
    public async Task Revoke_OwnToken_ReturnsItAndItStopsWorking()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var created = await client.PostAsync(Route, ApiRequests.Json(new { name = "x", scopes = new[] { "items:read" }, expiresInDays = 30 }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var tokenId = data?["token"]?["tokenId"]?.GetValue<Guid>();
        using var anonymous = factory.CreateClient();

        // Act
        using var revoked = await client.DeleteAsync($"{Route}/{tokenId}");
        var revokedData = await ApiRequests.AssertSuccessEnvelopeAsync(revoked, HttpStatusCode.OK);
        using var afterwards = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", data?["secret"]?.GetValue<string>()!));

        // Assert
        Assert.That(revokedData?["tokenId"]?.GetValue<Guid>(), Is.EqualTo(tokenId));
        Assert.That(afterwards.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000001")]
    public async Task Revoke_UnknownOrNotAGuid_Returns404(string tokenId)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync($"{Route}/{tokenId}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, IdentityErrorCodes.AccessTokenNotFound);
    }

    [Test]
    public async Task Revoke_SomeoneElsesToken_Returns404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var owner = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var member = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var created = await owner.PostAsync(Route, ApiRequests.Json(new { name = "x", scopes = new[] { "items:read" }, expiresInDays = 30 }));
        var tokenId = (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))?["token"]?["tokenId"]?.GetValue<Guid>();

        // Act
        using var response = await member.DeleteAsync($"{Route}/{tokenId}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, IdentityErrorCodes.AccessTokenNotFound);
    }

    [Test]
    public async Task Management_NotSignedIn_Returns401_AndWritesNeedTheCsrfHeader()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var anonymous = factory.CreateClient();
        var cookie = await Sessions.SignInForCookieAsync(factory.CreateApiClient(), TestUsers.Member);
        using var signedIn = factory.CreateClient();

        // Act
        using var list = await anonymous.GetAsync(Route);
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = ApiRequests.Json(new { name = "x", scopes = new[] { "items:read" }, expiresInDays = 30 }),
        };
        request.Headers.Add("Cookie", cookie);
        using var withoutHeader = await signedIn.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(list, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        await ApiRequests.AssertErrorEnvelopeAsync(withoutHeader, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
    }
}
