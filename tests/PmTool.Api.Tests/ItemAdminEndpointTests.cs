using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>刪除項目與改類型：API 只轉換格式與狀態碼，規則由工作引擎判斷。</summary>
[TestFixture]
public sealed class ItemAdminEndpointTests
{
    // 改類型回的和 GET /v1/items/{itemKey} 同一個形狀，網頁拿回來可以直接換掉讀到的項目。
    private static readonly string[] ItemShape =
    [
        "key", "projectKey", "number", "type", "title", "state", "stateCategory", "assignee", "priority",
        "startDate", "dueDate", "estimate", "parentKey", "cycleId", "milestoneId", "fields", "createdAt", "updatedAt", "description", "descriptionRevision",
    ];

    [Test]
    public async Task DeleteItem_Accepted_PassesTheKeyAndReturnsTheKeyAndTheTime()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync("/v1/items/WEB-7");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = new JsonObject { ["key"] = "WEB-7", ["deletedAt"] = "2026-11-13T04:05:06Z" };
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.Items.DeleteCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB-7") }));
    }

    [Test]
    public async Task DeleteItem_StillHasChildren_Returns409WithOneDetailPerChild()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnDelete = (_, _) => new Failure(FailureKind.Conflict, WorkErrorCodes.ItemHasChildren, "底下還有 2 個項目。")
        {
            Details =
            [
                new FailureDetail(WorkErrorCodes.ItemHasChildren, "children/WEB-8", "「首頁的版面」（WEB-8）還掛在這個項目底下。"),
                new FailureDetail(WorkErrorCodes.ItemHasChildren, "children/WEB-9", "「首頁的文案」（WEB-9）還掛在這個項目底下。"),
            ],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync("/v1/items/WEB-7");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.Conflict, WorkErrorCodes.ItemHasChildren);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[]
        {
            $"{WorkErrorCodes.ItemHasChildren}|children/WEB-8",
            $"{WorkErrorCodes.ItemHasChildren}|children/WEB-9",
        }));
        Assert.That(details[0]?["message"]?.GetValue<string>(), Does.Contain("WEB-8"));
    }

    [TestCase(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    public async Task DeleteItem_ModuleFails_ReturnsTheMatchingStatus(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnDelete = (_, _) => new Failure(kind, code, "模組說的原因。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync("/v1/items/WEB-7");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task ChangeType_Accepted_PassesTheTypeAndReturnsTheItemWithItsDescription()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnChangeType = (_, key, type) => TestData.Item(key) with { Type = type!, Description = "說明" };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-3/type", ApiRequests.Json(new { type = "requirement" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["key"]?.GetValue<string>(), Is.EqualTo("WEB-3"));
            Assert.That(data?["type"]?.GetValue<string>(), Is.EqualTo("requirement"));
            Assert.That(data?["description"]?.GetValue<string>(), Is.EqualTo("說明"));
            Assert.That(data?.AsObject().Select(p => p.Key), Is.EquivalentTo(ItemShape), "和 GET /v1/items/{itemKey} 同一個形狀");
            Assert.That(factory.Modules.Items.ChangeTypeCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB-3", (string?)"requirement") }));
        }
    }

    [TestCase("{}", TestName = "ChangeType_TypeMissing_IsLeftToTheModuleWhichSaysUnknownType")]
    [TestCase("""{"type":null}""", TestName = "ChangeType_TypeNull_IsLeftToTheModuleWhichSaysUnknownType")]
    public async Task ChangeType_WithoutAType(string body)
    {
        // Arrange：沒給類型和給了不存在的類型一樣，由工作引擎回 422 item.unknown_type。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnChangeType = (_, _, type) => type is null
            ? new Failure(FailureKind.Invalid, WorkErrorCodes.ItemUnknownType, "這個專案沒有「」這種項目類型。")
            {
                Details = [new FailureDetail(WorkErrorCodes.ItemUnknownType, "type", "這個專案沒有「」這種項目類型。")],
            }
            : TestData.Item();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-3/type", ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemUnknownType);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemUnknownType}|type" }));
        Assert.That(factory.Modules.Items.ChangeTypeCalls.Single().Type, Is.Null);
    }

    [TestCase("""{"type":7}""", "type", TestName = "ChangeType_TypeNotAString_Returns400")]
    [TestCase("""{"type":"task","state":"done"}""", "state", TestName = "ChangeType_StateGiven_Returns400")]
    [TestCase("""{"type":"task","type":"bug"}""", "type", TestName = "ChangeType_DuplicateProperty_Returns400")]
    public async Task ChangeType_BadBody(string body, string expectedPath)
    {
        // Arrange：狀態由伺服器決定，送了也不收（和其他端點一樣，不認得的屬性一律擋下）。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-3/type", ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{ApiErrorCodes.BadRequest}|{expectedPath}" }));
        Assert.That(factory.Modules.Items.ChangeTypeCalls, Is.Empty);
    }

    [Test]
    public async Task ChangeType_NotJson_Returns415WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-3/type", ApiRequests.Document("type: task", "application/yaml"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
        Assert.That(factory.Modules.Items.ChangeTypeCalls, Is.Empty);
    }

    [Test]
    public async Task ChangeType_ChildrenNotAllowed_Returns422WithOneDetailPerChild()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnChangeType = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.ItemParentNotAllowed, "「任務」只能掛在需求底下。")
            {
                Details =
                [
                    new FailureDetail(WorkErrorCodes.ItemParentNotAllowed, "parent", "「任務」只能掛在需求底下。"),
                    new FailureDetail(WorkErrorCodes.ItemChildrenNotAllowed, "children/WEB-8", "「登入按鈕」（WEB-8）不能掛在任務底下。"),
                ],
            };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-3/type", ApiRequests.Json(new { type = "task" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemParentNotAllowed);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[]
        {
            $"{WorkErrorCodes.ItemParentNotAllowed}|parent",
            $"{WorkErrorCodes.ItemChildrenNotAllowed}|children/WEB-8",
        }));
    }
}
