using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 批次修改、刪除與復原的主線（真的資料庫）：成功與失敗分開，整串的上層與下層一起刪除、一起復原時順序由伺服器排好。
/// </summary>
[TestFixture]
public sealed class BulkItemFlowTests : RealStackTestBase
{
    [Test]
    public async Task BulkUpdate_State_MovesTheAllowedOnesAndReportsTheBlockedOnesWithTheirErrors()
    {
        // Arrange：需求 BLK-1 在「需求確認」，BLK-2 已經走到「設計」；都轉去「報價」，只有第一個符合流程。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "BLK", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "BLK", new { type = "requirement", title = "確認中" });
        await RealStackSteps.CreateItemAsync(owner.Client, "BLK", new { type = "requirement", title = "設計中" });
        await RealStackSteps.TransitionThroughAsync(owner.Client, "BLK-2", "quoting", "designing");

        // Act
        using var response = await owner.Client.PostAsync(
            "/v1/projects/BLK/items/bulk-update", ApiRequests.Json(new { keys = new[] { "BLK-2", "BLK-1", "BLK-404" }, state = "quoting" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var failed = data?["failed"]?.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KeysOf(data?["updated"]), Is.EqualTo(new[] { "BLK-1" }));
            Assert.That(failed?.Select(f => (f?["key"]?.GetValue<string>(), f?["error"]?["code"]?.GetValue<string>())),
                Is.EqualTo(new[] { ("BLK-2", WorkErrorCodes.TransitionNotAllowed), ("BLK-404", WorkErrorCodes.ItemNotFound) }));
            Assert.That(failed?[0]?["error"]?["message"]?.GetValue<string>(), Is.Not.Empty);
        }
    }

    [Test]
    public async Task BulkUpdate_Properties_ChangesEveryItemAndNullClears()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "PROP", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "PROP", new { type = "task", title = "一", dueDate = "2026-12-01" });
        await RealStackSteps.CreateItemAsync(owner.Client, "PROP", new { type = "task", title = "二" });

        // Act
        using var set = await owner.Client.PostAsync(
            "/v1/projects/PROP/items/bulk-update",
            ApiRequests.Json(new { keys = new[] { "PROP-1", "PROP-2" }, assigneeId = owner.PersonId, priority = "high" }));
        var changed = await ApiRequests.AssertSuccessEnvelopeAsync(set, HttpStatusCode.OK);
        using var clear = await owner.Client.PostAsync(
            "/v1/projects/PROP/items/bulk-update", ApiRequests.Document("""{"keys":["PROP-1","PROP-2"],"dueDate":null,"assigneeId":null}"""));
        var cleared = await ApiRequests.AssertSuccessEnvelopeAsync(clear, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed?["updated"]?.AsArray().Select(i => i?["priority"]?.GetValue<string>()), Is.All.EqualTo("high"));
            Assert.That(changed?["updated"]?.AsArray().Select(i => i?["assignee"]?["personId"]?.GetValue<Guid>()), Is.All.EqualTo(owner.PersonId));
            Assert.That(changed?["updated"]?[0]?.AsObject().ContainsKey("description"), Is.False);
            Assert.That(cleared?["updated"]?.AsArray().Select(i => (RealStackSteps.IsWrittenAsNull(i, "dueDate"), RealStackSteps.IsWrittenAsNull(i, "assignee"))),
                Is.All.EqualTo((true, true)));
            Assert.That(cleared?["updated"]?[0]?["priority"]?.GetValue<string>(), Is.EqualTo("high"), "沒寫的屬性不動");
        }
    }

    [Test]
    public async Task BulkUpdate_InvalidRequests_Return422BulkInvalidWithThePath()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "BAD", "simple_tasks");
        var cases = new (string Body, string Path)[]
        {
            ("""{"keys":[]}""", "keys"),
            ("""{"keys":["BAD-1"]}""", string.Empty),
            ("""{"keys":["BAD-1"],"state":"doing","priority":"high"}""", "state"),
        };

        foreach (var (body, path) in cases)
        {
            // Act
            using var response = await owner.Client.PostAsync("/v1/projects/BAD/items/bulk-update", ApiRequests.Document(body));
            var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.BulkInvalid);

            // Assert
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.BulkInvalid}|{path}" }), body);
        }
    }

    [Test]
    public async Task BulkDeleteAndRestore_ParentAndChildrenTogether_WorkInAnyOrderAndRestoreBringsTheWholeChainBack()
    {
        // Arrange：需求 CHAIN-1 與它底下的任務 CHAIN-2、CHAIN-3，一起選。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "CHAIN", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "CHAIN", new { type = "requirement", title = "需求" });
        await RealStackSteps.CreateItemAsync(owner.Client, "CHAIN", new { type = "task", title = "任務一", parentKey = "CHAIN-1" });
        await RealStackSteps.CreateItemAsync(owner.Client, "CHAIN", new { type = "task", title = "任務二", parentKey = "CHAIN-1" });
        var keys = new[] { "CHAIN-1", "CHAIN-2", "CHAIN-3" };

        // Act
        using var deleteResponse = await owner.Client.PostAsync("/v1/projects/CHAIN/items/bulk-delete", ApiRequests.Json(new { keys }));
        var deleted = await ApiRequests.AssertSuccessEnvelopeAsync(deleteResponse, HttpStatusCode.OK);
        var whileDeleted = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/CHAIN/items");
        var deletedList = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/CHAIN/items/deleted");
        using var deleteAgain = await owner.Client.PostAsync("/v1/projects/CHAIN/items/bulk-delete", ApiRequests.Json(new { keys }));
        var again = await ApiRequests.AssertSuccessEnvelopeAsync(deleteAgain, HttpStatusCode.OK);
        using var restoreResponse = await owner.Client.PostAsync(
            "/v1/projects/CHAIN/items/bulk-restore", ApiRequests.Json(new { keys = new[] { "CHAIN-3", "CHAIN-2", "CHAIN-1" } }));
        var restored = await ApiRequests.AssertSuccessEnvelopeAsync(restoreResponse, HttpStatusCode.OK);
        var children = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/CHAIN/items?parent=CHAIN-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted?["deleted"]?.AsArray().Select(d => d?["key"]?.GetValue<string>()), Is.EqualTo(keys));
            Assert.That(deleted?["failed"]?.AsArray(), Is.Empty);
            Assert.That(whileDeleted["items"]?.AsArray(), Is.Empty);
            Assert.That(RealStackSteps.KeysOf(deletedList["items"]), Is.EquivalentTo(keys));
            Assert.That(again?["deleted"]?.AsArray().Select(d => d?["deletedAt"]?.GetValue<DateTimeOffset>()),
                Is.EqualTo(deleted?["deleted"]?.AsArray().Select(d => d?["deletedAt"]?.GetValue<DateTimeOffset>())), "再刪一次算成功，時刻不變");
            Assert.That(RealStackSteps.KeysOf(restored?["restored"]), Is.EqualTo(new[] { "CHAIN-3", "CHAIN-2", "CHAIN-1" }));
            Assert.That(restored?["failed"]?.AsArray(), Is.Empty);
            Assert.That(RealStackSteps.KeysOf(children["items"]), Is.EqualTo(new[] { "CHAIN-2", "CHAIN-3" }));
        }
    }

    [Test]
    public async Task BulkDelete_ParentWithAnUnselectedChild_ReportsHasChildrenForThatParentOnly()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "PART", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "PART", new { type = "requirement", title = "需求" });
        await RealStackSteps.CreateItemAsync(owner.Client, "PART", new { type = "task", title = "沒選到", parentKey = "PART-1" });
        await RealStackSteps.CreateItemAsync(owner.Client, "PART", new { type = "task", title = "選到了" });

        // Act
        using var response = await owner.Client.PostAsync(
            "/v1/projects/PART/items/bulk-delete", ApiRequests.Json(new { keys = new[] { "PART-1", "PART-3" } }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var failure = data?["failed"]?[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["deleted"]?.AsArray().Select(d => d?["key"]?.GetValue<string>()), Is.EqualTo(new[] { "PART-3" }));
            Assert.That(failure?["key"]?.GetValue<string>(), Is.EqualTo("PART-1"));
            Assert.That(failure?["error"]?["code"]?.GetValue<string>(), Is.EqualTo(WorkErrorCodes.ItemHasChildren));
            Assert.That(ApiRequests.Describe((JsonArray)failure!["error"]!["details"]!), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemHasChildren}|children/PART-2" }));
        }
    }
}
