using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>專案的 AI 助理的對話端點：權限與錯誤碼、資料的形狀、請求怎麼交給模組。送訊息的串流在 <c>AssistantStreamTests</c>。</summary>
[TestFixture]
public sealed class AssistantEndpointTests
{
    internal const string Collection = "/v1/projects/WEB/assistant/conversations";

    internal static readonly string Item = $"{Collection}/{FakeAssistantService.ConversationId}";

    private const string SummaryJson = """
        {"id":"22222222-2222-2222-2222-222222222222","title":"把驗收拆成兩段","messageCount":0,"isRunning":false,
         "createdAt":"2026-11-23T03:00:00Z","updatedAt":"2026-11-23T03:05:00Z"}
        """;

    [Test]
    public async Task List_ReturnsTheConversationsInTheContractShape_AndPassesTheSessionAndProject()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Collection);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse($"[{SummaryJson}]")), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Assistant.ListCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB") }));
        }
    }

    [Test]
    public async Task List_NoConversations_IsAnEmptyArray()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnList = (_, _) => new List<AssistantConversationSummary>();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Collection);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?.AsArray(), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("{}")]
    [TestCase("not even json")]
    public async Task Create_WithAnyBody_Returns201WithLocationAndTheConversation(string? body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Collection, body is null ? null : ApiRequests.Document(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo(Item));
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(SummaryJson)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Assistant.CreateCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB") }));
        }
    }

    [Test]
    public async Task Get_ReturnsTheConversationWithMessagesPartsSelectionAndUsage()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Item);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse("""
            {"conversation":{"id":"22222222-2222-2222-2222-222222222222","title":"把驗收拆成兩段","messageCount":2,"isRunning":false,
              "createdAt":"2026-11-23T03:00:00Z","updatedAt":"2026-11-23T03:05:00Z"},
             "messages":[
              {"id":"33333333-3333-3333-3333-333333333331","role":"user","createdAt":"2026-11-23T03:00:00Z",
               "parts":[{"type":"text","text":"把驗收拆成內部驗收和客戶驗收"}],
               "status":null,"error":null,"selection":null,"connectionName":null,"usage":null,"itemKey":null,"attachments":[]},
              {"id":"33333333-3333-3333-3333-333333333332","role":"assistant","createdAt":"2026-11-23T03:01:00Z",
               "parts":[{"type":"text","text":"好，我先讀目前的設定。"},
                        {"type":"tool","callId":"c1","name":"propose_changeset","status":"done","changeSetNumber":12,"error":null,"itemProposalId":null,"projectProposalId":null,"newItemProposalId":null,"memberProposalId":null}],
               "status":"complete","error":null,
               "selection":{"connectionId":"11111111-1111-1111-1111-111111111111","model":"claude-opus-5-5","effort":"high"},
               "connectionName":"Anthropic","usage":{"inputTokens":1200,"outputTokens":300},"itemKey":null,"attachments":[]}]}
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Assistant.GetCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB", FakeAssistantService.ConversationId) }));
        }
    }

    [Test]
    public async Task Get_StoppedFailedAndFailedToolParts_KeepTheirReasons()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failedTool = new AssistantToolPart("c2", "preview_changeset", AssistantToolStatus.Failed, null, "變更集驗證不過");
        factory.Modules.Assistant.OnGet = (_, _, _) => new AssistantConversation(
            FakeAssistantService.Summary(messageCount: 2),
            [
                FakeAssistantService.UserMessage(),
                FakeAssistantService.AssistantReply(AssistantMessageStatus.Failed, "模型拒絕了這個要求") with
                {
                    Parts = [failedTool],
                    Selection = new AiSelection(FakeAssistantService.ConnectionId, "gpt-5", null),
                    Usage = null,
                },
            ]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Item);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var reply = data?["messages"]?[1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply?["status"]?.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(reply?["error"]?.GetValue<string>(), Is.EqualTo("模型拒絕了這個要求"));
            Assert.That(reply?["parts"]?[0]?["status"]?.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(reply?["parts"]?[0]?["error"]?.GetValue<string>(), Is.EqualTo("變更集驗證不過"));
            Assert.That(reply?["parts"]?[0]?["changeSetNumber"], Is.Null);
            Assert.That(reply?["selection"]?.AsObject().ContainsKey("effort") == true && reply["selection"]!["effort"] is null, Is.True);
            Assert.That(reply?.AsObject().ContainsKey("usage") == true && reply["usage"] is null, Is.True);
        }
    }

    [Test]
    public async Task Delete_Returns204_AndPassesTheIds()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.DeleteAsync(Item);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(factory.Modules.Assistant.DeleteCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB", FakeAssistantService.ConversationId) }));
        }
    }

    [TestCase(FailureKind.Forbidden, AiErrorCodes.AssistantForbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.NotFound, AiErrorCodes.ConversationNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.ConversationBusy, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Invalid, AiErrorCodes.Unavailable, HttpStatusCode.UnprocessableEntity)]
    public async Task EveryEndpoint_ModuleFailure_BecomesTheEnvelopeWithItsCodeAndStatus(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failure = new Failure(kind, code, "原因說明");
        var assistant = factory.Modules.Assistant;
        assistant.OnList = (_, _) => failure;
        assistant.OnCreate = (_, _) => failure;
        assistant.OnGet = (_, _, _) => failure;
        assistant.OnDelete = (_, _, _) => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var list = await client.GetAsync(Collection);
        using var create = await client.PostAsync(Collection, null);
        using var get = await client.GetAsync(Item);
        using var delete = await client.DeleteAsync(Item);

        // Assert
        foreach (var response in new[] { list, create, get, delete })
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
        }
    }

    [Test]
    public async Task Routes_ConversationIdThatIsNotAGuid_Is404WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var get = await client.GetAsync($"{Collection}/not-a-guid");
        using var delete = await client.DeleteAsync($"{Collection}/123");
        using var send = await client.PostAsync($"{Collection}/abc/messages", ApiRequests.Json(new { text = "a" }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(factory.Modules.Assistant.GetCalls, Is.Empty);
            Assert.That(factory.Modules.Assistant.StartCalls, Is.Empty);
        }
    }

    [Test]
    public async Task EveryEndpoint_WithoutSignIn_Is401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        var responses = new[]
        {
            await client.GetAsync(Collection),
            await client.PostAsync(Collection, null),
            await client.GetAsync(Item),
            await client.DeleteAsync(Item),
            await client.PostAsync($"{Item}/messages", ApiRequests.Json(new { text = "a" })),
        };

        // Assert
        Assert.That(responses.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Modules.Assistant.ListCalls, Is.Empty);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [TestCase("GET", "")]
    [TestCase("POST", "")]
    [TestCase("GET", "/22222222-2222-2222-2222-222222222222")]
    [TestCase("DELETE", "/22222222-2222-2222-2222-222222222222")]
    [TestCase("POST", "/22222222-2222-2222-2222-222222222222/messages")]
    public async Task EveryEndpoint_PersonalAccessToken_IsRefusedBecauseItsWebOnly(string method, string suffix)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, PmTool.Identity.AccessTokenScope.ItemsRead, PmTool.Identity.AccessTokenScope.ItemsWrite,
            PmTool.Identity.AccessTokenScope.BlueprintRead, PmTool.Identity.AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), Collection + suffix);
        request.Headers.Authorization = new("Bearer", secret);
        if (method == "POST")
        {
            request.Content = ApiRequests.Json(new { text = "a" });
        }

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        Assert.That(factory.Modules.Assistant.StartCalls, Is.Empty);
    }
}
