using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>專案的「執行者」只看得到自己負責或建立的項目：清單、單一項目、搜尋、「我的工作」、留言都照這個範圍。</summary>
[TestFixture]
public sealed class OwnItemsFlowTests : RealStackTestBase
{
    [Test]
    public async Task Contributor_SeesOnlyTheirOwnItemsOnEveryReadingRoute()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "OWNONLY", "simple_tasks");
        using var contributor = await AddMemberAsync(owner, "OWNONLY", "contributor", "Contributor Example", "contributor-own@example.com");
        var assigned = await RealStackSteps.CreateItemAsync(
            owner.Client, "OWNONLY", new { type = "task", title = "Shared Report", assigneeId = contributor.PersonId });
        var others = await RealStackSteps.CreateItemAsync(owner.Client, "OWNONLY", new { type = "task", title = "Shared Secret Report" });
        var created = await RealStackSteps.CreateItemAsync(contributor.Client, "OWNONLY", new { type = "task", title = "Shared Draft" });

        // Act
        var list = await RealStackSteps.GetAsync(contributor.Client, "/v1/projects/OWNONLY/items");
        var crossProject = await RealStackSteps.GetAsync(contributor.Client, "/v1/items?limit=50");
        var search = await RealStackSteps.GetAsync(contributor.Client, "/v1/search/items?q=Shared");
        var detail = await RealStackSteps.GetAsync(contributor.Client, "/v1/projects/OWNONLY");
        using var getOthers = await contributor.Client.GetAsync($"/v1/items/{others["key"]!.GetValue<string>()}");
        using var commentOthers = await RealStackSteps.PostCommentAsync(contributor.Client, others["key"]!.GetValue<string>(), "偷看");
        using var activityOthers = await contributor.Client.GetAsync($"/v1/items/{others["key"]!.GetValue<string>()}/activity");
        using var attachmentsOthers = await contributor.Client.GetAsync($"/v1/items/{others["key"]!.GetValue<string>()}/attachments");
        var ownComment = await RealStackSteps.CommentAsync(contributor.Client, assigned["key"]!.GetValue<string>(), "我的留言");

        // Assert
        var ownKeys = new[] { assigned["key"]!.GetValue<string>(), created["key"]!.GetValue<string>() };
        await ApiRequests.AssertFailureEnvelopeAsync(getOthers, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(commentOthers, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(activityOthers, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(attachmentsOthers, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KeysOf(list["items"]), Is.EquivalentTo(ownKeys));
            Assert.That(RealStackSteps.KeysOf(search["items"]), Is.EquivalentTo(ownKeys));
            Assert.That(RealStackSteps.KeysOf(crossProject["items"]), Is.EquivalentTo(ownKeys));
            Assert.That(detail["myRole"]?.GetValue<string>() ?? detail["project"]?["myRole"]?.GetValue<string>(), Is.EqualTo("contributor"));
            Assert.That(detail["myPermissions"]!.AsArray().Select(p => p?.GetValue<string>()), Does.Contain(WorkPermissions.ItemReadOwn));
            Assert.That(ownComment["kind"]?.GetValue<string>(), Is.EqualTo("commented"));
        }
    }
}
