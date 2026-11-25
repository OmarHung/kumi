using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Notifications;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>/v1/notifications 與 /v1/items/{key}/subscription：形狀、查詢參數、錯誤與權限。</summary>
[TestFixture]
public sealed class NotificationEndpointTests
{
    private static readonly Guid NotificationId = Guid.Parse("00000000-0000-7000-8000-0000000000c1");

    private static readonly Guid CommentId = Guid.Parse("00000000-0000-7000-8000-0000000000d2");

    private static readonly DateTimeOffset Created = new(2026, 11, 18, 3, 4, 5, TimeSpan.Zero);

    private static NotificationItem Item(NotificationKind kind, Guid actor, string? from = null, string? to = null, DateTimeOffset? readAt = null) =>
        new(NotificationId, kind, "WEB", "WEB-7", "範例項目", actor, from, to, kind is NotificationKind.Commented ? "留言內容" : null, Created, readAt,
            kind is NotificationKind.Commented ? CommentId : null);

    [Test]
    public async Task List_SignedIn_ReturnsTheContractShapeWithTheActorsName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnList = (_, _) => new NotificationPage(
            [Item(NotificationKind.StateChanged, TestUsers.Owner.PersonId, "待辦", "審查中")], "next-page");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse($$"""
            {"items":[{"notificationId":"{{NotificationId}}","kind":"state_changed","projectKey":"WEB","itemKey":"WEB-7",
            "itemTitle":"範例項目","actor":{"personId":"{{TestUsers.Owner.PersonId}}","displayName":"{{TestUsers.Owner.DisplayName}}"},
            "fromStateName":"待辦","toStateName":"審查中","excerpt":null,"commentId":null,"createdAt":"2026-11-18T03:04:05Z","readAt":null,"edited":false,"account":null}],
            "nextCursor":"next-page"}
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task List_AccountNotice_HasNoProjectAndWritesTheAccountShape()
    {
        // Arrange
        var account = new AccountNotice(
            AccountNoticeEvent.CliLoginTokensIssued,
            [new AccountNoticeToken("kumi（MacBook-Pro）", ["items:read"], new DateTimeOffset(2027, 2, 23, 0, 0, 0, TimeSpan.Zero))],
            [],
            "203.0.113.5",
            "MacBook-Pro",
            null,
            null,
            0);
        var notice = new NotificationItem(
            NotificationId, NotificationKind.AccountSecurity, null, null, null, TestUsers.Owner.PersonId, null, null, null, Created, null, null)
        {
            Account = account,
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnList = (_, _) => new NotificationPage([notice], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse($$$"""
            {"items":[{"notificationId":"{{{NotificationId}}}","kind":"account_security","projectKey":null,"itemKey":null,
            "itemTitle":null,"actor":{"personId":"{{{TestUsers.Owner.PersonId}}}","displayName":"{{{TestUsers.Owner.DisplayName}}}"},
            "fromStateName":null,"toStateName":null,"excerpt":null,"commentId":null,"createdAt":"2026-11-18T03:04:05Z","readAt":null,"edited":false,
            "account":{"event":"cli_login_tokens_issued",
            "tokens":[{"name":"kumi（MacBook-Pro）","scopes":["items:read"],"expiresAt":"2027-02-23T00:00:00Z"}],
            "scopes":[],"sourceAddress":"203.0.113.5","machineName":"MacBook-Pro","clientName":null,"clientHost":null,"revokedTokens":0}}],
            "nextCursor":null}
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [TestCase(AccountNoticeEvent.AccessTokenCreated, "access_token_created")]
    [TestCase(AccountNoticeEvent.CliLoginTokensIssued, "cli_login_tokens_issued")]
    [TestCase(AccountNoticeEvent.OauthAppAuthorized, "oauth_app_authorized")]
    [TestCase(AccountNoticeEvent.CliLoginCodeReused, "cli_login_code_reused")]
    public async Task List_EachAccountEvent_IsWrittenInSnakeCase(AccountNoticeEvent kind, string expected)
    {
        // Arrange
        var notice = new NotificationItem(
            NotificationId, NotificationKind.AccountSecurity, null, null, null, TestUsers.Owner.PersonId, null, null, null, Created, null, null)
        {
            Account = new AccountNotice(kind, [], [], null, null, null, null, 0),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnList = (_, _) => new NotificationPage([notice], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["items"]?[0]?["account"]?["event"]?.GetValue<string>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task List_EditedCommentNotification_WritesEditedTrue()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnList = (_, _) => new NotificationPage(
            [Item(NotificationKind.Commented, TestUsers.Owner.PersonId) with { Edited = true }], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["items"]?[0]?["edited"]?.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task List_CommentNotification_WritesTheCommentIdAsAString()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnList = (_, _) => new NotificationPage([Item(NotificationKind.Commented, TestUsers.Owner.PersonId)], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["items"]?[0]?["commentId"]?.GetValue<string>(), Is.EqualTo(CommentId.ToString()));
    }

    [TestCase(NotificationKind.Assigned, "assigned")]
    [TestCase(NotificationKind.Mentioned, "mentioned")]
    [TestCase(NotificationKind.Commented, "commented")]
    [TestCase(NotificationKind.StateChanged, "state_changed")]
    public async Task List_EachKind_IsWrittenInSnakeCase(NotificationKind kind, string expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnList = (_, _) => new NotificationPage([Item(kind, TestUsers.Owner.PersonId)], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["items"]?[0]?["kind"]?.GetValue<string>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task List_ActorNoLongerInTheDirectory_ShowsTheFallbackName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnList = (_, _) => new NotificationPage(
            [Item(NotificationKind.Assigned, Guid.Parse("00000000-0000-7000-8000-0000000000ee"))], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["items"]?[0]?["actor"]?["displayName"]?.GetValue<string>(), Is.EqualTo("查不到的人"));
    }

    [Test]
    public async Task List_NoParameters_AsksForTheFirstPageOfEverything()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications");
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var (actor, query) = factory.Modules.Notifications.ListCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(query, Is.EqualTo(new NotificationQuery(false, null, NotificationRules.DefaultPageSize)));
        }
    }

    [Test]
    public async Task List_WithParameters_PassesThemToTheInbox()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications?unread=true&cursor=abc&limit=7");
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(factory.Modules.Notifications.ListCalls.Single().Query, Is.EqualTo(new NotificationQuery(true, "abc", 7)));
    }

    [TestCase("unread=yes", "unread")]
    [TestCase("unread=true&unread=false", "unread")]
    [TestCase("limit=abc", "limit")]
    [TestCase("limit=-1", "limit")]
    [TestCase("cursor=a&cursor=b", "cursor")]
    public async Task List_BadParameters_Return400OnTheParameterWithoutCallingTheInbox(string query, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"/v1/notifications?{query}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, NotificationErrorCodes.QueryInvalid);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{NotificationErrorCodes.QueryInvalid}|{path}" }));
            Assert.That(factory.Modules.Notifications.ListCalls, Is.Empty);
        }
    }

    [TestCase("limit", "limit=0")]
    [TestCase("limit", "limit=101")]
    [TestCase("cursor", "cursor=garbage")]
    public async Task List_InboxRejectsTheQuery_Returns400WithItsDetails(string path, string query)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnList = (_, _) =>
            new Failure(FailureKind.Invalid, NotificationErrorCodes.QueryInvalid, "查詢參數不正確。")
            {
                Details = [new FailureDetail(NotificationErrorCodes.QueryInvalid, path, "不正確。")],
            };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"/v1/notifications?{query}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, NotificationErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{NotificationErrorCodes.QueryInvalid}|{path}" }));
    }

    [Test]
    public async Task UnreadCount_SignedIn_ReturnsTheCount()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnCountUnread = _ => 3;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/notifications/unread-count");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"count":3}""")), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task MarkRead_Ids_PassesTheDistinctIdsAndReturnsTheUpdatedCount()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnMarkRead = (_, _) => 2;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        // Act
        using var response = await client.PostAsync(
            "/v1/notifications/read", ApiRequests.Json(new { notificationIds = new[] { first, second, first } }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["updated"]?.GetValue<int>(), Is.EqualTo(2));
            Assert.That(factory.Modules.Notifications.MarkReadCalls.Single().Ids, Is.EqualTo(new[] { first, second }));
        }
    }

    [Test]
    public async Task MarkRead_TwoHundredIds_IsAccepted()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var ids = Enumerable.Range(0, NotificationRules.MaxMarkReadIds).Select(_ => Guid.NewGuid()).ToArray();

        // Act
        using var response = await client.PostAsync("/v1/notifications/read", ApiRequests.Json(new { notificationIds = ids }));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task MarkRead_MoreThanTwoHundredIds_Returns400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var ids = Enumerable.Range(0, NotificationRules.MaxMarkReadIds + 1).Select(_ => Guid.NewGuid()).ToArray();

        // Act
        using var response = await client.PostAsync("/v1/notifications/read", ApiRequests.Json(new { notificationIds = ids }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{ApiErrorCodes.BadRequest}|notificationIds" }));
            Assert.That(factory.Modules.Notifications.MarkReadCalls, Is.Empty);
        }
    }

    [TestCase("""{"notificationIds":[]}""", "notificationIds")]
    [TestCase("""{"notificationIds":["not-a-guid"]}""", "notificationIds[0]")]
    [TestCase("""{"notificationIds":null}""", "notificationIds")]
    [TestCase("""{}""", "")]
    [TestCase("""{"notificationIds":[],"extra":1}""", "extra")]
    public async Task MarkRead_BadBody_Returns400(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/notifications/read", ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details.Select(d => d?["path"]?.GetValue<string>()).First(), Is.EqualTo(path));
            Assert.That(factory.Modules.Notifications.MarkReadCalls, Is.Empty);
        }
    }

    [Test]
    public async Task MarkAllRead_SignedIn_ReturnsTheUpdatedCount()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Notifications.OnMarkAllRead = _ => 5;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/notifications/read-all", content: null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["updated"]?.GetValue<int>(), Is.EqualTo(5));
            Assert.That(factory.Modules.Notifications.MarkAllReadCalls, Is.EqualTo(new[] { TestUsers.Member.ToActor() }));
        }
    }

    [TestCase("GET", "/v1/notifications")]
    [TestCase("GET", "/v1/notifications/unread-count")]
    [TestCase("POST", "/v1/notifications/read-all")]
    [TestCase("GET", "/v1/items/WEB-1/subscription")]
    public async Task Endpoints_NotSignedIn_Return401(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task GetSubscription_SignedIn_ResolvesTheItemFirstAndReturnsTheState()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Subscriptions.OnGet = (_, _, _) => new ItemSubscription(true, SubscriptionReason.Assignee, true);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-7/subscription");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"following":true,"reason":"assignee","canFollow":true}""")), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Access.ResolveCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "WEB-7")));
            Assert.That(factory.Modules.Subscriptions.GetCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), FakeWorkAccessQueries.ProjectId, FakeWorkAccessQueries.ItemId)));
        }
    }

    [Test]
    public async Task GetSubscription_NotFollowing_WritesAnExplicitNullReasonAndCanFollow()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-7/subscription");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"following":false,"reason":null,"canFollow":true}""")), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task GetSubscription_NotAProjectMember_WritesCanFollowFalse()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Subscriptions.OnGet = (_, _, _) => new ItemSubscription(false, null, false);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-7/subscription");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"following":false,"reason":null,"canFollow":false}""")), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task SetSubscription_NotAProjectMember_Returns409WithTheNotMemberCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Subscriptions.OnSet = (_, _, _, _) =>
            new Failure(FailureKind.Conflict, NotificationErrorCodes.NotProjectMember, "要先加入這個專案，才能關注它的項目。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync("/v1/items/WEB-7/subscription", ApiRequests.Json(new { following = true }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, "subscription.not_member");
    }

    [TestCase("GET")]
    [TestCase("PUT")]
    public async Task Subscription_ItemNotVisible_Returns404WithoutTouchingTheSubscription(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Access.OnResolve = (_, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, "找不到這個項目。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var request = new HttpRequestMessage(new HttpMethod(method), "/v1/items/WEB-9/subscription");
        if (method == "PUT")
        {
            request.Content = ApiRequests.Json(new { following = true });
        }

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
            Assert.That(factory.Modules.Subscriptions.GetCalls, Is.Empty);
            Assert.That(factory.Modules.Subscriptions.SetCalls, Is.Empty);
        }
    }

    [TestCase(true, "manual")]
    [TestCase(false, null)]
    public async Task SetSubscription_Following_PassesTheProjectAndItemAndReturnsTheState(bool following, string? reason)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync("/v1/items/WEB-7/subscription", ApiRequests.Json(new { following }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["following"]?.GetValue<bool>(), Is.EqualTo(following));
            Assert.That(data?["reason"]?.GetValue<string>(), Is.EqualTo(reason));
            Assert.That(
                factory.Modules.Subscriptions.SetCalls.Single(),
                Is.EqualTo((TestUsers.Member.ToActor(), FakeWorkAccessQueries.ProjectId, FakeWorkAccessQueries.ItemId, following)));
        }
    }

    [TestCase("""{}""")]
    [TestCase("""{"following":"yes"}""")]
    [TestCase("""{"following":null}""")]
    [TestCase("""{"following":true,"extra":1}""")]
    public async Task SetSubscription_BadBody_Returns400(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync("/v1/items/WEB-7/subscription", ApiRequests.Document(body));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
            Assert.That(factory.Modules.Subscriptions.SetCalls, Is.Empty);
        }
    }
}
