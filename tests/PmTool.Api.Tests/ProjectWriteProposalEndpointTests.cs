using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>
/// AI 助理的四種新建議（週期與里程碑、改專案、留言、復原項目）的端點：形狀、錯誤碼、套用不跟著取消、只能用 Cookie。
/// 規則在 AI 模組的測試；這裡用假服務。
/// </summary>
[TestFixture]
public sealed class ProjectWriteProposalEndpointTests
{
    private sealed record Kind(
        string Segment,
        Guid ProposalId,
        string NotFoundCode,
        string ClosedCode,
        Func<ApiFactory, IReadOnlyList<(string Action, Actor Actor, Guid Id)>> Calls,
        Func<ApiFactory, IReadOnlyList<CancellationToken>> ApplyTokens,
        Action<ApiFactory, Failure> Fail)
    {
        public string Route => $"/v1/ai/{Segment}/{ProposalId}";
    }

    private static readonly Kind Planning = new(
        "planning-proposals", FakePlanningProposals.ProposalId, AiErrorCodes.PlanningProposalNotFound, AiErrorCodes.PlanningProposalClosed,
        f => f.Modules.PlanningProposals.Calls, f => f.Modules.PlanningProposals.ApplyTokens,
        (f, failure) => { var m = f.Modules.PlanningProposals; m.OnGet = _ => failure; m.OnApply = _ => failure; m.OnDismiss = _ => failure; });

    private static readonly Kind ProjectChange = new(
        "project-change-proposals", FakeProjectChangeProposals.ProposalId, AiErrorCodes.ProjectChangeProposalNotFound, AiErrorCodes.ProjectChangeProposalClosed,
        f => f.Modules.ProjectChangeProposals.Calls, f => f.Modules.ProjectChangeProposals.ApplyTokens,
        (f, failure) => { var m = f.Modules.ProjectChangeProposals; m.OnGet = _ => failure; m.OnApply = _ => failure; m.OnDismiss = _ => failure; });

    private static readonly Kind Comment = new(
        "comment-proposals", FakeCommentProposals.ProposalId, AiErrorCodes.CommentProposalNotFound, AiErrorCodes.CommentProposalClosed,
        f => f.Modules.CommentProposals.Calls, f => f.Modules.CommentProposals.ApplyTokens,
        (f, failure) => { var m = f.Modules.CommentProposals; m.OnGet = _ => failure; m.OnApply = _ => failure; m.OnDismiss = _ => failure; });

    private static readonly Kind ItemRestore = new(
        "item-restore-proposals", FakeItemRestoreProposals.ProposalId, AiErrorCodes.ItemRestoreProposalNotFound, AiErrorCodes.ItemRestoreProposalClosed,
        f => f.Modules.ItemRestoreProposals.Calls, f => f.Modules.ItemRestoreProposals.ApplyTokens,
        (f, failure) => { var m = f.Modules.ItemRestoreProposals; m.OnGet = _ => failure; m.OnApply = _ => failure; m.OnDismiss = _ => failure; });

    private static readonly Kind[] AllKinds = [Planning, ProjectChange, Comment, ItemRestore];

    private static ApiFactory NewFactory() => new(FakeBlueprintValidator.Passing());

    [Test]
    public async Task Planning_Get_ReturnsTheProposalInTheContractShape()
    {
        // Arrange
        const string expected = """
            {"id":"a1a1a1a1-0000-0000-0000-000000000001","projectKey":"WEB",
             "changes":[
               {"target":"cycle","action":"create","id":null,"before":null,
                "after":{"name":"第 49 週","startDate":"2026-11-30","endDate":"2026-12-04","dueDate":null,"description":null},"itemCount":null,"error":null},
               {"target":"milestone","action":"delete","id":"a1a1a1a1-0000-0000-0000-0000000000d1",
                "before":{"name":"舊的里程碑","startDate":null,"endDate":null,"dueDate":"2026-12-01","description":null},"after":null,"itemCount":3,"error":null}],
             "reason":"下週開始新的週期","status":"pending","createdAt":"2026-11-27T03:00:00Z","decidedAt":null,"error":null}
            """;

        // Act
        var (data, factory) = await GetAsync(Planning);
        using (factory)
        {
            // Assert
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(expected)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.PlanningProposals.Calls.Single(), Is.EqualTo(("get", TestUsers.Member.ToActor(), FakePlanningProposals.ProposalId)));
        }
    }

    [Test]
    public async Task Planning_Apply_KeepsTheApplyingStatusAsALowercaseString()
    {
        // Arrange
        using var factory = NewFactory();
        factory.Modules.PlanningProposals.OnApply = _ => FakePlanningProposals.Proposal(NewItemProposalStatus.Applying);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Planning.Route + "/apply", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["status"]?.GetValue<string>(), Is.EqualTo("applying"));
    }

    [Test]
    public async Task ProjectChange_Get_ReturnsTheProposalInTheContractShape()
    {
        // Arrange
        const string expected = """
            {"id":"a2a2a2a2-0000-0000-0000-000000000001","projectKey":"WEB","name":"官網改版","archived":null,"changesDescription":true,
             "description":"## 網址\n- https://example.com",
             "before":{"name":"官網","archived":false,"description":null,"descriptionUpdatedAt":null},
             "reason":null,"status":"pending","createdAt":"2026-11-27T03:00:00Z","decidedAt":null,"error":null}
            """;

        // Act
        var (data, factory) = await GetAsync(ProjectChange);
        using (factory)
        {
            // Assert
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(expected)), Is.True, data?.ToJsonString());
        }
    }

    [Test]
    public async Task Comment_Get_ReturnsTheProposalInTheContractShape()
    {
        // Arrange
        const string expected = """
            {"id":"a3a3a3a3-0000-0000-0000-000000000001","projectKey":"WEB",
             "changes":[{"itemKey":"WEB-12","commentId":"a3a3a3a3-0000-0000-0000-0000000000c1","action":"edit","authorName":"測試者",
                         "commentedAt":"2026-11-26T03:00:00Z","excerpt":"原本的留言","body":"改過的留言","error":null}],
             "status":"pending","createdAt":"2026-11-27T03:00:00Z","decidedAt":null,"error":null}
            """;

        // Act
        var (data, factory) = await GetAsync(Comment);
        using (factory)
        {
            // Assert
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(expected)), Is.True, data?.ToJsonString());
        }
    }

    [Test]
    public async Task ItemRestore_Apply_ReturnsRestoredKeysAndPendingHasNone()
    {
        // Arrange
        const string pending = """
            {"id":"a4a4a4a4-0000-0000-0000-000000000001","projectKey":"WEB",
             "items":[{"itemKey":"WEB-7","title":"刪錯的任務","deletedAt":"2026-11-27T01:00:00Z"}],
             "reason":"刪錯了","status":"pending","createdAt":"2026-11-27T03:00:00Z","decidedAt":null,"restoredKeys":[],"error":null}
            """;
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var getResponse = await client.GetAsync(ItemRestore.Route);
        var got = await ApiRequests.AssertSuccessEnvelopeAsync(getResponse, HttpStatusCode.OK);
        using var applyResponse = await client.PostAsync(ItemRestore.Route + "/apply", null);
        var applied = await ApiRequests.AssertSuccessEnvelopeAsync(applyResponse, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(got, JsonNode.Parse(pending)), Is.True, got?.ToJsonString());
            Assert.That(applied?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(applied?["restoredKeys"]?[0]?.GetValue<string>(), Is.EqualTo("WEB-7"));
        }
    }

    private static IEnumerable<string> Kinds() => AllKinds.Select(kind => kind.Segment);

    private static Kind Of(string segment) => AllKinds.Single(kind => kind.Segment == segment);

    [TestCaseSource(nameof(Kinds))]
    public async Task Apply_AndDismiss_AreCalledAsTheSignedInPerson(string segment)
    {
        var kind = Of(segment);
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var apply = await client.PostAsync(kind.Route + "/apply", null);
        var applied = await ApiRequests.AssertSuccessEnvelopeAsync(apply, HttpStatusCode.OK);
        using var dismiss = await client.PostAsync(kind.Route + "/dismiss", null);
        var dismissed = await ApiRequests.AssertSuccessEnvelopeAsync(dismiss, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(dismissed?["status"]?.GetValue<string>(), Is.EqualTo("dismissed"));
            Assert.That(kind.Calls(factory), Is.EqualTo(new[]
            {
                ("apply", TestUsers.Member.ToActor(), kind.ProposalId),
                ("dismiss", TestUsers.Member.ToActor(), kind.ProposalId),
            }));
        }
    }

    [TestCaseSource(nameof(Kinds))]
    public async Task Apply_DoesNotFollowTheRequestsCancellation(string segment)
    {
        var kind = Of(segment);
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(kind.Route + "/apply", null);
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(kind.ApplyTokens(factory).Single().CanBeCanceled, Is.False);
    }

    [TestCaseSource(nameof(Kinds))]
    public async Task Failures_AreReturnedWithTheModulesCodeAndStatus(string segment)
    {
        var kind = Of(segment);
        // Arrange
        var cases = new (string Method, string Suffix, Failure Failure, HttpStatusCode Status)[]
        {
            ("GET", "", new Failure(FailureKind.NotFound, kind.NotFoundCode, "找不到"), HttpStatusCode.NotFound),
            ("POST", "/apply", new Failure(FailureKind.NotFound, kind.NotFoundCode, "找不到"), HttpStatusCode.NotFound),
            ("POST", "/apply", new Failure(FailureKind.Conflict, kind.ClosedCode, "已經處理過"), HttpStatusCode.Conflict),
            ("POST", "/apply", new Failure(FailureKind.Conflict, AiErrorCodes.ProjectChangeConflict, "說明被改過"), HttpStatusCode.Conflict),
            ("POST", "/apply", new Failure(FailureKind.Conflict, AiErrorCodes.ApplyTimedOut, "套用逾時"), HttpStatusCode.Conflict),
            ("POST", "/apply", new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限"), HttpStatusCode.Forbidden),
            ("POST", "/dismiss", new Failure(FailureKind.NotFound, kind.NotFoundCode, "找不到"), HttpStatusCode.NotFound),
            ("POST", "/dismiss", new Failure(FailureKind.Conflict, kind.ClosedCode, "已經處理過"), HttpStatusCode.Conflict),
        };

        foreach (var (method, suffix, failure, status) in cases)
        {
            using var factory = NewFactory();
            kind.Fail(factory, failure);
            using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

            // Act
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), kind.Route + suffix));

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(response, status, failure.Code);
        }
    }

    [TestCaseSource(nameof(Kinds))]
    public async Task Route_IdThatIsNotAGuid_Is404WithoutCallingTheModule(string segment)
    {
        var kind = Of(segment);
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"/v1/ai/{kind.Segment}/not-a-guid");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(kind.Calls(factory), Is.Empty);
        }
    }

    [TestCaseSource(nameof(Kinds))]
    public async Task EveryEndpoint_PersonalAccessToken_IsRefusedBecauseItsWebOnly(string segment)
    {
        var kind = Of(segment);
        // Arrange
        using var factory = NewFactory();
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, PmTool.Identity.AccessTokenScope.ItemsRead, PmTool.Identity.AccessTokenScope.ItemsWrite,
            PmTool.Identity.AccessTokenScope.BlueprintRead, PmTool.Identity.AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();

        foreach (var (method, suffix) in new[] { ("GET", ""), ("POST", "/apply"), ("POST", "/dismiss") })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), kind.Route + suffix);
            request.Headers.Authorization = new("Bearer", secret);

            // Act
            using var response = await client.SendAsync(request);

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        }

        Assert.That(kind.Calls(factory), Is.Empty);
    }

    [TestCaseSource(nameof(Kinds))]
    public async Task Get_NotSignedIn_Is401(string segment)
    {
        var kind = Of(segment);
        // Arrange
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(kind.Route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static async Task<(JsonNode? Data, ApiFactory Factory)> GetAsync(Kind kind)
    {
        var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var response = await client.GetAsync(kind.Route);
        return (await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK), factory);
    }
}
