using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>AI 助理的批次修改建議端點：形狀、錯誤碼、只能用 Cookie。規則（誰能套用、部分成功）在 AI 模組的測試。</summary>
[TestFixture]
public sealed class ItemBatchProposalEndpointTests
{
    private static readonly string Route = $"/v1/ai/item-batch-proposals/{FakeItemBatchProposals.ProposalId}";

    private const string PendingJson = """
        {"id":"bbbbbbbb-0000-0000-0000-000000000001","projectKey":"WEB",
         "items":[
           {"itemKey":"WEB-12","itemTitle":"改版首頁","changes":[{"field":"dueDate","value":"2026-12-05"}],"comment":"順延一週","replyTo":null,"error":null},
           {"itemKey":"WEB-13","itemTitle":"整理文案","changes":[{"field":"dueDate","value":null}],"comment":null,"replyTo":null,"error":null}],
         "status":"pending","createdAt":"2026-11-27T04:10:00Z","decidedAt":null,"error":null}
        """;

    private static ApiFactory NewFactory() => new(FakeBlueprintValidator.Passing());

    [Test]
    public async Task Get_ReturnsTheProposalInTheContractShapeForTheSignedInPerson()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(PendingJson)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.ItemBatchProposals.Calls.Single(), Is.EqualTo(("get", TestUsers.Member.ToActor(), FakeItemBatchProposals.ProposalId)));
        }
    }

    [Test]
    public async Task Get_EntryThatRepliesToAComment_CarriesTheReplyTarget()
    {
        // Arrange
        using var factory = NewFactory();
        var commentId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000c1");
        var proposal = FakeItemBatchProposals.Proposal();
        factory.Modules.ItemBatchProposals.OnGet = _ => proposal with
        {
            Items = [proposal.Items[0] with { ReplyTo = new CommentReplyTarget(commentId, "測試者", "請先確認") }, proposal.Items[1]],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["items"]?[0]?["replyTo"]?["commentId"]?.GetValue<Guid>(), Is.EqualTo(commentId));
            Assert.That(data?["items"]?[0]?["replyTo"]?["authorName"]?.GetValue<string>(), Is.EqualTo("測試者"));
            Assert.That(data?["items"]?[0]?["replyTo"]?["excerpt"]?.GetValue<string>(), Is.EqualTo("請先確認"));
            Assert.That(data?["items"]?[1]?["replyTo"], Is.Null);
        }
    }

    [Test]
    public async Task Apply_ReturnsTheAppliedProposalWithEachEntrysResult()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Route + "/apply", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(data?["items"]?[0]?["error"], Is.Null);
            Assert.That(data?["items"]?[1]?["error"]?.GetValue<string>(), Does.Contain("沒有權限"));
            Assert.That(data?["error"]?.GetValue<string>(), Does.Contain("WEB-13"));
            Assert.That(factory.Modules.ItemBatchProposals.Calls.Single(), Is.EqualTo(("apply", TestUsers.Member.ToActor(), FakeItemBatchProposals.ProposalId)));
        }
    }

    [Test]
    public async Task Apply_Failure_IsReturnedWithTheEnginesCode()
    {
        // Arrange
        using var factory = NewFactory();
        factory.Modules.ItemBatchProposals.OnApply = _ => new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "你沒有修改項目的權限。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route + "/apply", null);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
    }

    [Test]
    public async Task Dismiss_ReturnsTheDismissedProposal()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Route + "/dismiss", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["status"]?.GetValue<string>(), Is.EqualTo("dismissed"));
            Assert.That(factory.Modules.ItemBatchProposals.Calls.Single().Action, Is.EqualTo("dismiss"));
        }
    }

    [TestCase("GET", "", FailureKind.NotFound, AiErrorCodes.ItemBatchProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/apply", FailureKind.NotFound, AiErrorCodes.ItemBatchProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/apply", FailureKind.Conflict, AiErrorCodes.ItemBatchProposalClosed, HttpStatusCode.Conflict)]
    [TestCase("POST", "/apply", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/dismiss", FailureKind.NotFound, AiErrorCodes.ItemBatchProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/dismiss", FailureKind.Conflict, AiErrorCodes.ItemBatchProposalClosed, HttpStatusCode.Conflict)]
    public async Task Failures_AreReturnedWithTheModulesCodeAndStatus(
        string method, string suffix, FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = NewFactory();
        var failure = new Failure(kind, code, "原因說明");
        factory.Modules.ItemBatchProposals.OnGet = _ => failure;
        factory.Modules.ItemBatchProposals.OnApply = _ => failure;
        factory.Modules.ItemBatchProposals.OnDismiss = _ => failure;
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
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/ai/item-batch-proposals/not-a-guid");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(factory.Modules.ItemBatchProposals.Calls, Is.Empty);
        }
    }

    [TestCase("GET", "")]
    [TestCase("POST", "/apply")]
    [TestCase("POST", "/dismiss")]
    public async Task EveryEndpoint_PersonalAccessToken_IsRefusedBecauseItsWebOnly(string method, string suffix)
    {
        // Arrange
        using var factory = NewFactory();
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
        Assert.That(factory.Modules.ItemBatchProposals.Calls, Is.Empty);
    }

    [Test]
    public async Task Get_NotSignedIn_Is401()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Route));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
