using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class ItemEndpointTests
{
    [Test]
    public async Task CreateItem_AllProperties_PassesThemToTheModuleAndReturns201()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new
        {
            type = "task",
            title = "寫首頁",
            assigneeId = TestUsers.Member.PersonId,
            priority = "urgent",
            startDate = "2026-11-01",
            dueDate = "2026-11-30",
            estimate = 2.5,
            parentKey = "WEB-1",
            fields = new { impact = "high", score = 3 },
        };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items", ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);
        var (actor, projectKey, item) = factory.Modules.Items.CreateCalls.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/v1/items/WEB-1"));
            Assert.That(data?["title"]?.GetValue<string>(), Is.EqualTo("寫首頁"));
            Assert.That(actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(projectKey, Is.EqualTo("WEB"));
            Assert.That(item.Type, Is.EqualTo("task"));
            Assert.That(item.AssigneeId, Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(item.Priority, Is.EqualTo(Priority.Urgent));
            Assert.That(item.StartDate, Is.EqualTo(new DateOnly(2026, 11, 1)));
            Assert.That(item.DueDate, Is.EqualTo(new DateOnly(2026, 11, 30)));
            Assert.That(item.Estimate, Is.EqualTo(2.5m));
            Assert.That(item.ParentKey, Is.EqualTo("WEB-1"));
            Assert.That(item.Fields.Keys, Is.EquivalentTo(new[] { "impact", "score" }));
            Assert.That(item.Fields["impact"].GetString(), Is.EqualTo("high"));
            Assert.That(item.Fields["score"].GetInt32(), Is.EqualTo(3));
        }
    }

    [Test]
    public async Task CreateItem_OnlyTypeAndTitle_UsesTheDefaults()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items", ApiRequests.Json(new { type = "task", title = "寫首頁" }));
        var item = factory.Modules.Items.CreateCalls.Single().Item;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(item.Priority, Is.EqualTo(Priority.None));
            Assert.That(item.AssigneeId, Is.Null);
            Assert.That(item.Fields, Is.Empty);
        }
    }

    [Test]
    public async Task CreateItem_PriorityNull_IsTreatedAsNone()
    {
        // Arrange：修改項目也這樣收，兩邊一致。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects/WEB/items", ApiRequests.Document("""{"type":"task","title":"x","priority":null}"""));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(factory.Modules.Items.CreateCalls.Single().Item.Priority, Is.EqualTo(Priority.None));
    }

    [Test]
    public async Task CreateItem_LongTextAtTheModuleLimitInChinese_ReachesTheModule()
    {
        // Arrange：長文字欄位最多 WorkRules.MaxLongTextFieldLength 個字，中文在 UTF-8 是一個字 3 個位元組。
        // 網頁的 JSON.stringify 不會把中文跳脫成 \uXXXX，所以內文照原樣組，不經過會跳脫的序列化。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var notes = new string('長', WorkRules.MaxLongTextFieldLength);
        var summary = new string('短', WorkRules.MaxTextFieldLength);
        var body = $$$"""{"type":"task","title":"寫首頁","fields":{"notes":"{{{notes}}}","summary":"{{{summary}}}"}}""";

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items", ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        Assert.That(factory.Modules.Items.CreateCalls.Single().Item.Fields["notes"].GetString(), Is.EqualTo(notes));
    }

    [Test]
    public async Task CreateItem_FullDescriptionAndFullLongTextInChinese_ReachesTheModule()
    {
        // Arrange：說明與一個長文字欄位都填到模組的上限，是網頁一次新增可能送出的最大內容。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var description = new string('說', WorkRules.MaxDescriptionLength);
        var notes = new string('長', WorkRules.MaxLongTextFieldLength);
        var body = $$$"""{"type":"task","title":"寫首頁","description":"{{{description}}}","fields":{"notes":"{{{notes}}}"}}""";

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items", ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        Assert.That(factory.Modules.Items.CreateCalls.Single().Item.Description, Is.EqualTo(description));
    }

    // 路徑和模組的 422 用同一套名字（文件裡的名字），網頁只要認一種寫法。
    [TestCase("""{"type":"task","title":"x","priority":4}""", "priority", TestName = "CreateItem_PriorityAsNumber_Returns400")]
    [TestCase("""{"type":"task","title":"x","priority":"critical"}""", "priority", TestName = "CreateItem_UnknownPriority_Returns400")]
    [TestCase("""{"type":"task","title":"x","dueDate":"2026/11/30"}""", "due_date", TestName = "CreateItem_DateInAnotherFormat_Returns400")]
    [TestCase("""{"type":"task","title":"x","dueDate":"2026-11-30T00:00:00Z"}""", "due_date", TestName = "CreateItem_DateWithTime_Returns400")]
    [TestCase("""{"type":"task","title":"x","startDate":"tomorrow"}""", "start_date", TestName = "CreateItem_StartDateNotADate_Returns400")]
    [TestCase("""{"type":"task","title":"x","estimate":"3"}""", "estimate", TestName = "CreateItem_NumberAsString_Returns400")]
    [TestCase("""{"type":"task","title":"x","assigneeId":"me"}""", "assignee", TestName = "CreateItem_AssigneeNotAGuid_Returns400")]
    [TestCase("""{"type":"task","title":"x","parentKey":7}""", "parent", TestName = "CreateItem_ParentKeyNotAString_Returns400")]
    [TestCase("""{"type":7,"title":"x"}""", "type", TestName = "CreateItem_TypeNotAString_Returns400")]
    [TestCase("""{"type":"task","title":"x","fields":"high"}""", "fields", TestName = "CreateItem_FieldsNotAnObject_Returns400")]
    [TestCase("""{"type":"task","title":"x","state":"done"}""", "state", TestName = "CreateItem_State_Returns400")]
    public async Task CreateItem_BadBody(string body, string expectedPath)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items", ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{ApiErrorCodes.BadRequest}|{expectedPath}" }));
        Assert.That(factory.Modules.Items.CreateCalls, Is.Empty);
    }

    [Test]
    public async Task GetItem_Found_ReturnsTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnGet = (_, key) => TestData.Item(key, TestUsers.Owner.PersonId);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-7");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = new JsonObject
        {
            ["key"] = "WEB-7",
            ["projectKey"] = "WEB",
            ["number"] = 7,
            ["type"] = "task",
            ["title"] = "寫首頁",
            ["state"] = "doing",
            ["stateCategory"] = "in_progress",
            ["assignee"] = new JsonObject { ["personId"] = TestUsers.Owner.PersonId, ["displayName"] = TestUsers.Owner.DisplayName },
            ["createdBy"] = new JsonObject { ["personId"] = TestUsers.Owner.PersonId, ["displayName"] = TestUsers.Owner.DisplayName },
            ["priority"] = "high",
            ["startDate"] = "2026-11-01",
            ["dueDate"] = "2026-11-30",
            ["estimate"] = 2.5,
            ["parentKey"] = null,
            ["cycleId"] = null,
            ["milestoneId"] = null,
            ["fields"] = new JsonObject { ["impact"] = "high" },
            ["createdAt"] = "2026-11-13T01:02:03Z",
            ["updatedAt"] = "2026-11-13T04:05:06Z",
            ["description"] = null,
            ["descriptionRevision"] = DescriptionRevisions.Of(null),
        };
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.Items.GetCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB-7") }));
    }

    [Test]
    public async Task GetItem_Unassigned_WritesNullAssigneeAndStillNamesTheCreator()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-1");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?.AsObject().ContainsKey("assignee") == true && data["assignee"] is null, Is.True);
        Assert.That(data?["createdBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
        Assert.That(factory.Modules.People.FindManyCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetItem_AssigneeNoLongerInTheOrganization_UsesTheFallbackName()
    {
        // Arrange
        var departed = Guid.Parse("0190a000-0000-7000-8000-0000000000d9");
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnGet = (_, key) => TestData.Item(key, departed);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-1");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["assignee"]?["displayName"]?.GetValue<string>(), Is.EqualTo("（已不在組織）"));
    }

    [Test]
    public async Task GetItem_NotFound_Returns404WithTheModuleCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnGet = (_, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, "找不到這個項目。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-404");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task Transition_Accepted_PassesTheTargetStateAndReturnsTheItem()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-3/transitions", ApiRequests.Json(new { to = "done" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["key"]?.GetValue<string>(), Is.EqualTo("WEB-3"));
        Assert.That(factory.Modules.Items.TransitionCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB-3", "done") }));
    }

    [Test]
    public async Task Transition_RuleRejects_Returns422WithTheMissingFields()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnTransition = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.TransitionFieldsRequired, "要先填負責人與到期日。")
            {
                Details =
                [
                    new FailureDetail(WorkErrorCodes.TransitionFieldsRequired, "assignee", "要先填負責人。"),
                    new FailureDetail(WorkErrorCodes.TransitionFieldsRequired, "due_date", "要先填到期日。"),
                ],
            };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-3/transitions", ApiRequests.Json(new { to = "done" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.TransitionFieldsRequired);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[]
        {
            $"{WorkErrorCodes.TransitionFieldsRequired}|assignee",
            $"{WorkErrorCodes.TransitionFieldsRequired}|due_date",
        }));
    }

    [Test]
    public async Task MyWork_SignedIn_ReturnsItemsWithNamesLookedUpOnce()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnMyWork = actor =>
        [
            TestData.Item("WEB-1", actor.PersonId),
            TestData.Item("WEB-2", actor.PersonId),
        ];
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/my-work");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["items"]?.AsArray().Select(i => i?["key"]?.GetValue<string>()), Is.EqualTo(new[] { "WEB-1", "WEB-2" }));
            Assert.That(data?["items"]?[1]?["assignee"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.DisplayName));
            Assert.That(factory.Modules.Items.MyWorkCalls, Is.EqualTo(new[] { TestUsers.Member.ToActor() }));
            Assert.That(factory.Modules.People.FindManyCalls.Single().PersonIds, Is.EquivalentTo(new[] { TestUsers.Member.PersonId, TestData.Item().CreatedBy }), "負責人與建立者的名字一次查齊");
        }
    }

    [Test]
    public async Task ListItems_SignedIn_ReturnsItemsAndTheNextCursor()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnList = (_, _, _) => new WorkItemPage(
            [TestData.Item("WEB-1", TestUsers.Owner.PersonId), TestData.Item("WEB-2", TestUsers.Member.PersonId)], "page-2");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/items");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["items"]?.AsArray(), Has.Count.EqualTo(2));
            Assert.That(data?["nextCursor"]?.GetValue<string>(), Is.EqualTo("page-2"));
            Assert.That(data?["items"]?[0]?["assignee"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(factory.Modules.People.FindManyCalls, Has.Count.EqualTo(1));
            Assert.That(factory.Modules.Items.ListCalls.Single().ProjectKey, Is.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task ListItems_LastPage_WritesNullCursor()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/items");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?.AsObject().ContainsKey("nextCursor") == true && data["nextCursor"] is null, Is.True);
    }
}
