using System.Net;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using PmTool.Work;
using static PmTool.Api.Tests.ProjectEndpointTests;

namespace PmTool.Api.Tests;

/// <summary>跨專案的項目清單 <c>GET /v1/items</c> 與類型 <c>GET /v1/item-types</c> 的 API 形狀：參數、回應、錯誤與權杖範圍。</summary>
[TestFixture]
public sealed class CrossProjectEndpointTests
{
    private const string ItemsRoute = "/v1/items";
    private const string TypesRoute = "/v1/item-types";

    [Test]
    public async Task List_SignedIn_ReturnsItemsAndTheNextCursorLikeTheProjectList()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnListAcross = (_, _, _) => new WorkItemPage(
            [TestData.Item("WEB-1", TestUsers.Owner.PersonId), TestData.Item("APP-2", TestUsers.Member.PersonId)], "page-2");
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(ItemsRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["items"]?.AsArray().Select(i => i?["key"]?.GetValue<string>()), Is.EqualTo(new[] { "WEB-1", "APP-2" }));
            Assert.That(data?["items"]?[0]?["assignee"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(data?["items"]?[0]?["createdBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(TestData.Item().CreatedBy));
            Assert.That(data?["nextCursor"]?.GetValue<string>(), Is.EqualTo("page-2"));
            Assert.That(factory.Modules.People.FindManyCalls, Has.Count.EqualTo(1), "負責人的名字一次查齊");
            Assert.That(factory.Modules.Items.ListCalls, Is.Empty, "不走單一專案的清單");
        }
    }

    [Test]
    public async Task List_NoParameters_UsesTheDefaultQueryAndAllProjects()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(ItemsRoute);
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var call = factory.Modules.Items.ListAcrossCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Owner.ToActor()));
            Assert.That(call.Scope.ProjectKeys, Is.Empty);
            Assert.That(call.Query.Limit, Is.EqualTo(WorkItemQuery.DefaultLimit));
            Assert.That(call.Query.SortBy, Is.EqualTo(WorkItemSortField.Number));
        }
    }

    [Test]
    public async Task List_RepeatedProjectParameter_BecomesTheScopeAndEmptyValuesAreIgnored()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{ItemsRoute}?project=web&project=APP&project=");
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(factory.Modules.Items.ListAcrossCalls.Single().Scope.ProjectKeys, Is.EqualTo(new[] { "web", "APP" }));
    }

    [Test]
    public async Task List_TheSameConditionsAsTheProjectList_AreParsedTheSameWay()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);
        var query = "?type=task&stateCategory=not_started&stateCategory=in_progress&assignee=me&createdBy=me&priority=high&dueTo=2026-11-22"
            + "&noDue=false&q=%E7%99%BB%E5%85%A5&sort=due_date:desc&limit=100&cursor=abc";

        // Act
        using var response = await client.GetAsync(ItemsRoute + query);
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        using var projectResponse = await client.GetAsync("/v1/projects/WEB/items" + query);
        await ApiRequests.AssertSuccessEnvelopeAsync(projectResponse, HttpStatusCode.OK);

        // Assert
        var across = factory.Modules.Items.ListAcrossCalls.Single().Query;
        var project = factory.Modules.Items.ListCalls.Single().Query;
        Assert.That(across.AssigneeId, Is.EqualTo(TestUsers.Member.PersonId));
        Assert.That(across.CreatedBy, Is.EqualTo(TestUsers.Member.PersonId));
        Assert.That(Describe(across), Is.EqualTo(Describe(project)));
    }

    [Test]
    public async Task List_BadParameter_Returns422WithThePathAndDoesNotCallTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{ItemsRoute}?sort=nope:asc&limit=abc");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(details.AsArray().Select(d => d?["path"]?.GetValue<string>()), Is.EquivalentTo(new[] { "sort", "limit" }));
        Assert.That(factory.Modules.Items.ListAcrossCalls, Is.Empty);
    }

    [Test]
    public async Task List_ModuleRejectsTheProjects_ReturnsItsFailure()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnListAcross = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, "太多專案") { Details = [new FailureDetail(WorkErrorCodes.QueryInvalid, "project", "太多專案")] };
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{ItemsRoute}?project=A1&project=A2");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("project"));
    }

    [Test]
    public async Task ItemTypes_SignedIn_ReturnsKeyNamesAndProjectKeys()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnItemTypes = _ =>
        [
            new ItemTypeSummary("bug", ["缺陷"], ["APP"]),
            new ItemTypeSummary("task", ["任務", "工作"], ["APP", "WEB"]),
        ];
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(TypesRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                System.Text.Json.Nodes.JsonNode.DeepEquals(
                    data,
                    System.Text.Json.Nodes.JsonNode.Parse("""
                        [{"key":"bug","names":["缺陷"],"projectKeys":["APP"]},
                         {"key":"task","names":["任務","工作"],"projectKeys":["APP","WEB"]}]
                        """)),
                Is.True,
                data?.ToJsonString());
            Assert.That(factory.Modules.Items.ItemTypeCalls, Is.EqualTo(new[] { TestUsers.Owner.ToActor() }));
        }
    }

    [TestCase(ItemsRoute)]
    [TestCase(TypesRoute)]
    public async Task NotSignedIn_Returns401(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(ItemsRoute)]
    [TestCase(TypesRoute)]
    public async Task AccessToken_ItemsReadWorksAndOtherScopesAreInsufficient(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var readOnly = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.ItemsRead);
        var others = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();

        // Act
        using var allowed = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, route, readOnly));
        using var denied = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, route, others));

        // Assert
        await ApiRequests.AssertSuccessEnvelopeAsync(allowed, HttpStatusCode.OK);
        await ApiRequests.AssertErrorEnvelopeAsync(denied, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    private static string Describe(WorkItemQuery query) =>
        $"{string.Join(',', query.Types)}|{string.Join(',', query.States)}|{string.Join(',', query.StateCategories)}|"
        + $"{query.AssigneeId}|{query.OnlyUnassigned}|{query.CreatedBy}|{string.Join(',', query.Priorities)}|{query.DueFrom}|{query.DueTo}|{query.DatesFrom}|{query.DatesTo}|{query.OnlyWithoutDueDate}|{query.Search}|{query.ParentKey}|{query.SortBy}|{query.Descending}|{query.Limit}|{query.Cursor}";
}
