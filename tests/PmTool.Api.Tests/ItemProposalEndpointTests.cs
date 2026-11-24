using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>AI 助理的修改建議端點：形狀、錯誤碼、只能用 Cookie。規則（誰能套用、順序）在 AI 模組的測試。</summary>
[TestFixture]
public sealed class ItemProposalEndpointTests
{
    private static readonly string Route = $"/v1/ai/item-proposals/{FakeItemProposals.ProposalId}";

    private const string PendingJson = """
        {"id":"44444444-4444-4444-4444-444444444444","projectKey":"WEB","itemKey":"WEB-12","itemTitle":"改版首頁",
         "changes":[{"field":"status","value":"done"},{"field":"dueDate","value":null}],
         "comment":"做完了","status":"pending","createdAt":"2026-11-23T03:00:00Z","decidedAt":null,"error":null}
        """;

    [Test]
    public async Task Get_ReturnsTheProposalInTheContractShapeForTheSignedInPerson()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(PendingJson)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.ItemProposals.Calls.Single(), Is.EqualTo(("get", TestUsers.Member.ToActor(), FakeItemProposals.ProposalId)));
        }
    }

    [Test]
    public async Task Apply_ReturnsTheAppliedProposalWithTheDecisionTimeAndTheError()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.ItemProposals.OnApply = _ => FakeItemProposals.Proposal(ItemProposalStatus.Applied) with { };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Route + "/apply", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(data?["decidedAt"]?.GetValue<string>(), Is.EqualTo("2026-11-23T03:05:00Z"));
            Assert.That(factory.Modules.ItemProposals.Calls.Single().Action, Is.EqualTo("apply"));
        }
    }

    [Test]
    public async Task Dismiss_ReturnsTheDismissedProposal()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Route + "/dismiss", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["status"]?.GetValue<string>(), Is.EqualTo("dismissed"));
            Assert.That(factory.Modules.ItemProposals.Calls.Single().Action, Is.EqualTo("dismiss"));
        }
    }

    [TestCase("GET", "", FailureKind.NotFound, AiErrorCodes.ItemProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/apply", FailureKind.NotFound, AiErrorCodes.ItemProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/apply", FailureKind.Conflict, AiErrorCodes.ItemProposalClosed, HttpStatusCode.Conflict)]
    [TestCase("POST", "/apply", FailureKind.Conflict, WorkErrorCodes.ItemDescriptionConflict, HttpStatusCode.Conflict)]
    [TestCase("POST", "/apply", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/apply", FailureKind.Invalid, WorkErrorCodes.TransitionNotAllowed, HttpStatusCode.UnprocessableEntity)]
    [TestCase("POST", "/dismiss", FailureKind.NotFound, AiErrorCodes.ItemProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/dismiss", FailureKind.Conflict, AiErrorCodes.ItemProposalClosed, HttpStatusCode.Conflict)]
    public async Task Failures_AreReturnedWithTheModulesCodeAndStatus(
        string method, string suffix, FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failure = new Failure(kind, code, "原因說明");
        factory.Modules.ItemProposals.OnGet = _ => failure;
        factory.Modules.ItemProposals.OnApply = _ => failure;
        factory.Modules.ItemProposals.OnDismiss = _ => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Route + suffix));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Route_IdThatIsNotAGuid_Is404WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/ai/item-proposals/not-a-guid");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(factory.Modules.ItemProposals.Calls, Is.Empty);
        }
    }

    [TestCase("GET", "")]
    [TestCase("POST", "/apply")]
    [TestCase("POST", "/dismiss")]
    public async Task EveryEndpoint_PersonalAccessToken_IsRefusedBecauseItsWebOnly(string method, string suffix)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, PmTool.Identity.AccessTokenScope.ItemsRead, PmTool.Identity.AccessTokenScope.ItemsWrite,
            PmTool.Identity.AccessTokenScope.BlueprintRead, PmTool.Identity.AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), Route + suffix);
        request.Headers.Authorization = new("Bearer", secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        Assert.That(factory.Modules.ItemProposals.Calls, Is.Empty);
    }

    [Test]
    public async Task Get_NotSignedIn_Is401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Route));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
