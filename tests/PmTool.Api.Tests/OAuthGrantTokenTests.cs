using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>OAuth 授權發的權杖（有授權編號）只能用在 /mcp；個人存取權杖不受這一關影響。</summary>
[TestFixture]
public sealed class OAuthGrantTokenTests
{
    [TestCase("/v1/session")]
    [TestCase("/v1/projects")]
    [TestCase("/v1/account/tokens")]
    public async Task GrantToken_OnTheApi_IsTreatedAsNotSignedInWithAnExplanation(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.IssueForGrant(TestUsers.Member, Guid.NewGuid(), AccessTokenScopes.All.ToArray());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, route, secret));

        // Assert
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(envelope["error"]!["code"]!.GetValue<string>(), Is.EqualTo(ApiErrorCodes.AuthRequired));
            Assert.That(envelope["error"]!["message"]!.GetValue<string>(), Does.Contain("MCP"));
            Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.StartWith("Bearer"));
        }
    }

    [Test]
    public async Task GrantToken_OnTheApi_DoesNotCountAsAFailedAttempt()
    {
        // Arrange：權杖本身是有效的，不能因為用錯地方就把來源擋掉。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.IssueForGrant(TestUsers.Member, Guid.NewGuid(), AccessTokenScopes.All.ToArray());
        using var client = factory.CreateClient();

        // Act
        for (var i = 0; i < 40; i++)
        {
            using var denied = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", secret));
            Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        using var personal = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(
            HttpMethod.Get, "/v1/session", factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead)));

        // Assert
        Assert.That(personal.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PersonalToken_StillWorksOnTheApi()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/projects", secret));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GrantToken_OnMcp_IsNotStoppedByThisCheck()
    {
        // Arrange：/mcp 自己的驗證與挑戰是另一組測試（Mcp*Tests）；這裡只確認不被「只能用在 MCP」這一關擋掉。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.IssueForGrant(TestUsers.Member, Guid.NewGuid(), AccessTokenScopes.All.ToArray());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/mcp", secret));
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized), body);
        Assert.That(body, Does.Not.Contain("只能用在 MCP"));
    }
}
