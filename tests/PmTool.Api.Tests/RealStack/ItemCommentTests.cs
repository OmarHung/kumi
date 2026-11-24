using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 誰能讀活動、誰能留言照專案裡的角色；看不到的項目和不存在的一樣。
/// 存不進資料庫的留言內容要在工作引擎擋下來，回 422 而不是 500。
/// </summary>
[TestFixture]
public sealed class ItemCommentTests : RealStackTestBase
{
    // 合約寫的上限，故意寫死：常數被改了，這裡要跟著失敗，提醒網頁與文件一起改。
    private const int MaxCommentLength = 10_000;

    [Test]
    public async Task Viewer_ReadsTheActivityButCommentsOnlyAfterBecomingCommenter()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "TALK", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "TALK", new { type = "task", title = "寫首頁" });
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, "Commenter Example", "commenter@example.com");
        var personId = person["personId"]!.GetValue<Guid>();
        await RealStackSteps.SetMemberAsync(owner.Client, "TALK", personId, "viewer");
        using var member = await SignInAsAsync("commenter@example.com", RealStackSteps.MemberPassword);

        // Act
        var asViewer = await RealStackSteps.ActivityAsync(member.Client, "TALK-1");
        using var viewerComment = await RealStackSteps.PostCommentAsync(member.Client, "TALK-1", "檢視者想留言");
        await RealStackSteps.SetMemberAsync(owner.Client, "TALK", personId, "commenter");
        var comment = await RealStackSteps.CommentAsync(member.Client, "TALK-1", "留言者的意見");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "TALK-1");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(viewerComment, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        var entries = activity["items"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KindsOf(asViewer["items"]), Is.EqualTo(new[] { "created" }));
            Assert.That(comment["actor"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(member.PersonId));
            Assert.That(comment["actor"]?["displayName"]?.GetValue<string>(), Is.EqualTo("Commenter Example"));
            Assert.That(RealStackSteps.KindsOf(entries), Is.EqualTo(new[] { "created", "commented" }), "被擋下來的留言沒有留下紀錄");
            Assert.That(entries[0]?["actor"]?["displayName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That(entries[1]?["actor"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(member.PersonId));
            Assert.That(entries[1]?["actor"]?["displayName"]?.GetValue<string>(), Is.EqualTo("Commenter Example"));
            Assert.That(entries[1]?["comment"]?.GetValue<string>(), Is.EqualTo("留言者的意見"));
        }
    }

    [Test]
    public async Task NonMember_CannotReadTheActivityOrComment()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "PRIVATE", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "PRIVATE", new { type = "task", title = "寫首頁" });
        await RealStackSteps.CreateMemberAsync(owner.Client, "Outsider Example", "outsider@example.com");
        using var outsider = await SignInAsAsync("outsider@example.com", RealStackSteps.MemberPassword);

        // Act
        using var activity = await outsider.Client.GetAsync("/v1/items/PRIVATE-1/activity");
        using var comment = await RealStackSteps.PostCommentAsync(outsider.Client, "PRIVATE-1", "外人的留言");
        var afterwards = await RealStackSteps.ActivityAsync(owner.Client, "PRIVATE-1");

        // Assert：看不到的項目和不存在的一樣，從回應分辨不出項目在不在。
        await ApiRequests.AssertFailureEnvelopeAsync(activity, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(comment, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        Assert.That(RealStackSteps.KindsOf(afterwards["items"]), Is.EqualTo(new[] { "created" }));
    }

    [Test]
    public async Task Comment_ThatCannotBeStored_IsRejectedWithTheCodeForItsProblem()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "BODY", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "BODY", new { type = "task", title = "寫首頁" });

        // Act：NUL 送到 PostgreSQL 會是 22021，沒擋就是 500。
        using var blank = await RealStackSteps.PostCommentAsync(owner.Client, "BODY-1", " \r\n\t ");
        using var tooLong = await RealStackSteps.PostCommentAsync(owner.Client, "BODY-1", new string('長', MaxCommentLength + 1));
        using var withNul = await RealStackSteps.PostCommentAsync(owner.Client, "BODY-1", $"前{(char)0x0000}後");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "BODY-1");

        // Assert
        var blankDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            blank, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.CommentBodyRequired);
        var tooLongDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            tooLong, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.CommentBodyTooLong);
        var nulDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            withNul, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.CommentBodyInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(blankDetails), Is.EqualTo(new[] { "comment.body_required|body" }));
            Assert.That(ApiRequests.Describe(tooLongDetails), Is.EqualTo(new[] { "comment.body_too_long|body" }));
            Assert.That(ApiRequests.Describe(nulDetails), Is.EqualTo(new[] { "comment.body_invalid|body" }));
            Assert.That(RealStackSteps.KindsOf(activity["items"]), Is.EqualTo(new[] { "created" }), "被擋下來的都沒有留下紀錄");
        }
    }

    [Test]
    public async Task Comment_AtTheLimitAfterTrimming_IsAccepted()
    {
        // Arrange：頭尾的空白與換行不佔字數。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "EDGE", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "EDGE", new { type = "task", title = "寫首頁" });
        var body = new string('長', MaxCommentLength);

        // Act
        var comment = await RealStackSteps.CommentAsync(owner.Client, "EDGE-1", $"\n  {body}  \n");

        // Assert
        Assert.That(comment["comment"]?.GetValue<string>(), Is.EqualTo(body));
    }
}
