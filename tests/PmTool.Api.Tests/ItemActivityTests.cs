using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>GET /v1/items/{itemKey}/activity：活動的形狀、人名的補齊、查詢參數。</summary>
[TestFixture]
public sealed class ItemActivityTests
{
    private const string Route = "/v1/items/WEB-7/activity";

    [Test]
    public async Task List_SignedIn_PassesTheItemAndTheDefaultQueryAndReturnsThePage()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnList = (_, _, _) => new ItemActivityPage(
            [TestData.Activity(ActivityKind.Created, TestUsers.Owner.PersonId)], "page-2");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var (actor, itemKey, query) = factory.Modules.Activity.ListCalls.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["items"]?.AsArray().Select(a => a?["id"]?.GetValue<Guid>()), Is.EqualTo(new[] { TestData.ActivityId(1) }));
            Assert.That(data?["nextCursor"]?.GetValue<string>(), Is.EqualTo("page-2"));
            Assert.That(actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(itemKey, Is.EqualTo("WEB-7"));
            Assert.That(query, Is.EqualTo(new ActivityQuery()));
        }
    }

    [Test]
    public async Task List_EachKind_HasTheContractShape()
    {
        // Arrange
        var owner = TestUsers.Owner.PersonId;
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnList = (_, _, _) => new ItemActivityPage(
        [
            TestData.Activity(ActivityKind.Created, owner, 1),
            TestData.Activity(ActivityKind.Updated, owner, 2) with
            {
                Changes =
                [
                    new FieldChange("title", Element("舊標題"), Element("新標題")),
                    new FieldChange("assignee", null, Element(TestUsers.Member.PersonId.ToString())),
                    new FieldChange("start_date", Element<string?>(null), Element("2026-12-01")),
                    new FieldChange("estimate", Element(2.5m), Element(3)),
                    new FieldChange("tags", Element(new[] { "a" }), Element(new[] { "a", "b" })),
                    new FieldChange("description", null, null),
                ],
            },
            TestData.Activity(ActivityKind.Transitioned, owner, 3) with { FromState = "todo", ToState = "doing" },
            TestData.Activity(ActivityKind.Commented, owner, 4) with { Comment = "第一行\n第二行" },
            TestData.Activity(ActivityKind.Migrated, owner, 5) with
            {
                FromState = "review",
                ToState = "internal_review",
                BlueprintVersion = 3,
                Changes = [new FieldChange("budget", Element("1200"), Element(1200)), new FieldChange("legacy", Element("舊值"), null)],
            },
            TestData.Activity(ActivityKind.TypeChanged, owner, 6) with
            {
                FromState = "developing",
                ToState = "doing",
                Changes =
                [
                    new FieldChange("item_type", Element("change_request"), Element("task")),
                    new FieldChange("quote_amount", Element(1200), null),
                    new FieldChange("change_reason", null, null),
                ],
            },
            TestData.Activity(ActivityKind.Deleted, owner, 7),
        ], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var changes = new JsonArray(
            Change("title", "舊標題", "新標題"),
            Change("assignee", null, TestUsers.Member.PersonId.ToString()),
            Change("start_date", null, "2026-12-01"),
            Change("estimate", 2.5, 3),
            Change("tags", new JsonArray("a"), new JsonArray("a", "b")),
            Change("description", null, null));
        var migratedChanges = new JsonArray(Change("budget", "1200", 1200), Change("legacy", "舊值", null));
        var typeChanges = new JsonArray(
            Change("item_type", "change_request", "task"), Change("quote_amount", 1200, null), Change("change_reason", null, null));
        var expected = new JsonObject
        {
            ["items"] = new JsonArray(
                Expected(1, "created", "2026-11-14T07:01:00Z"),
                Expected(2, "updated", "2026-11-14T07:02:00Z", changes: changes),
                Expected(3, "transitioned", "2026-11-14T07:03:00Z", fromState: "todo", toState: "doing"),
                Expected(4, "commented", "2026-11-14T07:04:00Z", comment: "第一行\n第二行"),
                Expected(5, "migrated", "2026-11-14T07:05:00Z", migratedChanges, "review", "internal_review", blueprintVersion: 3),
                Expected(6, "type_changed", "2026-11-14T07:06:00Z", typeChanges, "developing", "doing"),

                // 刪除的項目讀不到，這一種實際上不會出現在 API；對應寫好，資料表裡的紀錄之後讀得出來。
                Expected(7, "deleted", "2026-11-14T07:07:00Z")),
            ["nextCursor"] = null,
        };
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task List_PeopleOnThePage_AreLookedUpOnceAndThoseNotFoundGetTheFallbackName()
    {
        // Arrange
        var departed = Guid.Parse("0190a000-0000-7000-8000-0000000000d9");
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnList = (_, _, _) => new ItemActivityPage(
        [
            TestData.Activity(ActivityKind.Created, TestUsers.Owner.PersonId, 1),
            TestData.Activity(ActivityKind.Commented, TestUsers.Guest.PersonId, 2),
            TestData.Activity(ActivityKind.Commented, TestUsers.Owner.PersonId, 3),
            TestData.Activity(ActivityKind.Commented, departed, 4),
        ], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var actors = data?["items"]?.AsArray().Select(a => a?["actor"]).ToArray() ?? [];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actors.Select(a => a?["displayName"]?.GetValue<string>()), Is.EqualTo(new[]
            {
                TestUsers.Owner.DisplayName, TestUsers.Guest.DisplayName, TestUsers.Owner.DisplayName, "（已不在組織）",
            }));
            Assert.That(actors.Select(a => a?["personId"]?.GetValue<Guid>()), Is.EqualTo(new[]
            {
                TestUsers.Owner.PersonId, TestUsers.Guest.PersonId, TestUsers.Owner.PersonId, departed,
            }));
            var lookup = factory.Modules.People.FindManyCalls.Single();
            Assert.That(lookup.OrganizationId, Is.EqualTo(TestUsers.OrganizationId));
            Assert.That(lookup.PersonIds, Is.EquivalentTo(new[] { TestUsers.Owner.PersonId, TestUsers.Guest.PersonId, departed }));
        }
    }

    [Test]
    public async Task List_EmptyPage_ReturnsAnEmptyListWithoutLookingUpPeople()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = new JsonObject { ["items"] = new JsonArray(), ["nextCursor"] = null };
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
    }

    [Test]
    public async Task List_LimitAndCursor_ArePassedThrough()
    {
        // Act
        var query = await QueryForAsync("?limit=25&cursor=opaque-123");

        // Assert
        Assert.That(query, Is.EqualTo(new ActivityQuery { Limit = 25, Cursor = "opaque-123" }));
    }

    [TestCase("commented", ActivityKind.Commented)]
    [TestCase("type_changed", ActivityKind.TypeChanged)]
    [TestCase("attachment_added", ActivityKind.AttachmentAdded)]
    public async Task List_Kind_IsPassedThrough(string value, ActivityKind expected)
    {
        // Act
        var query = await QueryForAsync($"?kind={value}&limit=10");

        // Assert
        Assert.That(query, Is.EqualTo(new ActivityQuery { Kind = expected, Limit = 10 }));
    }

    [TestCase("kind=bogus")]
    [TestCase("kind=Commented")]
    [TestCase("kind=3")]
    [TestCase("kind=commented&kind=updated")]
    public async Task List_InvalidKind_Returns422WithTheAllowedValues(string parameters)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?{parameters}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|kind" }));
        Assert.That(factory.Modules.Activity.ListCalls, Is.Empty);
    }

    [Test]
    public async Task List_UnknownKind_ListsTheAllowedValuesInTheMessage()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?kind=bogus");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        var message = details.Single()?["message"]?.GetValue<string>();
        Assert.That(message, Does.Contain("bogus").And.Contain("commented").And.Contain("attachment_removed"));
    }

    [Test]
    public async Task List_KindAndLimitBothInvalid_ReportsBoth()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?kind=bogus&limit=ten");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EquivalentTo(new[] { "limit", "kind" }));
    }

    [Test]
    public async Task List_EmptyValuesAndUnknownParameters_AreIgnored()
    {
        // Act
        var query = await QueryForAsync("?limit=&cursor=&kind=&sort=at:desc");

        // Assert
        Assert.That(query, Is.EqualTo(new ActivityQuery()));
    }

    [TestCase("limit=ten", "limit", TestName = "List_LimitNotANumber_Returns422")]
    [TestCase("limit=-1", "limit", TestName = "List_NegativeLimit_Returns422")]
    [TestCase("limit=2.5", "limit", TestName = "List_FractionalLimit_Returns422")]
    [TestCase("limit=99999999999", "limit", TestName = "List_LimitTooBigForANumber_Returns422")]
    [TestCase("limit=10&limit=20", "limit", TestName = "List_LimitGivenTwice_Returns422")]
    [TestCase("cursor=a&cursor=b", "cursor", TestName = "List_CursorGivenTwice_Returns422")]
    public async Task List_InvalidParameter(string parameters, string expectedPath)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?{parameters}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|{expectedPath}" }));
        Assert.That(factory.Modules.Activity.ListCalls, Is.Empty);
    }

    [Test]
    public async Task List_LimitAndCursorBothInvalid_ReportsEachOfThem()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?limit=ten&cursor=a&cursor=b");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EquivalentTo(new[] { "limit", "cursor" }));
    }

    [TestCase("limit=0", TestName = "List_ModuleRejectsTheLimitRange_Returns422")]
    [TestCase("limit=201", TestName = "List_ModuleRejectsALimitOverTheMaximum_Returns422")]
    [TestCase("cursor=not-a-cursor", TestName = "List_ModuleRejectsTheCursor_Returns422")]
    public async Task List_ModuleRejectsTheQuery(string parameters)
    {
        // Arrange：範圍與游標的內容由模組檢查，API 照原樣傳過去，模組的 422 照原樣回給用戶端。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnList = (_, _, _) => new Failure(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, "查詢參數不正確。")
        {
            Details = [new FailureDetail(WorkErrorCodes.QueryInvalid, parameters.Split('=')[0], "不正確。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?{parameters}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|{parameters.Split('=')[0]}" }));
        Assert.That(factory.Modules.Activity.ListCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task List_ItemNotVisible_Returns404WithTheModuleCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnList = (_, _, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, "找不到這個項目。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
    }

    [Test]
    public async Task List_ModuleForbids_Returns403WithTheModuleCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnList = (_, _, _) => new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
    }

    private static async Task<ActivityQuery> QueryForAsync(string queryString)
    {
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var response = await client.GetAsync(Route + queryString);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return factory.Modules.Activity.ListCalls.Single().Query;
    }

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static JsonObject Change(string field, JsonNode? from, JsonNode? to) =>
        new() { ["field"] = field, ["from"] = from, ["to"] = to };

    private static JsonObject Expected(
        int sequence,
        string kind,
        string at,
        JsonArray? changes = null,
        string? fromState = null,
        string? toState = null,
        string? comment = null,
        long? blueprintVersion = null)
    {
        var actor = new JsonObject { ["personId"] = TestUsers.Owner.PersonId, ["displayName"] = TestUsers.Owner.DisplayName };
        return new JsonObject
        {
            ["id"] = TestData.ActivityId(sequence),
            ["kind"] = kind,
            ["actor"] = actor,
            ["at"] = at,
            ["changes"] = changes ?? new JsonArray(),
            ["fromState"] = fromState,
            ["toState"] = toState,
            ["comment"] = comment,
            ["replyToId"] = null,
            ["importedFrom"] = null,
            ["blueprintVersion"] = blueprintVersion,
            ["editedAt"] = null,
            ["deletedAt"] = null,
            ["deletedBy"] = null,
        };
    }
}
