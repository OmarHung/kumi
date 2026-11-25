using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 本機代理的權杖（agent:run）會交給成員電腦上的引擎，能打的端點要是一份看得見的清單：
/// 走遍主機所有的端點，宣告了 agent:run 或「任何權杖都可以」的就只有這六個。
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class AgentTokenEndpointListTests
{
    private const string RequirementTypeName = "TokenScopeRequirement";

    private static readonly string[] Expected =
    [
        "GET /v1/cli",
        "GET /v1/cli/download/{platform}",
        "GET /v1/session",
        "POST /v1/agent/check-in",
        "POST /v1/agent/jobs/{jobId:guid}/events",
        "POST /v1/agent/jobs/{jobId:guid}/finish",
    ];

    [Test]
    public void Endpoints_OpenToTheAgentRunToken_AreExactlyTheKnownList()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();

        // Act
        var open = endpoints
            .Where(RequiresSignIn)
            .Where(OpensToAgentRunToken)
            .SelectMany(endpoint => MethodsOf(endpoint).Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .Order()
            .ToArray();

        // Assert
        Assert.That(
            open,
            Is.EqualTo(Expected),
            "本機代理的權杖只該打得到領工作的端點與任何權杖都能用的端點。新增的端點會因此開給本機代理的權杖（它在成員的電腦上、引擎拿得到），"
            + "確定要的話再把它加進這份清單；不是的話，端點改成宣告別的範圍，或不宣告（只給網頁用）。");
    }

    [Test]
    public async Task LiveHub_WithAnAgentRunToken_IsRefused()
    {
        // Arrange
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions
            {
                Settings = new Dictionary<string, string?> { [LiveRegistration.EnabledSetting] = "true", [LiveUpdateWorker.EnabledSetting] = "false" },
            });
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, LiveRegistration.Route + "/negotiate?negotiateVersion=1", secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    private static bool RequiresSignIn(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

    // TokenScopeRequirement 是 internal：用型別名稱從端點的 metadata 找，不為了測試把它改成 public。
    private static bool OpensToAgentRunToken(Endpoint endpoint)
    {
        var requirement = endpoint.Metadata.FirstOrDefault(item => item.GetType().Name == RequirementTypeName);
        if (requirement is null)
        {
            return false;
        }

        var type = requirement.GetType();
        var allowsAny = (bool)type.GetProperty("AllowsAnyToken", BindingFlags.Public | BindingFlags.Instance)!.GetValue(requirement)!;
        var anyOf = (IEnumerable<AccessTokenScope>)type.GetProperty("AnyOf", BindingFlags.Public | BindingFlags.Instance)!.GetValue(requirement)!;
        return allowsAny || anyOf.Contains(AccessTokenScope.AgentRun);
    }

    private static IEnumerable<string> MethodsOf(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["(any)"];
}
