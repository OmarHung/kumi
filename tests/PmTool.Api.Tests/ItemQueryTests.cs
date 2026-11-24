using System.Net;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>項目清單的查詢參數怎麼變成 <see cref="WorkItemQuery"/>。</summary>
[TestFixture]
public sealed class ItemQueryTests
{
    private const string Route = "/v1/projects/WEB/items";

    [Test]
    public async Task List_NoParameters_UsesTheDefaultQuery()
    {
        // Act
        var query = await QueryForAsync(string.Empty);

        // Assert
        Assert.That(Describe(query), Is.EqualTo(Describe(new WorkItemQuery())));
    }

    [Test]
    public async Task List_RepeatedFilters_BecomeLists()
    {
        // Act
        var query = await QueryForAsync("?type=task&type=bug&state=todo&state=doing&stateCategory=not_started&stateCategory=in_progress");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.Types, Is.EqualTo(new[] { "task", "bug" }));
            Assert.That(query.States, Is.EqualTo(new[] { "todo", "doing" }));
            Assert.That(query.StateCategories, Is.EqualTo(new[] { StateCategory.NotStarted, StateCategory.InProgress }));
        }
    }

    [Test]
    public async Task List_AssigneeMe_MeansTheSignedInPerson()
    {
        // Act
        var query = await QueryForAsync("?assignee=me");

        // Assert
        Assert.That(query.AssigneeId, Is.EqualTo(TestUsers.Member.PersonId));
        Assert.That(query.OnlyUnassigned, Is.False);
    }

    [Test]
    public async Task List_AssigneeUnassigned_AsksForItemsWithoutAnAssignee()
    {
        // Act
        var query = await QueryForAsync("?assignee=unassigned");

        // Assert
        Assert.That(query.OnlyUnassigned, Is.True);
        Assert.That(query.AssigneeId, Is.Null);
    }

    [Test]
    public async Task List_AssigneeId_IsPassedAsIs()
    {
        // Act
        var query = await QueryForAsync($"?assignee={TestUsers.Owner.PersonId}");

        // Assert
        Assert.That(query.AssigneeId, Is.EqualTo(TestUsers.Owner.PersonId));
    }

    [TestCase("number", WorkItemSortField.Number)]
    [TestCase("title", WorkItemSortField.Title)]
    [TestCase("state", WorkItemSortField.State)]
    [TestCase("priority", WorkItemSortField.Priority)]
    [TestCase("start_date", WorkItemSortField.StartDate)]
    [TestCase("due_date", WorkItemSortField.DueDate)]
    [TestCase("estimate", WorkItemSortField.Estimate)]
    [TestCase("created_at", WorkItemSortField.CreatedAt)]
    [TestCase("updated_at", WorkItemSortField.UpdatedAt)]
    public async Task List_SortField_MapsToTheSortField(string field, WorkItemSortField expected)
    {
        // Act
        var ascending = await QueryForAsync($"?sort={field}:asc");
        var descending = await QueryForAsync($"?sort={field}:desc");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ascending.SortBy, Is.EqualTo(expected));
            Assert.That(ascending.Descending, Is.False);
            Assert.That(descending.SortBy, Is.EqualTo(expected));
            Assert.That(descending.Descending, Is.True);
        }
    }

    [Test]
    public async Task List_SearchLimitAndCursor_ArePassedThrough()
    {
        // Act
        var query = await QueryForAsync("?q=%E9%A6%96%E9%A0%81&limit=25&cursor=opaque-123");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.Search, Is.EqualTo("首頁"));
            Assert.That(query.Limit, Is.EqualTo(25));
            Assert.That(query.Cursor, Is.EqualTo("opaque-123"));
        }
    }

    [Test]
    public async Task List_Parent_IsPassedAsTheParentKey()
    {
        // Act
        var query = await QueryForAsync("?parent=WEB-12&type=task");

        // Assert
        Assert.That(query.ParentKey, Is.EqualTo("WEB-12"));
        Assert.That(query.Types, Is.EqualTo(new[] { "task" }), "其他條件照舊");
    }

    [Test]
    public async Task List_PriorityRepeated_BecomesAListOfPriorities()
    {
        // Act
        var query = await QueryForAsync("?priority=none&priority=high&priority=urgent");

        // Assert
        Assert.That(query.Priorities, Is.EqualTo(new[] { Priority.None, Priority.High, Priority.Urgent }));
    }

    [Test]
    public async Task List_DueRange_IsParsedAsDates()
    {
        // Act
        var query = await QueryForAsync("?dueFrom=2026-11-16&dueTo=2026-11-22");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.DueFrom, Is.EqualTo(new DateOnly(2026, 11, 16)));
            Assert.That(query.DueTo, Is.EqualTo(new DateOnly(2026, 11, 22)));
            Assert.That(query.OnlyWithoutDueDate, Is.False);
        }
    }

    [Test]
    public async Task List_DatesRange_IsParsedAsDates()
    {
        // Act
        var query = await QueryForAsync("?datesFrom=2026-11-09&datesTo=2026-12-20");
        var onlyFrom = await QueryForAsync("?datesFrom=2026-11-09");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.DatesFrom, Is.EqualTo(new DateOnly(2026, 11, 9)));
            Assert.That(query.DatesTo, Is.EqualTo(new DateOnly(2026, 12, 20)));
            Assert.That(query.DueFrom, Is.Null);
            Assert.That(onlyFrom.DatesTo, Is.Null);
        }
    }

    [Test]
    public async Task List_NoDueTrue_AsksForItemsWithoutADueDate()
    {
        // Act
        var query = await QueryForAsync("?noDue=true");
        var off = await QueryForAsync("?noDue=false");

        // Assert
        Assert.That(query.OnlyWithoutDueDate, Is.True);
        Assert.That(off.OnlyWithoutDueDate, Is.False);
    }

    [TestCase("priority=critical", "priority", TestName = "List_UnknownPriority_Returns422")]
    [TestCase("priority=3", "priority", TestName = "List_PriorityAsNumber_Returns422")]
    [TestCase("dueFrom=tomorrow", "dueFrom", TestName = "List_DueFromNotADate_Returns422")]
    [TestCase("dueFrom=2026-13-01", "dueFrom", TestName = "List_DueFromNotARealDate_Returns422")]
    [TestCase("dueTo=2026-11-1", "dueTo", TestName = "List_DueToWithoutZeroPadding_Returns422")]
    [TestCase("dueTo=2026-11-01&dueTo=2026-11-02", "dueTo", TestName = "List_DueToGivenTwice_Returns422")]
    [TestCase("datesFrom=tomorrow", "datesFrom", TestName = "List_DatesFromNotADate_Returns422")]
    [TestCase("datesTo=2026-11-1", "datesTo", TestName = "List_DatesToWithoutZeroPadding_Returns422")]
    [TestCase("datesFrom=2026-11-01&datesFrom=2026-11-02", "datesFrom", TestName = "List_DatesFromGivenTwice_Returns422")]
    [TestCase("noDue=yes", "noDue", TestName = "List_NoDueNotABoolean_Returns422")]
    public async Task List_InvalidDueOrPriority(string parameters, string expectedPath)
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
        Assert.That(factory.Modules.Items.ListCalls, Is.Empty);
    }

    [Test]
    public async Task List_EmptyValues_AreTreatedAsAbsent()
    {
        // Act：網頁把空的篩選欄位照樣送出來，不該變成「找類型是空字串的項目」。
        var query = await QueryForAsync("?type=&state=&stateCategory=&assignee=&q=&parent=&sort=&priority=&dueFrom=&dueTo=&datesFrom=&datesTo=&noDue=&limit=&cursor=");

        // Assert
        Assert.That(Describe(query), Is.EqualTo(Describe(new WorkItemQuery())));
    }

    [TestCase("sort=color:asc", "sort", TestName = "List_UnknownSortField_Returns422")]
    [TestCase("sort=number", "sort", TestName = "List_SortWithoutDirection_Returns422")]
    [TestCase("sort=number:up", "sort", TestName = "List_UnknownSortDirection_Returns422")]
    [TestCase("limit=ten", "limit", TestName = "List_LimitNotANumber_Returns422")]
    [TestCase("limit=99999999999", "limit", TestName = "List_LimitTooBigForANumber_Returns422")]
    [TestCase("assignee=bob", "assignee", TestName = "List_AssigneeNeitherKeywordNorId_Returns422")]
    [TestCase("assignee=me&assignee=unassigned", "assignee", TestName = "List_AssigneeGivenTwice_Returns422")]
    [TestCase("stateCategory=finished", "stateCategory", TestName = "List_UnknownStateCategory_Returns422")]
    [TestCase("stateCategory=1", "stateCategory", TestName = "List_StateCategoryAsNumber_Returns422")]
    [TestCase("q=a&q=b", "q", TestName = "List_SearchGivenTwice_Returns422")]
    [TestCase("parent=WEB-1&parent=WEB-2", "parent", TestName = "List_ParentGivenTwice_Returns422")]
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
        Assert.That(factory.Modules.Items.ListCalls, Is.Empty);
    }

    [Test]
    public async Task List_SeveralInvalidParameters_ReportsEachOfThem()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?sort=color:asc&limit=ten&assignee=bob");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EquivalentTo(new[] { "sort", "limit", "assignee" }));
    }

    [Test]
    public async Task List_FieldParameters_BecomeFiltersWithRepeatedValuesAndNone()
    {
        // Act
        var query = await QueryForAsync(
            "?field.client=client_a&field.client=client_b&field.client=_none&field.reviewer=me&field.blocked=true&field.tags=_none&field.empty=");

        // Assert：同一個欄位的值合在一起；_none 是 IncludeEmpty；空的值當作沒給。
        var filters = query.FieldFilters.OrderBy(f => f.Key, StringComparer.Ordinal).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filters.Select(f => f.Key), Is.EqualTo(new[] { "blocked", "client", "reviewer", "tags" }));
            Assert.That(filters[0].Values, Is.EqualTo(new[] { "true" }));
            Assert.That(filters[0].IncludeEmpty, Is.False);
            Assert.That(filters[1].Values, Is.EqualTo(new[] { "client_a", "client_b" }));
            Assert.That(filters[1].IncludeEmpty, Is.True);
            Assert.That(filters[2].Values, Is.EqualTo(new[] { "me" }));
            Assert.That(filters[3].Values, Is.Empty);
            Assert.That(filters[3].IncludeEmpty, Is.True);
        }
    }

    [Test]
    public async Task List_FieldPrefixInAnotherCase_IsStillAFieldCondition()
    {
        // Act：其他參數不分大小寫，這個也一樣；默默略過的話，使用者會以為已經篩了。
        var query = await QueryForAsync("?Field.client=client_a&FIELD.reviewer=me");

        // Assert
        Assert.That(query.FieldFilters.Select(f => f.Key).Order(StringComparer.Ordinal), Is.EqualTo(new[] { "client", "reviewer" }));
    }

    [Test]
    public async Task List_NoFieldParameters_HasNoFieldFilters()
    {
        // Act
        var query = await QueryForAsync("?type=task&fieldx=1");

        // Assert
        Assert.That(query.FieldFilters, Is.Empty);
    }

    [Test]
    public async Task List_ValueWithAComma_Returns422AtTheFieldParameter()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?field.client=a,b");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|field.client" }));
        Assert.That(factory.Modules.Items.ListCalls, Is.Empty);
    }

    [TestCase("field.client:asc", "client", false)]
    [TestCase("field.actual_hours:desc", "actual_hours", true)]
    public async Task List_SortByAField_UsesTheFieldSortWithItsKey(string sort, string key, bool descending)
    {
        // Act
        var query = await QueryForAsync($"?sort={sort}");

        // Assert
        Assert.That((query.SortBy, query.SortFieldKey, query.Descending), Is.EqualTo((WorkItemSortField.Field, key, descending)));
    }

    [TestCase("sort=field:asc")]
    [TestCase("sort=field.client")]
    [TestCase("sort=field.client:up")]
    public async Task List_BareFieldOrBadFieldSort_Returns422AtSortWithoutListingFieldAsAName(string parameters)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?{parameters}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        var message = details.Single()?["message"]?.GetValue<string>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|sort" }));
            Assert.That(message, Does.Not.Contain("、field、").And.Not.Contain("、field。").And.Not.Contain("field、"));
            Assert.That(message, Does.Contain("field.<欄位的 key>"));
        }
    }

    [Test]
    public async Task List_ModuleRejectsTheQuery_Returns422WithTheModuleCode()
    {
        // Arrange：limit 的範圍由模組檢查，API 只負責把字串變成數字。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnList = (_, _, _) => new Failure(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, "limit 超出範圍。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?limit=0");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
        Assert.That(factory.Modules.Items.ListCalls.Single().Query.Limit, Is.Zero);
    }

    private static async Task<WorkItemQuery> QueryForAsync(string queryString)
    {
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var response = await client.GetAsync(Route + queryString);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return factory.Modules.Items.ListCalls.Single().Query;
    }

    // 清單是不同的實體，record 的相等比的是參考；寫成字串再比。
    private static string Describe(WorkItemQuery query) =>
        $"{string.Join(',', query.Types)}|{string.Join(',', query.States)}|{string.Join(',', query.StateCategories)}|"
        + $"{query.AssigneeId}|{query.OnlyUnassigned}|{string.Join(',', query.Priorities)}|{query.DueFrom}|{query.DueTo}|{query.DatesFrom}|{query.DatesTo}|{query.OnlyWithoutDueDate}|{query.Search}|{query.ParentKey}|{query.FieldFilters.Count}|{query.SortBy}|{query.SortFieldKey}|{query.Descending}|{query.Limit}|{query.Cursor}";
}
