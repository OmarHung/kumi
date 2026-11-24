using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 通知的端到端：工作引擎的寫入留下事件，背景處理（這裡直接跑一輪）交給通知，收件匣、未讀數與關注反映出來。
/// 兩個成員：A 是組織擁有者，B 是專案的編輯者。
/// </summary>
[TestFixture]
public sealed class NotificationFlowTests : RealStackTestBase
{
    private const string BName = "Member B Example";

    private SignedInClient _a = null!;
    private SignedInClient _b = null!;

    /// <summary>
    /// 整個類別只登入兩次：登入有限流，每個測試各登入兩次會超過額度。每個測試用自己的專案（代號不同），
    /// 通知又是依項目與人分開的，所以共用登入不會互相影響。
    /// </summary>
    [OneTimeSetUp]
    public async Task SignInBothMembersAsync()
    {
        _a = await SignInAsOwnerAsync();
        var email = RealStackSteps.UniqueEmail("member-b");
        await RealStackSteps.CreateMemberAsync(_a.Client, BName, email);
        _b = await SignInAsAsync(email, RealStackSteps.MemberPassword);
    }

    [OneTimeTearDown]
    public void DisposeClients()
    {
        _a?.Dispose();
        _b?.Dispose();
    }

    private async Task<(SignedInClient A, SignedInClient B)> StartAsync(string projectKey)
    {
        await RealStackSteps.CreateProjectAsync(_a.Client, projectKey, "simple_tasks");
        await RealStackSteps.SetMemberAsync(_a.Client, projectKey, _b.PersonId, "editor");
        return (_a, _b);
    }

    /// <summary>
    /// 這個專案的通知（新的在前）。B 的收件匣是整個類別共用的，所以每個測試只看自己專案的；
    /// 清單與未讀數也因此不受別的測試影響。
    /// </summary>
    private static async Task<JsonNode[]> InboxAsync(SignedInClient who, string projectKey, bool unreadOnly = false)
    {
        var page = await RealStackSteps.GetAsync(who.Client, $"/v1/notifications?limit=100{(unreadOnly ? "&unread=true" : string.Empty)}");
        return [.. page["items"]!.AsArray().Select(n => n!).Where(n => n["projectKey"]?.GetValue<string>() == projectKey)];
    }

    private static async Task<int> UnreadAsync(SignedInClient who) =>
        (await RealStackSteps.GetAsync(who.Client, "/v1/notifications/unread-count"))["count"]!.GetValue<int>();

    private static string?[] KindsOf(IEnumerable<JsonNode> notifications) =>
        [.. notifications.Select(n => n["kind"]?.GetValue<string>())];

    private async Task RunAsync()
    {
        var handed = await Factory.RunNotificationsOnceAsync();
        Assert.That(handed, Is.GreaterThan(0), "背景處理要交出新的事件（0 也可能是出錯被記下來了）");
    }

    [Test]
    public async Task Comment_MentioningAMember_GivesThemOneUnreadMentionUntilTheyReadIt()
    {
        // Arrange
        var (a, b) = await StartAsync("NTMEN");
        await RealStackSteps.CreateItemAsync(a.Client, "NTMEN", new { type = "task", title = "請 B 看一下" });

        // Act
        await RealStackSteps.CommentAsync(a.Client, "NTMEN-1", $"麻煩 @[{BName}](mention:{b.PersonId}) 看一下");
        await RunAsync();
        var unreadBefore = (await InboxAsync(b, "NTMEN", unreadOnly: true)).Length;
        var inbox = await InboxAsync(b, "NTMEN");
        var notificationId = inbox[0]!["notificationId"]!.GetValue<Guid>();
        using var marked = await b.Client.PostAsync("/v1/notifications/read", ApiRequests.Json(new { notificationIds = new[] { notificationId } }));
        var updated = await ApiRequests.AssertSuccessEnvelopeAsync(marked, HttpStatusCode.OK);
        var unreadAfter = (await InboxAsync(b, "NTMEN", unreadOnly: true)).Length;

        // Assert
        var notification = inbox[0]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unreadBefore, Is.EqualTo(1));
            Assert.That(inbox, Has.Length.EqualTo(1));
            Assert.That(notification["kind"]?.GetValue<string>(), Is.EqualTo("mentioned"));
            Assert.That((notification["projectKey"]?.GetValue<string>(), notification["itemKey"]?.GetValue<string>()), Is.EqualTo(("NTMEN", "NTMEN-1")));
            Assert.That(notification["itemTitle"]?.GetValue<string>(), Is.EqualTo("請 B 看一下"));
            Assert.That(notification["actor"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(a.PersonId));
            Assert.That(notification["actor"]?["displayName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That(notification["excerpt"]?.GetValue<string>(), Does.Contain($"@{BName}"));
            Assert.That(notification["readAt"], Is.Null);
            Assert.That(updated?["updated"]?.GetValue<int>(), Is.EqualTo(1));
            Assert.That(unreadAfter, Is.Zero);
            Assert.That(await InboxAsync(a, "NTMEN"), Is.Empty, "自己做的事自己不收");
        }
    }

    [Test]
    public async Task Comment_NotifiesTheFollowerWithTheIdOfThatComment_AndOtherKindsHaveNone()
    {
        // Arrange
        var (a, b) = await StartAsync("NTCID");
        await RealStackSteps.CreateItemAsync(a.Client, "NTCID", new { type = "task", title = "留言編號", assigneeId = b.PersonId });
        await RealStackSteps.TransitionThroughAsync(a.Client, "NTCID-1", "doing");

        // Act
        var comment = await RealStackSteps.CommentAsync(a.Client, "NTCID-1", "第一則");
        var second = await RealStackSteps.CommentAsync(a.Client, "NTCID-1", $"@[{BName}](mention:{b.PersonId}) 第二則");
        await RunAsync();

        // Assert
        var inbox = await InboxAsync(b, "NTCID");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inbox.Single(n => n["excerpt"]?.GetValue<string>() == "第一則")["commentId"]?.GetValue<Guid>(), Is.EqualTo(comment["id"]?.GetValue<Guid>()));
            Assert.That(inbox.Single(n => n["kind"]?.GetValue<string>() == "mentioned")["commentId"]?.GetValue<Guid>(), Is.EqualTo(second["id"]?.GetValue<Guid>()));
            Assert.That(inbox.Where(n => n["kind"]?.GetValue<string>() is "assigned" or "state_changed").Select(n => RealStackSteps.IsWrittenAsNull(n, "commentId")), Is.All.True);
        }
    }

    [Test]
    public async Task Reply_NotifiesTheAuthorOfTheTopLevelCommentAsReplied_AndAnUnknownReplyToIs404()
    {
        // Arrange
        var (a, b) = await StartAsync("NTREP");
        await RealStackSteps.CreateItemAsync(a.Client, "NTREP", new { type = "task", title = "回覆" });
        var top = await RealStackSteps.CommentAsync(b.Client, "NTREP-1", "最上層");
        var topId = top["id"]!.GetValue<Guid>();

        // Act
        using var reply = await b.Client.PostAsync("/v1/items/NTREP-1/comments", ApiRequests.Json(new { body = "自己補充", replyTo = topId }));
        using var reply2 = await a.Client.PostAsync("/v1/items/NTREP-1/comments", ApiRequests.Json(new { body = "收到", replyTo = topId }));
        var replied = await ApiRequests.AssertSuccessEnvelopeAsync(reply2, HttpStatusCode.Created);
        using var unknown = await a.Client.PostAsync("/v1/items/NTREP-1/comments", ApiRequests.Json(new { body = "回誰", replyTo = Guid.NewGuid() }));
        await RunAsync();

        // Assert
        await ApiRequests.AssertSuccessEnvelopeAsync(reply, HttpStatusCode.Created);
        await ApiRequests.AssertFailureEnvelopeAsync(unknown, HttpStatusCode.NotFound, WorkErrorCodes.CommentNotFound);
        var inbox = await InboxAsync(b, "NTREP");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replied?["replyToId"]?.GetValue<Guid>(), Is.EqualTo(topId));
            Assert.That(inbox.Select(n => n["kind"]?.GetValue<string>()), Is.EqualTo(new[] { "replied" }), "自己回覆自己不通知");
            Assert.That(inbox.Single()["commentId"]?.GetValue<Guid>(), Is.EqualTo(replied?["id"]?.GetValue<Guid>()));
        }
    }

    [Test]
    public async Task Create_AssignedToAMember_NotifiesThemAsAssigned()
    {
        // Arrange
        var (a, b) = await StartAsync("NTASG");

        // Act
        await RealStackSteps.CreateItemAsync(a.Client, "NTASG", new { type = "task", title = "給 B 的任務", assigneeId = b.PersonId });
        await RunAsync();

        // Assert
        var inbox = await InboxAsync(b, "NTASG");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(KindsOf(inbox), Is.EqualTo(new[] { "assigned" }));
            Assert.That(inbox[0]["itemKey"]?.GetValue<string>(), Is.EqualTo("NTASG-1"));
            Assert.That(await InboxAsync(a, "NTASG"), Is.Empty);
        }
    }

    [Test]
    public async Task StateChange_NotifiesFollowersButNotTheOneWhoDidIt()
    {
        // Arrange：B 是負責人所以自動關注。
        var (a, b) = await StartAsync("NTSTA");
        await RealStackSteps.CreateItemAsync(a.Client, "NTSTA", new { type = "task", title = "改狀態", assigneeId = b.PersonId });

        // Act
        await RealStackSteps.TransitionThroughAsync(a.Client, "NTSTA-1", "doing");
        await RunAsync();

        // Assert
        var inbox = await InboxAsync(b, "NTSTA");
        var stateChange = inbox.Single(n => n["kind"]?.GetValue<string>() == "state_changed");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inbox, Has.Length.EqualTo(2));
            Assert.That(stateChange["fromStateName"]?.GetValue<string>(), Is.EqualTo("待辦"));
            Assert.That(stateChange["toStateName"]?.GetValue<string>(), Is.EqualTo("進行中"));
            Assert.That(await InboxAsync(a, "NTSTA"), Is.Empty);
        }
    }

    [Test]
    public async Task Unfollow_StopsStateChangeNotificationsButAssignmentStillCounts()
    {
        // Arrange
        var (a, b) = await StartAsync("NTUNF");
        await RealStackSteps.CreateItemAsync(a.Client, "NTUNF", new { type = "task", title = "取消關注", assigneeId = b.PersonId });
        await RunAsync();
        var following = await RealStackSteps.GetAsync(b.Client, "/v1/items/NTUNF-1/subscription");

        // Act
        using var unfollow = await b.Client.PutAsync("/v1/items/NTUNF-1/subscription", ApiRequests.Json(new { following = false }));
        var afterUnfollow = await ApiRequests.AssertSuccessEnvelopeAsync(unfollow, HttpStatusCode.OK);
        await RealStackSteps.TransitionThroughAsync(a.Client, "NTUNF-1", "doing");
        await RunAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((following["following"]?.GetValue<bool>(), following["reason"]?.GetValue<string>()), Is.EqualTo((true, "assignee")));
            Assert.That(afterUnfollow?["following"]?.GetValue<bool>(), Is.False);
            Assert.That(RealStackSteps.IsWrittenAsNull(afterUnfollow, "reason"), Is.True);
            Assert.That(KindsOf(await InboxAsync(b, "NTUNF")), Is.EqualTo(new[] { "assigned" }));
        }
    }

    [Test]
    public async Task Follow_ManuallyOnAnItemOfTheProject_ThenReceivesComments()
    {
        // Arrange
        var (a, b) = await StartAsync("NTFOL");
        await RealStackSteps.CreateItemAsync(a.Client, "NTFOL", new { type = "task", title = "手動關注" });

        // Act
        using var follow = await b.Client.PutAsync("/v1/items/NTFOL-1/subscription", ApiRequests.Json(new { following = true }));
        var followed = await ApiRequests.AssertSuccessEnvelopeAsync(follow, HttpStatusCode.OK);
        await RealStackSteps.CommentAsync(a.Client, "NTFOL-1", "有進展了");
        await RunAsync();

        // Assert
        var inbox = await InboxAsync(b, "NTFOL");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(followed?["reason"]?.GetValue<string>(), Is.EqualTo("manual"));
            Assert.That(KindsOf(inbox), Is.EqualTo(new[] { "commented" }));
            Assert.That(inbox[0]["excerpt"]?.GetValue<string>(), Is.EqualTo("有進展了"));
        }
    }

    [Test]
    public async Task MarkAllRead_ClearsTheUnreadCountAndKeepsTheListPaged()
    {
        // Arrange
        var (a, b) = await StartAsync("NTALL");
        for (var i = 1; i <= 3; i++)
        {
            await RealStackSteps.CreateItemAsync(a.Client, "NTALL", new { type = "task", title = $"任務 {i}", assigneeId = b.PersonId });
        }

        await RunAsync();

        // Act：B 的收件匣是共用的，分頁只確認新的在前、接得起來。
        var firstPage = await RealStackSteps.GetAsync(b.Client, "/v1/notifications?limit=2");
        var secondPage = await RealStackSteps.GetAsync(b.Client, $"/v1/notifications?limit=2&cursor={Uri.EscapeDataString(firstPage["nextCursor"]!.GetValue<string>())}");
        using var readAll = await b.Client.PostAsync("/v1/notifications/read-all", content: null);
        var updated = await ApiRequests.AssertSuccessEnvelopeAsync(readAll, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPage["items"]!.AsArray().Select(n => n?["itemKey"]?.GetValue<string>()), Is.EqualTo(new[] { "NTALL-3", "NTALL-2" }));
            Assert.That(secondPage["items"]!.AsArray()[0]?["itemKey"]?.GetValue<string>(), Is.EqualTo("NTALL-1"));
            Assert.That(updated?["updated"]?.GetValue<int>(), Is.GreaterThanOrEqualTo(3));
            Assert.That(await UnreadAsync(b), Is.Zero);
        }
    }

    [Test]
    public async Task Subscription_ItemOfAProjectTheMemberCannotSee_Returns404()
    {
        // Arrange
        var (a, b) = await StartAsync("NTVIS");
        await RealStackSteps.CreateProjectAsync(a.Client, "NTHID", "simple_tasks");
        await RealStackSteps.CreateItemAsync(a.Client, "NTHID", new { type = "task", title = "B 看不到" });

        // Act
        using var get = await b.Client.GetAsync("/v1/items/NTHID-1/subscription");
        using var put = await b.Client.PutAsync("/v1/items/NTHID-1/subscription", ApiRequests.Json(new { following = true }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(get, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(put, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task Subscription_OrganizationAdminWhoIsNotAProjectMember_CannotFollow()
    {
        // Arrange：A 是組織擁有者，看得到每個專案；把 B 升成專案管理員之後，A 退出專案，只剩組織的身分。
        var (a, b) = await StartAsync("NTADM");
        await RealStackSteps.SetMemberAsync(a.Client, "NTADM", b.PersonId, "admin");
        await RealStackSteps.CreateItemAsync(a.Client, "NTADM", new { type = "task", title = "管理員沒加入" });
        using var leave = await a.Client.DeleteAsync($"/v1/projects/NTADM/members/{a.PersonId}");
        await ApiRequests.AssertSuccessEnvelopeAsync(leave, HttpStatusCode.OK);

        // Act
        var asAdmin = await RealStackSteps.GetAsync(a.Client, "/v1/items/NTADM-1/subscription");
        using var follow = await a.Client.PutAsync("/v1/items/NTADM-1/subscription", ApiRequests.Json(new { following = true }));
        var asMember = await RealStackSteps.GetAsync(b.Client, "/v1/items/NTADM-1/subscription");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((asAdmin["following"]?.GetValue<bool>(), asAdmin["canFollow"]?.GetValue<bool>()), Is.EqualTo((false, false)));
            await ApiRequests.AssertErrorEnvelopeAsync(follow, HttpStatusCode.Conflict, "subscription.not_member");
            Assert.That(asMember["canFollow"]?.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task Inbox_AfterBeingRemovedFromTheProject_HidesItsNotificationsAndNoLongerCountsThem()
    {
        // Arrange
        var (a, b) = await StartAsync("NTRMV");
        await RealStackSteps.CreateItemAsync(a.Client, "NTRMV", new { type = "task", title = "之後被移出", assigneeId = b.PersonId });
        await RunAsync();
        var before = (await InboxAsync(b, "NTRMV", unreadOnly: true)).Length;
        var totalBefore = await UnreadAsync(b);

        // Act
        using var removal = await a.Client.DeleteAsync($"/v1/projects/NTRMV/members/{b.PersonId}");
        await ApiRequests.AssertSuccessEnvelopeAsync(removal, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.EqualTo(1));
            Assert.That(await UnreadAsync(b), Is.EqualTo(totalBefore - 1));
            Assert.That(await InboxAsync(b, "NTRMV"), Is.Empty);
        }
    }
}
