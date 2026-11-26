using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 一把權杖撤銷它自己（<c>kumi auth logout --revoke</c>）：權杖的清單與撤銷只能用 Cookie，CLI 手上只有權杖本身。
/// 只能撤銷「這一把」，所以任何範圍（含本機代理的 agent:run）都可以打；用 Cookie 打沒有「這一把」可撤銷。
/// </summary>
[TestFixture]
public sealed class AccessTokenSelfRevokeTests
{
    private const string Route = "/v1/account/tokens";
    private const string CurrentRoute = Route + "/current";

    [Test]
    public async Task RevokeCurrent_WithTheTokenItself_RevokesOnlyThatToken()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var (tokenId, secret) = await CreateAsync(client, ["items:read"]);
        var (_, other) = await CreateAsync(client, ["items:read"]);
        using var anonymous = factory.CreateClient();

        // Act
        using var revoked = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Delete, CurrentRoute, secret));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(revoked, HttpStatusCode.OK);
        using var afterwards = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", secret));
        using var otherStill = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", other));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["tokenId"]?.GetValue<Guid>(), Is.EqualTo(tokenId));
            Assert.That(afterwards.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(otherStill.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task RevokeCurrent_WithTheLocalAgentToken_IsAllowed()
    {
        // Arrange：本機代理的權杖在 REST 上只能打少數端點，撤銷自己要在其中。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var (_, secret) = await CreateAsync(client, ["agent:run", "blueprint:read", "blueprint:write", "items:read"]);
        using var anonymous = factory.CreateClient();

        // Act
        using var revoked = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Delete, CurrentRoute, secret));

        // Assert
        await ApiRequests.AssertSuccessEnvelopeAsync(revoked, HttpStatusCode.OK);
    }

    [Test]
    public async Task RevokeCurrent_WithACookie_Is404AndRevokesNothing()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var (_, secret) = await CreateAsync(client, ["items:read"]);
        using var anonymous = factory.CreateClient();

        // Act
        using var response = await client.DeleteAsync(CurrentRoute);
        using var still = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", secret));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, IdentityErrorCodes.AccessTokenNotFound);
        Assert.That(still.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task<(Guid TokenId, string Secret)> CreateAsync(HttpClient client, string[] scopes)
    {
        using var created = await client.PostAsync(Route, ApiRequests.Json(new { name = "cli " + Guid.NewGuid().ToString("N")[..6], scopes, expiresInDays = 30 }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        return (data!["token"]!["tokenId"]!.GetValue<Guid>(), data["secret"]!.GetValue<string>());
    }
}
