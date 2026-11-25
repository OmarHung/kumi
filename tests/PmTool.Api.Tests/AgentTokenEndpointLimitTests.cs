using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 本機代理的權杖（agent:run）會交給成員電腦上的引擎：REST 上只能打領工作的端點與任何權杖都能用的端點，
/// 就算權杖另外帶了別的範圍。沒有 agent:run 的權杖不受影響。
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class AgentTokenEndpointLimitTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite, AccessTokenScope.ItemsRead];

    [TestCase("POST", "/v1/projects/WEB/changesets/1/apply")]
    [TestCase("POST", "/v1/projects/WEB/changesets")]
    [TestCase("PUT", "/v1/projects/WEB/members/00000000-0000-0000-0000-000000000001")]
    [TestCase("POST", "/v1/projects/WEB/archive")]
    [TestCase("GET", "/v1/projects/WEB/items")]
    [TestCase("GET", "/v1/items")]
    [TestCase("GET", "/v1/items/WEB-1")]
    [TestCase("GET", "/v1/projects/WEB")]
    [TestCase("GET", "/v1/projects/WEB/changesets")]
    [TestCase("GET", "/v1/notifications")]
    [TestCase("GET", "/v1/people")]
    public async Task Endpoint_AgentRunTokenWithOtherMatchingScopes_Returns403AndSaysItIsForTheLocalAgent(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AgentScopes);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Request(method, route, secret));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        var message = (await ApiRequests.ReadEnvelopeAsync(response))["error"]?["message"]?.GetValue<string>();
        Assert.That(message, Does.Contain("本機代理"));
    }

    [TestCase("POST", "/v1/projects/WEB/items")]
    [TestCase("PATCH", "/v1/items/WEB-1")]
    [TestCase("POST", "/v1/items/WEB-1/comments")]
    public async Task Endpoint_OldTokenWithAgentRunAndItemsWrite_CannotCreateOrChangeItemsOrComment(string method, string route)
    {
        // Arrange: 建立權杖時已經擋掉這個組合；這裡直接發一把舊的，確認端點這一層也擋。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.AgentRun, AccessTokenScope.ItemsWrite, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Request(method, route, secret));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    [TestCase("POST", "/v1/agent/check-in")]
    [TestCase("GET", "/v1/session")]
    [TestCase("GET", "/v1/cli")]
    public async Task Endpoint_AgentRunTokenOnTheAgentOrAnyTokenEndpoints_IsNotRejectedForItsScope(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AgentScopes);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Request(method, route, secret));

        // Assert
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await IsInsufficientScopeAsync(response), Is.False);
    }

    [Test]
    public async Task Endpoint_TokenWithoutAgentRun_StillWorksForItsScope()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Request("GET", "/v1/items", secret));

        // Assert
        Assert.That(await IsInsufficientScopeAsync(response), Is.False);
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static HttpRequestMessage Request(string method, string route, string secret)
    {
        var request = AccessTokenAuthenticationTests.Bearer(new HttpMethod(method), route, secret);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = ApiRequests.Json(new { });
        }

        return request;
    }

    private static async Task<bool> IsInsufficientScopeAsync(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Forbidden
        && (await ApiRequests.ReadEnvelopeAsync(response))["error"]?["code"]?.GetValue<string>() == ApiErrorCodes.InsufficientScope;
}
