using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>復原刪除、已刪除的清單與批次操作：API 只轉換格式與狀態碼，規則由工作引擎判斷。</summary>
[TestFixture]
public sealed class ItemRecoveryEndpointTests
{
    private static readonly string[] SummaryShape =
    [
        "key", "projectKey", "number", "type", "title", "state", "stateCategory", "parentKey", "deletedBy", "deletedAt",
    ];

    [Test]
    public async Task ListDeleted_Accepted_ReturnsTheSummaryShapeWithTheDeletersNameAndTheCursor()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var deletedAt = TestData.UpdatedAt;
        factory.Modules.Items.OnListDeleted = (_, _, _) => new DeletedItemPage(
            [
                new DeletedItemSummary("WEB-5", "WEB", 5, "task", "寫首頁", "doing", StateCategory.InProgress, "WEB-1", TestUsers.Owner.PersonId, deletedAt),
                new DeletedItemSummary("WEB-4", "WEB", 4, "task", "舊資料", "todo", StateCategory.NotStarted, null, null, deletedAt),
            ],
            "next-cursor");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/items/deleted?limit=2&cursor=abc");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var (actor, projectKey, query) = factory.Modules.Items.ListDeletedCalls.Single();

        // Assert
        var first = data?["items"]?[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first?.AsObject().Select(p => p.Key), Is.EquivalentTo(SummaryShape));
            Assert.That(first?["deletedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Owner.PersonId));
            Assert.That(first?["deletedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That((first?["stateCategory"]?.GetValue<string>(), first?["parentKey"]?.GetValue<string>()), Is.EqualTo(("in_progress", "WEB-1")));
            Assert.That(data?["items"]?[1]?.AsObject().ContainsKey("deletedBy") == true && data["items"]![1]!["deletedBy"] is null, Is.True);
            Assert.That(data?["nextCursor"]?.GetValue<string>(), Is.EqualTo("next-cursor"));
            Assert.That((actor, projectKey, query.Limit, query.Cursor), Is.EqualTo((TestUsers.Member.ToActor(), "WEB", 2, "abc")));
        }
    }

    [Test]
    public async Task ListDeleted_NoQuery_UsesTheDefaultLimitAndWritesANullCursorAtTheEnd()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/items/deleted");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(factory.Modules.Items.ListDeletedCalls.Single().Query, Is.EqualTo(new DeletedItemQuery()));
        Assert.That(data?.AsObject().ContainsKey("nextCursor") == true && data["nextCursor"] is null, Is.True);
        Assert.That(data?["items"]?.AsArray(), Is.Empty);
    }

    [TestCase("limit=abc", "limit")]
    [TestCase("limit=1&limit=2", "limit")]
    [TestCase("cursor=a&cursor=b", "cursor")]
    public async Task ListDeleted_BadQueryParameters_Return422WithTheParameterPathAndCallNothing(string query, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"/v1/projects/WEB/items/deleted?{query}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|{path}" }));
        Assert.That(factory.Modules.Items.ListDeletedCalls, Is.Empty);
    }

    [TestCase(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, HttpStatusCode.UnprocessableEntity)]
    public async Task ListDeleted_ModuleFails_ReturnsTheMatchingStatus(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnListDeleted = (_, _, _) => new Failure(kind, code, "模組說的原因。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/items/deleted");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task RestoreItem_Accepted_PassesTheKeyAndReturnsTheItemWithItsDescription()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnRestore = (_, key) => TestData.Item(key) with { Description = "說明" };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/restore", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["key"]?.GetValue<string>(), Is.EqualTo("WEB-7"));
            Assert.That(data?["description"]?.GetValue<string>(), Is.EqualTo("說明"));
            Assert.That(factory.Modules.Items.RestoreCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB-7") }));
        }
    }

    [TestCase(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.ItemTypeRemoved, HttpStatusCode.Conflict)]
    public async Task RestoreItem_ModuleFails_ReturnsTheMatchingStatus(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnRestore = (_, _) => new Failure(kind, code, "模組說的原因。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/restore", null);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task RestoreItem_ParentDeleted_Returns409WithTheParentDetail()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnRestore = (_, _) => new Failure(FailureKind.Conflict, WorkErrorCodes.ItemParentDeleted, "上層 WEB-3 也已刪除，先復原它。")
        {
            Details = [new FailureDetail(WorkErrorCodes.ItemParentDeleted, "parent", "上層 WEB-3 也已刪除，先復原它。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/restore", null);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.Conflict, WorkErrorCodes.ItemParentDeleted);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemParentDeleted}|parent" }));
    }

    [Test]
    public async Task BulkUpdate_State_PassesTheKeysAndTheStateAndReturnsListItemsAndFailures()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Bulk.OnUpdate = (_, _, update) => new BulkUpdateResult(
            [TestData.Item(update.Keys[0], TestUsers.Owner.PersonId)],
            [new BulkItemFailure("WEB-2", new Failure(FailureKind.Conflict, WorkErrorCodes.TransitionNotAllowed, "不能這樣轉。")
            {
                Details = [new FailureDetail(WorkErrorCodes.TransitionNotAllowed, "state", "流程不允許。")],
            })]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects/WEB/items/bulk-update", ApiRequests.Json(new { keys = new[] { "WEB-1", "WEB-2" }, state = "done" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var (actor, projectKey, update) = factory.Modules.Bulk.UpdateCalls.Single();

        // Assert
        var item = data?["updated"]?[0];
        var failure = data?["failed"]?[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That((actor, projectKey, update.State.Value, update.Keys), Is.EqualTo((TestUsers.Member.ToActor(), "WEB", "done", new[] { "WEB-1", "WEB-2" })));
            Assert.That(update.AssigneeId.HasValue || update.Priority.HasValue || update.DueDate.HasValue, Is.False);
            Assert.That(item?.AsObject().ContainsKey("description"), Is.False, "清單的形狀，沒有說明");
            Assert.That(item?["assignee"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(failure?["key"]?.GetValue<string>(), Is.EqualTo("WEB-2"));
            Assert.That(failure?["error"]?["code"]?.GetValue<string>(), Is.EqualTo(WorkErrorCodes.TransitionNotAllowed));
            Assert.That(failure?["error"]?["message"]?.GetValue<string>(), Is.EqualTo("不能這樣轉。"));
            Assert.That(ApiRequests.Describe((JsonArray)failure!["error"]!["details"]!), Is.EqualTo(new[] { $"{WorkErrorCodes.TransitionNotAllowed}|state" }));
        }
    }

    [Test]
    public async Task BulkUpdate_PropertiesGivenNullOrAbsent_ArePassedAsClearAndNotChanged()
    {
        // Arrange：負責人給 null（清掉）、優先度給 null（none）、到期日給值，開始日沒寫。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = """{"keys":["WEB-1"],"assigneeId":null,"priority":null,"dueDate":"2026-12-01","cycleId":null}""";

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/bulk-update", ApiRequests.Document(body));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var update = factory.Modules.Bulk.UpdateCalls.Single().Update;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((update.AssigneeId.HasValue, update.AssigneeId.Value), Is.EqualTo((true, (Guid?)null)));
            Assert.That((update.Priority.HasValue, update.Priority.Value), Is.EqualTo((true, Priority.None)));
            Assert.That((update.DueDate.HasValue, update.DueDate.Value), Is.EqualTo((true, (DateOnly?)new DateOnly(2026, 12, 1))));
            Assert.That((update.CycleId.HasValue, update.CycleId.Value), Is.EqualTo((true, (Guid?)null)));
            Assert.That((update.StartDate.HasValue, update.MilestoneId.HasValue, update.State.HasValue), Is.EqualTo((false, false, false)));
        }
    }

    [TestCase("""{"keys":["WEB-1"],"title":"x"}""", "title", TestName = "BulkUpdate_UnknownProperty_Returns400")]
    [TestCase("""{"keys":["WEB-1"],"priority":"loud"}""", "priority", TestName = "BulkUpdate_BadPriority_Returns400")]
    [TestCase("""{"keys":["WEB-1"],"dueDate":"2026/12/01"}""", "dueDate", TestName = "BulkUpdate_BadDate_Returns400")]
    [TestCase("""{"keys":"WEB-1"}""", "keys", TestName = "BulkUpdate_KeysNotAnArray_Returns400")]
    public async Task BulkUpdate_MalformedBody_Returns400WithThePathAndCallsNothing(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/bulk-update", ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Has.Some.StartsWith(path));
        Assert.That(factory.Modules.Bulk.UpdateCalls, Is.Empty);
    }

    [Test]
    public async Task BulkUpdate_KeysMissing_IsPassedAsEmptyAndTheModuleSaysBulkInvalid()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Bulk.OnUpdate = (_, _, update) => update.Keys.Count == 0
            ? new Failure(FailureKind.Invalid, WorkErrorCodes.BulkInvalid, "要給至少一個項目編號。")
            {
                Details = [new FailureDetail(WorkErrorCodes.BulkInvalid, "keys", "要給至少一個項目編號。")],
            }
            : new BulkUpdateResult([], []);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/bulk-update", ApiRequests.Json(new { priority = "high" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.BulkInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.BulkInvalid}|keys" }));
    }

    [TestCase("update", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase("update", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("delete", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase("delete", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("restore", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase("restore", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Bulk_ModuleFailsTheWholeRequest_ReturnsTheMatchingStatus(string operation, FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failure = new Failure(kind, code, "模組說的原因。");
        factory.Modules.Bulk.OnUpdate = (_, _, _) => failure;
        factory.Modules.Bulk.OnDelete = (_, _, _) => failure;
        factory.Modules.Bulk.OnRestore = (_, _, _) => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        object body = operation == "update" ? new { keys = new[] { "WEB-1" }, priority = "high" } : new { keys = new[] { "WEB-1" } };

        // Act
        using var response = await client.PostAsync($"/v1/projects/WEB/items/bulk-{operation}", ApiRequests.Json(body));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task BulkDelete_Accepted_ReturnsKeysWithDeletionTimesAndFailures()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Bulk.OnDelete = (_, _, keys) => new BulkDeleteResult(
            [new DeletedWorkItem(keys[0], TestData.UpdatedAt)],
            [new BulkItemFailure("WEB-1", new Failure(FailureKind.Conflict, WorkErrorCodes.ItemHasChildren, "底下還有 1 個項目。")
            {
                Details = [new FailureDetail(WorkErrorCodes.ItemHasChildren, "children/WEB-9", "還掛著。")],
            })]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/bulk-delete", ApiRequests.Json(new { keys = new[] { "WEB-4", "WEB-1" } }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = new JsonObject { ["key"] = "WEB-4", ["deletedAt"] = "2026-11-13T04:05:06Z" };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data?["deleted"]?[0], expected), Is.True, data?.ToJsonString());
            Assert.That(data?["failed"]?[0]?["error"]?["code"]?.GetValue<string>(), Is.EqualTo(WorkErrorCodes.ItemHasChildren));
            Assert.That(factory.Modules.Bulk.DeleteCalls.Single().Keys, Is.EqualTo(new[] { "WEB-4", "WEB-1" }));
        }
    }

    [Test]
    public async Task BulkRestore_Accepted_ReturnsListItemsAndFailures()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Bulk.OnRestore = (_, _, keys) => new BulkRestoreResult(
            [TestData.Item(keys[0])],
            [new BulkItemFailure("WEB-2", new Failure(FailureKind.Conflict, WorkErrorCodes.ItemParentDeleted, "上層 WEB-9 也已刪除，先復原它。"))]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/bulk-restore", ApiRequests.Json(new { keys = new[] { "WEB-1", "WEB-2" } }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["restored"]?[0]?["key"]?.GetValue<string>(), Is.EqualTo("WEB-1"));
            Assert.That(data?["restored"]?[0]?.AsObject().ContainsKey("description"), Is.False);
            Assert.That(data?["failed"]?[0]?["error"]?["code"]?.GetValue<string>(), Is.EqualTo(WorkErrorCodes.ItemParentDeleted));
            Assert.That(data?["failed"]?[0]?["error"]?["details"]?.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task BulkDelete_KeysMissing_IsPassedAsEmptyToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/bulk-delete", ApiRequests.Document("{}"));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(factory.Modules.Bulk.DeleteCalls.Single().Keys, Is.Empty);
    }
}
