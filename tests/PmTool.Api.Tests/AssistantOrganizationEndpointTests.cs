using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 不選專案的組織對話端點（<c>/v1/assistant/conversations…</c>）：形狀和專案的對話一樣，交給模組的專案代號是 null，
/// 選項目或附件在送到模組之前就 422，失敗代碼照模組的說法，只能用 Cookie。規則（權限、隔離）在 AI 模組的測試。
/// </summary>
[TestFixture]
public sealed class AssistantOrganizationEndpointTests
{
    private const string Collection = "/v1/assistant/conversations";

    private static readonly string Item = $"{Collection}/{FakeAssistantService.ConversationId}";

    private static object Body(object? itemKey = null, object? attachmentIds = null) => new
    {
        text = "幫我開一個新專案",
        selection = new { connectionId = FakeAssistantService.ConnectionId, model = "claude-opus-5-5", effort = "high" },
        itemKey,
        attachmentIds,
    };

    [Test]
    public async Task List_PassesANullProjectAndReturnsTheConversations()
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
            Assert.That(data!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(factory.Modules.Assistant.ListCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), (string?)null) }));
        }
    }

    [Test]
    public async Task Create_Returns201WithTheOrganizationLocation()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Collection, null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo(Item));
            Assert.That(data?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeAssistantService.ConversationId));
            Assert.That(factory.Modules.Assistant.CreateCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), (string?)null) }));
        }
    }

    [Test]
    public async Task Get_PassesANullProjectAndReturnsTheMessages()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Item);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["messages"]?.AsArray(), Has.Count.EqualTo(2));
            Assert.That(
                factory.Modules.Assistant.GetCalls,
                Is.EqualTo(new[] { (TestUsers.Member.ToActor(), (string?)null, FakeAssistantService.ConversationId) }));
        }
    }

    [Test]
    public async Task Get_ToolPartWithAProjectProposal_CarriesProjectProposalId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var tool = new AssistantToolPart("c1", "propose_project", AssistantToolStatus.Done, null, null)
        {
            ProjectProposalId = FakeProjectProposals.ProposalId,
        };
        factory.Modules.Assistant.OnGet = (_, _, _) => new AssistantConversation(
            FakeAssistantService.Summary(messageCount: 1), [FakeAssistantService.AssistantReply() with { Parts = [tool] }]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Item);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var part = data!["messages"]![0]!["parts"]![0]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(part["projectProposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeProjectProposals.ProposalId));
            Assert.That(part["itemProposalId"], Is.Null);
        }
    }

    [Test]
    public async Task Delete_PassesANullProjectAndReturns204()
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
            Assert.That(
                factory.Modules.Assistant.DeleteCalls,
                Is.EqualTo(new[] { (TestUsers.Member.ToActor(), (string?)null, FakeAssistantService.ConversationId) }));
        }
    }

    [Test]
    public async Task Send_IsAnEventStreamAndPassesANullProject()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await SendAsync(client, Body());
        var frames = await new AssistantStreamTests.SseReader(response).ReadToEndAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
            Assert.That(frames.Select(frame => frame.Event), Does.Contain("done"));
            var call = factory.Modules.Assistant.StartCalls.Single();
            Assert.That(call.ProjectKey, Is.Null);
            Assert.That(call.Input.ItemKey, Is.Null);
        }
    }

    [Test]
    public async Task Send_ToolEventWithAProjectProposal_CarriesProjectProposalId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var tool = new AssistantToolPart("c1", "propose_project", AssistantToolStatus.Done, null, null)
        {
            ProjectProposalId = FakeProjectProposals.ProposalId,
        };
        factory.Modules.Assistant.OnStart = _ => new AssistantRun(
            FakeAssistantService.UserMessage(),
            Guid.Parse("33333333-3333-3333-3333-333333333332"),
            Events(new AssistantToolUpdate(tool), new AssistantRunCompleted(FakeAssistantService.AssistantReply() with { Parts = [tool] })));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await SendAsync(client, Body());
        var frames = await new AssistantStreamTests.SseReader(response).ReadToEndAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.Parse(frames[1].Data!)?["projectProposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeProjectProposals.ProposalId));
            Assert.That(JsonNode.Parse(frames[2].Data!)?["message"]?["parts"]?[0]?["projectProposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeProjectProposals.ProposalId));
        }
    }

    [Test]
    public async Task Send_WithAnItemKey_Is422OrganizationChatUnsupportedAndNeverReachesTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await SendAsync(client, Body(itemKey: "WEB-12"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.OrganizationChatUnsupported);
            Assert.That(factory.Modules.Assistant.StartCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Send_WithAttachments_Is422OrganizationChatUnsupportedAndNeverReachesTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await SendAsync(client, Body(attachmentIds: new[] { Guid.NewGuid() }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.OrganizationChatUnsupported);
            Assert.That(factory.Modules.Assistant.StartCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Send_WithAnEmptyAttachmentList_IsAccepted()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await SendAsync(client, Body(attachmentIds: Array.Empty<Guid>()));
        await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase(FailureKind.Forbidden, AiErrorCodes.AssistantForbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, AiErrorCodes.ConversationNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.ConversationBusy, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.AgentUpdateRequired, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Invalid, AiErrorCodes.OrganizationChatUnsupported, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.Invalid, AiErrorCodes.Unavailable, HttpStatusCode.UnprocessableEntity)]
    public async Task EveryEndpoint_ModuleFailure_BecomesTheEnvelopeWithItsCodeAndStatus(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failure = new Failure(kind, code, "原因說明");
        factory.Modules.Assistant.OnList = (_, _) => failure;
        factory.Modules.Assistant.OnCreate = (_, _) => failure;
        factory.Modules.Assistant.OnGet = (_, _, _) => failure;
        factory.Modules.Assistant.OnDelete = (_, _, _) => failure;
        factory.Modules.Assistant.OnStart = _ => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var list = await client.GetAsync(Collection);
        using var create = await client.PostAsync(Collection, null);
        using var get = await client.GetAsync(Item);
        using var delete = await client.DeleteAsync(Item);
        using var send = await SendAsync(client, Body());

        // Assert
        foreach (var response in new[] { list, create, get, delete, send })
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
        }
    }

    [Test]
    public async Task Send_MoreThanTenMessagesAMinute_Returns429()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var member = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        for (var i = 0; i < ApiLimits.AssistantMessagePermitsPerWindow; i++)
        {
            using var allowed = await SendAsync(member, Body());
            await allowed.Content.ReadAsStringAsync();
        }

        // Act
        using var tooMany = await SendAsync(member, Body());

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(tooMany, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
    }

    [TestCase("GET", Collection)]
    [TestCase("POST", Collection)]
    [TestCase("GET", "/v1/assistant/conversations/22222222-2222-2222-2222-222222222222")]
    [TestCase("DELETE", "/v1/assistant/conversations/22222222-2222-2222-2222-222222222222")]
    [TestCase("POST", "/v1/assistant/conversations/22222222-2222-2222-2222-222222222222/messages")]
    public async Task EveryEndpoint_PersonalAccessToken_IsRefusedBecauseItsWebOnly(string method, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, [.. PmTool.Identity.AccessTokenScopes.Grantable]);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new("Bearer", secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
            Assert.That(factory.Modules.Assistant.ListCalls, Is.Empty);
            Assert.That(factory.Modules.Assistant.CreateCalls, Is.Empty);
            Assert.That(factory.Modules.Assistant.StartCalls, Is.Empty);
        }
    }

    [Test]
    public async Task List_NotSignedIn_Is401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Collection);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Item}/messages") { Content = ApiRequests.Json(body) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static async IAsyncEnumerable<AssistantRunEvent> Events(params AssistantRunEvent[] events)
    {
        foreach (var runEvent in events)
        {
            yield return runEvent;
            await Task.Yield();
        }
    }
}
