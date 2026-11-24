using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 「接案專案」的改類型：需求變更單改成任務時，任務流程不限制轉換，管理員換到同類別的狀態；
/// 任務改成需求變更單時，交付流程限制轉換，一律從第一個狀態開始。任務沒有的欄位值封存（之後讀不到），
/// 活動記下 type_changed；上下層不符合新類型時擋下。
/// </summary>
[TestFixture]
public sealed class ItemTypeChangeFlowTests : RealStackTestBase
{
    [Test]
    public async Task ChangeType_ChangeRequestInDevelopmentToTask_MovesTheStateArchivesTheFieldsAndRecordsIt()
    {
        // Arrange：需求變更單走到「開發」，填了變更原因、影響程度與報價金額。任務流程不限制轉換，擁有者能改狀態，所以搬到同類別的「進行中」。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "TYPE", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "TYPE", new
        {
            type = "change_request",
            title = "加上社群登入",
            description = "客戶在週會提出",
            fields = new { change_reason = "客戶要加社群登入", impact = "high", quote_amount = 1200 },
        });
        await RealStackSteps.TransitionThroughAsync(owner.Client, "TYPE-1", "quoting", "designing", "developing");

        // Act
        using var response = await owner.Client.PostAsync("/v1/items/TYPE-1/type", ApiRequests.Json(new { type = "task" }));
        var changed = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var readBack = await RealStackSteps.GetAsync(owner.Client, "/v1/items/TYPE-1");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "TYPE-1");
        var done = await RealStackSteps.TransitionThroughAsync(owner.Client, "TYPE-1", "done");

        // Assert
        var last = activity["items"]!.AsArray()[^1];
        var expectedChanges = new JsonArray(
            new JsonObject { ["field"] = "item_type", ["from"] = "change_request", ["to"] = "task" },
            new JsonObject { ["field"] = "change_reason", ["from"] = null, ["to"] = null },
            new JsonObject { ["field"] = "impact", ["from"] = "high", ["to"] = null },
            new JsonObject { ["field"] = "quote_amount", ["from"] = 1200, ["to"] = null });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(changed, readBack), Is.True, "回的和 GET 的一樣");
            Assert.That(readBack["type"]?.GetValue<string>(), Is.EqualTo("task"));
            Assert.That(readBack["state"]?.GetValue<string>(), Is.EqualTo("doing"));
            Assert.That(readBack["stateCategory"]?.GetValue<string>(), Is.EqualTo("in_progress"));
            Assert.That(readBack["fields"]?.AsObject(), Is.Empty, "任務沒有的欄位不再出現");
            Assert.That(readBack["description"]?.GetValue<string>(), Is.EqualTo("客戶在週會提出"));
            Assert.That(last?["kind"]?.GetValue<string>(), Is.EqualTo("type_changed"));
            Assert.That(JsonNode.DeepEquals(last?["changes"], expectedChanges), Is.True, last?["changes"]?.ToJsonString());
            Assert.That(last?["fromState"]?.GetValue<string>(), Is.EqualTo("developing"));
            Assert.That(last?["toState"]?.GetValue<string>(), Is.EqualTo("doing"));
            Assert.That(last?["actor"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
            Assert.That(done["stateCategory"]?.GetValue<string>(), Is.EqualTo("done"), "之後照任務流程走");
        }
    }

    [Test]
    public async Task ChangeType_DoneTaskToAChangeRequest_StartsAtTheFirstDeliveryStateInsteadOfClosed()
    {
        // Arrange：沒有上層的任務一路做完；交付流程進「結案」限專案經理、要先填客戶驗收日，改類型不能跳過。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "BACK", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "BACK", new { type = "task", title = "整理共用元件" });
        await RealStackSteps.TransitionThroughAsync(owner.Client, "BACK-1", "doing", "done");

        // Act
        using var response = await owner.Client.PostAsync("/v1/items/BACK-1/type", ApiRequests.Json(new { type = "change_request" }));
        var changed = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "BACK-1");

        // Assert
        var last = activity["items"]!.AsArray()[^1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed?["type"]?.GetValue<string>(), Is.EqualTo("change_request"));
            Assert.That(changed?["state"]?.GetValue<string>(), Is.EqualTo("confirming"));
            Assert.That(changed?["stateCategory"]?.GetValue<string>(), Is.EqualTo("not_started"));
            Assert.That(last?["fromState"]?.GetValue<string>(), Is.EqualTo("done"));
            Assert.That(last?["toState"]?.GetValue<string>(), Is.EqualTo("confirming"));
        }
    }

    [Test]
    public async Task ChangeType_ThatDoesNotFitTheHierarchyOrTheProject_Returns422AndChangesNothing()
    {
        // Arrange：TREE-1 是需求變更單，TREE-2 是掛在它底下的任務。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "TREE", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "TREE", new { type = "change_request", title = "改結帳流程" });
        await RealStackSteps.CreateItemAsync(owner.Client, "TREE", new { type = "task", title = "結帳頁", parentKey = "TREE-1" });

        // Act
        using var parentToTask = await owner.Client.PostAsync("/v1/items/TREE-1/type", ApiRequests.Json(new { type = "task" }));
        using var childToRequirement = await owner.Client.PostAsync("/v1/items/TREE-2/type", ApiRequests.Json(new { type = "requirement" }));
        using var unknown = await owner.Client.PostAsync("/v1/items/TREE-1/type", ApiRequests.Json(new { type = "bug" }));
        using var missing = await owner.Client.PostAsync("/v1/items/TREE-1/type", ApiRequests.Json(new { }));
        var parent = await RealStackSteps.GetAsync(owner.Client, "/v1/items/TREE-1");

        // Assert
        var children = await ApiRequests.AssertFailureEnvelopeAsync(parentToTask, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemChildrenNotAllowed);
        var hierarchy = await ApiRequests.AssertFailureEnvelopeAsync(childToRequirement, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemParentNotAllowed);
        var unknownType = await ApiRequests.AssertFailureEnvelopeAsync(unknown, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemUnknownType);
        var missingType = await ApiRequests.AssertFailureEnvelopeAsync(missing, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemUnknownType);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(children), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemChildrenNotAllowed}|children/TREE-2" }));
            Assert.That(ApiRequests.Describe(hierarchy), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemParentNotAllowed}|parent" }));
            Assert.That(ApiRequests.Describe(unknownType), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemUnknownType}|type" }));
            Assert.That(ApiRequests.Describe(missingType), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemUnknownType}|type" }));
            Assert.That(parent["type"]?.GetValue<string>(), Is.EqualTo("change_request"));
        }
    }
}
