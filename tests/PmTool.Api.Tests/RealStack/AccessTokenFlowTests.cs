using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>在網頁上（Cookie）建一把權杖，拿它呼叫 API；撤銷之後立刻失效。</summary>
[TestFixture]
public sealed class AccessTokenFlowTests : RealStackTestBase
{
    private const string TokensRoute = "/v1/account/tokens";

    [Test]
    public async Task Token_CreatedOnTheWeb_CallsTheApiWithinItsScopesAndStopsWorkingOnceRevoked()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "TOK", "simple_tasks");
        var created = await CreateTokenAsync(owner.Client, "items:read");
        var secret = created["secret"]!.GetValue<string>();
        using var cli = Factory.CreateClient();

        // Act
        using var projects = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/projects", secret));
        var projectData = await ApiRequests.AssertSuccessEnvelopeAsync(projects, HttpStatusCode.OK);
        using var write = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/v1/projects/TOK/items", secret));
        using var webOnly = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, TokensRoute, secret));
        var listed = await RealStackSteps.GetAsync(owner.Client, TokensRoute);
        using var revoked = await owner.Client.DeleteAsync($"{TokensRoute}/{created["token"]!["tokenId"]!.GetValue<Guid>()}");
        using var afterwards = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/projects", secret));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KeysOf(projectData), Does.Contain("TOK"));
            Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "只有 items:read");
            Assert.That(webOnly.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "權杖管理只能用 Cookie");
            Assert.That(listed.AsArray().Select(t => t!["name"]!.GetValue<string>()), Does.Contain("web"));
            Assert.That(listed.ToJsonString(), Does.Not.Contain(secret));
            Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(afterwards, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task Token_OfADisabledPerson_StopsWorkingAndStaysDeadAfterTheyAreEnabled()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var member = await RealStackSteps.CreateMemberAsync(owner.Client, "Token Member", RealStackSteps.UniqueEmail("token"));
        var personId = member["personId"]!.GetValue<Guid>();
        using var memberClient = await SignInAsAsync(member["email"]!.GetValue<string>(), RealStackSteps.MemberPassword);
        var secret = (await CreateTokenAsync(memberClient.Client, "items:read"))["secret"]!.GetValue<string>();
        using var cli = Factory.CreateClient();
        using var before = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", secret));

        // Act
        using var disable = await owner.Client.PostAsync($"/v1/people/{personId}/disable", null);
        using var disabled = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", secret));
        using var enable = await owner.Client.PostAsync($"/v1/people/{personId}/enable", null);
        using var enabled = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", secret));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(disable.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(disabled.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(enable.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(enabled.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
    }

    private static async Task<JsonNode> CreateTokenAsync(HttpClient client, params string[] scopes)
    {
        using var response = await client.PostAsync(TokensRoute, ApiRequests.Json(new { name = "web", scopes, expiresInDays = 30 }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created)
            ?? throw new AssertionException("建立權杖的回應沒有 data");
    }
}
