using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 跨專案的對話清單（<c>GET /v1/assistant/all-conversations</c>，浮動面板的「對話」選單）：每一列是對話清單的形狀加 <c>projectKey</c>
/// （不選專案的是 null），只能用 Cookie。哪些列得出來（權限）在 AI 模組的測試。
/// </summary>
[TestFixture]
public sealed class AssistantAllConversationsEndpointTests
{
    private const string Route = "/v1/assistant/all-conversations";

    [Test]
    public async Task List_WritesEachConversationWithItsProjectKey()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnListAll = _ => new List<AssistantConversationListing>
        {
            new("WEB", FakeAssistantService.Summary()),
            new(null, FakeAssistantService.Summary(title: null, isRunning: true)),
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            $$"""
            [
              {"projectKey":"WEB","id":"{{FakeAssistantService.ConversationId}}","title":"把驗收拆成兩段","messageCount":0,"isRunning":false,
               "createdAt":"2026-11-23T03:00:00Z","updatedAt":"2026-11-23T03:05:00Z"},
              {"projectKey":null,"id":"{{FakeAssistantService.ConversationId}}","title":null,"messageCount":0,"isRunning":true,
               "createdAt":"2026-11-23T03:00:00Z","updatedAt":"2026-11-23T03:05:00Z"}
            ]
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Assistant.ListAllCalls, Is.EqualTo(new[] { TestUsers.Member.ToActor() }));
        }
    }

    [Test]
    public async Task List_AFailureFromTheModule_IsMappedToItsStatusAndCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnListAll = _ => new Failure(FailureKind.Forbidden, AiErrorCodes.AssistantForbidden, "不能用。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, AiErrorCodes.AssistantForbidden);
    }

    [Test]
    public async Task List_WithoutSigningIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(factory.Modules.Assistant.ListAllCalls, Is.Empty);
        }
    }
}
