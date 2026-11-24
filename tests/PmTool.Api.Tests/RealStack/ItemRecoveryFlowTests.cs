using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 復原刪除與已刪除的清單的主線（真的資料庫）：刪除之後在「已刪除」清單裡、復原後回到清單與我的工作，活動多一筆 restored。每個類別的登入次數有上限，所以批次操作另外放。
/// </summary>
[TestFixture]
public sealed class ItemRecoveryFlowTests : RealStackTestBase
{
    [Test]
    public async Task RestoreItem_AfterDelete_IsListedAsDeletedThenBackInEveryListWithARestoredActivity()
    {
        // Arrange：任務指派給擁有者，刪除。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "REC", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "REC", new { type = "task", title = "寫週報", assigneeId = owner.PersonId, description = "每週五" });
        using var deleted = await owner.Client.DeleteAsync("/v1/items/REC-1");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        var listedDeleted = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/REC/items/deleted");
        using var restore = await owner.Client.PostAsync("/v1/items/REC-1/restore", null);
        var restored = await ApiRequests.AssertSuccessEnvelopeAsync(restore, HttpStatusCode.OK);
        using var again = await owner.Client.PostAsync("/v1/items/REC-1/restore", null);
        var resent = await ApiRequests.AssertSuccessEnvelopeAsync(again, HttpStatusCode.OK);
        var afterRestore = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/REC/items/deleted");
        var all = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/REC/items");
        var mine = await RealStackSteps.GetAsync(owner.Client, "/v1/my-work");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "REC-1");

        // Assert
        var row = listedDeleted["items"]?[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KeysOf(listedDeleted["items"]), Is.EqualTo(new[] { "REC-1" }));
            Assert.That(row?["deletedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
            Assert.That((row?["state"]?.GetValue<string>(), row?["stateCategory"]?.GetValue<string>()), Is.EqualTo(("todo", "not_started")));
            Assert.That(RealStackSteps.IsWrittenAsNull(listedDeleted, "nextCursor"), Is.True);
            Assert.That(restored?["description"]?.GetValue<string>(), Is.EqualTo("每週五"));
            Assert.That(resent?["updatedAt"]?.GetValue<DateTimeOffset>(), Is.EqualTo(restored?["updatedAt"]?.GetValue<DateTimeOffset>()));
            Assert.That(afterRestore["items"]?.AsArray(), Is.Empty);
            Assert.That(RealStackSteps.KeysOf(all["items"]), Is.EqualTo(new[] { "REC-1" }));
            Assert.That(RealStackSteps.KeysOf(mine["items"]), Does.Contain("REC-1"));
            Assert.That(RealStackSteps.KindsOf(activity["items"]), Is.EqualTo(new[] { "created", "deleted", "restored" }), "復原之後刪除與復原都列在時間軸上");
        }
    }

    [Test]
    public async Task RestoreItem_ParentAlsoDeleted_Returns409ThenSucceedsAfterTheParentIsRestored()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "PAR", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "PAR", new { type = "requirement", title = "首頁改版" });
        await RealStackSteps.CreateItemAsync(owner.Client, "PAR", new { type = "task", title = "版面", parentKey = "PAR-1" });
        (await owner.Client.DeleteAsync("/v1/items/PAR-2")).Dispose();
        (await owner.Client.DeleteAsync("/v1/items/PAR-1")).Dispose();

        // Act
        using var blocked = await owner.Client.PostAsync("/v1/items/PAR-2/restore", null);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(blocked, HttpStatusCode.Conflict, WorkErrorCodes.ItemParentDeleted);
        using var parent = await owner.Client.PostAsync("/v1/items/PAR-1/restore", null);
        using var child = await owner.Client.PostAsync("/v1/items/PAR-2/restore", null);
        var childData = await ApiRequests.AssertSuccessEnvelopeAsync(child, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemParentDeleted}|parent" }));
            Assert.That(details[0]?["message"]?.GetValue<string>(), Does.Contain("PAR-1"));
            Assert.That(parent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(childData?["parentKey"]?.GetValue<string>(), Is.EqualTo("PAR-1"));
        }
    }

    [Test]
    public async Task RestoreItem_ByACommenter_Returns403AndKeepsTheItemDeleted()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "NOREC", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "NOREC", new { type = "task", title = "留言者復原不了" });
        (await owner.Client.DeleteAsync("/v1/items/NOREC-1")).Dispose();
        using var commenter = await AddMemberAsync(owner, "NOREC", "commenter", "Commenter Example", RealStackSteps.UniqueEmail("commenter"));

        // Act
        using var response = await commenter.Client.PostAsync("/v1/items/NOREC-1/restore", null);
        var stillDeleted = await RealStackSteps.GetAsync(commenter.Client, "/v1/projects/NOREC/items/deleted");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(RealStackSteps.KeysOf(stillDeleted["items"]), Is.EqualTo(new[] { "NOREC-1" }), "能讀的人看得到已刪除的清單");
    }

    [Test]
    public async Task ListDeleted_PagedByLimitAndCursor_WalksMostRecentFirst()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "PAGE", "simple_tasks");
        for (var i = 1; i <= 3; i++)
        {
            await RealStackSteps.CreateItemAsync(owner.Client, "PAGE", new { type = "task", title = $"任務 {i}" });
            (await owner.Client.DeleteAsync($"/v1/items/PAGE-{i}")).Dispose();
        }

        // Act
        var first = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/PAGE/items/deleted?limit=2");
        var cursor = Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>());
        var second = await RealStackSteps.GetAsync(owner.Client, $"/v1/projects/PAGE/items/deleted?limit=2&cursor={cursor}");
        using var bad = await owner.Client.GetAsync("/v1/projects/PAGE/items/deleted?cursor=nope!");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KeysOf(first["items"]), Is.EqualTo(new[] { "PAGE-3", "PAGE-2" }));
            Assert.That(RealStackSteps.KeysOf(second["items"]), Is.EqualTo(new[] { "PAGE-1" }));
            Assert.That(RealStackSteps.IsWrittenAsNull(second, "nextCursor"), Is.True);
        }

        await ApiRequests.AssertFailureEnvelopeAsync(bad, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
    }
}
