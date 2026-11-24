using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 刪除項目＝封存：刪除之後清單、下層清單、搜尋、我的工作、讀取都看不到它，也不能被設成上層；
/// 還有下層的上層刪不掉；刪除的項目在變更集裡不算，刪光一個類型的項目之後就能刪除那個類型。
/// </summary>
[TestFixture]
public sealed class ItemDeletionFlowTests : RealStackTestBase
{
    [Test]
    public async Task Delete_Task_IsGoneFromEveryListAndCannotBeUsedAsAParent()
    {
        // Arrange：DEL-1 是需求，DEL-2、DEL-3 是掛在它底下的任務，DEL-2 由擁有者負責。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "DEL", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "DEL", new { type = "requirement", title = "首頁改版需求" });
        await RealStackSteps.CreateItemAsync(owner.Client, "DEL", new { type = "task", title = "首頁的版面", parentKey = "DEL-1", assigneeId = owner.PersonId });
        await RealStackSteps.CreateItemAsync(owner.Client, "DEL", new { type = "task", title = "首頁的文案", parentKey = "DEL-1" });
        var mineBefore = await RealStackSteps.GetAsync(owner.Client, "/v1/my-work");

        // Act
        using var deleted = await owner.Client.DeleteAsync("/v1/items/DEL-2");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(deleted, HttpStatusCode.OK);
        using var read = await owner.Client.GetAsync("/v1/items/DEL-2");
        using var activity = await owner.Client.GetAsync("/v1/items/DEL-2/activity");
        using var again = await owner.Client.DeleteAsync("/v1/items/DEL-2");
        var all = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/DEL/items");
        var children = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/DEL/items?parent=DEL-1");
        var searched = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/DEL/items?q=%E7%89%88%E9%9D%A2");
        var mine = await RealStackSteps.GetAsync(owner.Client, "/v1/my-work");
        using var reparent = await owner.Client.PatchAsync("/v1/items/DEL-3", ApiRequests.Json(new { parentKey = "DEL-2" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(read, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(activity, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(again, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        var reparentDetails = await ApiRequests.AssertFailureEnvelopeAsync(reparent, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemUnknownParent);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["key"]?.GetValue<string>(), Is.EqualTo("DEL-2"));
            Assert.That(data?["deletedAt"]?.GetValue<DateTimeOffset>(), Is.Not.Null);
            Assert.That(RealStackSteps.KeysOf(all["items"]), Is.EqualTo(new[] { "DEL-1", "DEL-3" }));
            Assert.That(RealStackSteps.KeysOf(children["items"]), Is.EqualTo(new[] { "DEL-3" }));
            Assert.That(searched["items"]?.AsArray(), Is.Empty);
            Assert.That(RealStackSteps.KeysOf(mineBefore["items"]), Does.Contain("DEL-2"));
            Assert.That(RealStackSteps.KeysOf(mine["items"]), Does.Not.Contain("DEL-2"));
            Assert.That(ApiRequests.Describe(reparentDetails), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemUnknownParent}|parent" }));
        }
    }

    [Test]
    public async Task Delete_ParentWithAChild_Returns409NamingTheChildAndKeepsTheParent()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "KEEP", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "KEEP", new { type = "requirement", title = "會員中心需求" });
        await RealStackSteps.CreateItemAsync(owner.Client, "KEEP", new { type = "task", title = "會員中心的登入", parentKey = "KEEP-1" });

        // Act
        using var blocked = await owner.Client.DeleteAsync("/v1/items/KEEP-1");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(blocked, HttpStatusCode.Conflict, WorkErrorCodes.ItemHasChildren);
        var parent = await RealStackSteps.GetAsync(owner.Client, "/v1/items/KEEP-1");
        using var child = await owner.Client.DeleteAsync("/v1/items/KEEP-2");
        using var afterChild = await owner.Client.DeleteAsync("/v1/items/KEEP-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemHasChildren}|children/KEEP-2" }));
            Assert.That(details[0]?["message"]?.GetValue<string>(), Does.Contain("會員中心的登入"));
            Assert.That(parent["key"]?.GetValue<string>(), Is.EqualTo("KEEP-1"));
            Assert.That(child.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(afterChild.StatusCode, Is.EqualTo(HttpStatusCode.OK), "下層刪掉之後上層就刪得掉");
        }
    }

    [Test]
    public async Task RemoveType_AfterItsItemsAreDeleted_CountsOnlyLiveItemsAndApplies()
    {
        // Arrange：三個需求變更單刪掉兩個；送出刪除這個類型的變更集，只數剩下的一個。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "GONE", "agency_project");
        foreach (var title in new[] { "加上社群登入", "改結帳流程", "多一種付款方式" })
        {
            await RealStackSteps.CreateItemAsync(owner.Client, "GONE", new { type = "change_request", title });
        }

        foreach (var key in new[] { "GONE-1", "GONE-3" })
        {
            using var deleted = await owner.Client.DeleteAsync($"/v1/items/{key}");
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var blueprint = ChangeSetSteps.WithoutItemType(await ChangeSetSteps.BlueprintOfAsync(owner.Client, "GONE"), "change_request");
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "GONE", ChangeSetSteps.Json(1, "拿掉需求變更單", blueprint));

        // Act
        using var lastOne = await owner.Client.DeleteAsync("/v1/items/GONE-2");
        var recounted = await ChangeSetSteps.GetAsync(owner.Client, "GONE", 1);
        var applied = await ChangeSetSteps.AppliedAsync(owner.Client, "GONE", 1);
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/GONE");

        // Assert
        var stillOne = new JsonArray(new JsonObject { ["kind"] = "item_type_removal", ["affected"] = 1, ["blocksApply"] = true, ["itemType"] = "change_request" });
        var none = new JsonArray(new JsonObject { ["kind"] = "item_type_removal", ["affected"] = 0, ["blocksApply"] = false, ["itemType"] = "change_request" });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(proposed["impacts"], stillOne), Is.True, proposed["impacts"]?.ToJsonString());
            Assert.That(lastOne.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(JsonNode.DeepEquals(recounted["impacts"], none), Is.True, recounted["impacts"]?.ToJsonString());
            Assert.That(recounted["canApply"]?.GetValue<bool>(), Is.True);
            Assert.That(ChangeSetSteps.StatusOf(applied), Is.EqualTo("applied"));
            Assert.That(RealStackSteps.KeysOf(project["blueprint"]?["itemTypes"]), Is.EqualTo(new[] { "requirement", "task", "subtask" }));
        }
    }

    [Test]
    public async Task Delete_ByACommenter_Returns403AndKeepsTheItem()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "NODEL", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "NODEL", new { type = "task", title = "留言者刪不掉" });
        using var commenter = await AddMemberAsync(owner, "NODEL", "commenter", "Commenter Example", "commenter@example.com");

        // Act
        using var response = await commenter.Client.DeleteAsync("/v1/items/NODEL-1");
        var item = await RealStackSteps.GetAsync(owner.Client, "/v1/items/NODEL-1");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(item["key"]?.GetValue<string>(), Is.EqualTo("NODEL-1"));
    }
}
