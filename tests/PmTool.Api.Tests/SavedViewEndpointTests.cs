using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using PmTool.Work;
using static PmTool.Api.Tests.ProjectEndpointTests;

namespace PmTool.Api.Tests;

/// <summary>儲存的檢視的 API 形狀：路徑、內文、回應與錯誤；存取權杖打不進來。</summary>
[TestFixture]
public sealed class SavedViewEndpointTests
{
    private const string Route = "/v1/projects/WEB/saved-views";
    private static readonly Guid ViewId = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    private const string ExpectedView = """
        {"id":"018f0000-0000-7000-8000-000000000001","name":"我本週要交的","layout":"board",
         "query":{"type":"task","state":"in_progress","assignee":"me","q":"登入","sort":"due_date:asc","due":"week","priority":"high","cycle":null,"milestone":null,"fields":null,"group":null,"category":null,"project":null,"createdBy":null,"mine":null},
         "createdAt":"2026-11-13T04:05:06Z","updatedAt":"2026-11-13T04:05:06Z"}
        """;

    private const string FullBody = """
        {"name":"我本週要交的","layout":"board",
         "query":{"type":"task","state":"in_progress","assignee":"me","q":"登入","sort":"due_date:asc","due":"week","priority":"high"}}
        """;

    private static readonly SavedViewDraft FullDraft = new(
        "我本週要交的", SavedViewLayout.Board, new SavedViewQuery("task", "in_progress", "me", "登入", "due_date:asc", "week", "high"));

    [Test]
    public async Task List_SignedIn_PassesTheActorAndReturnsTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse($"[{ExpectedView}]")), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.SavedViews.ListCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB") }));
        }
    }

    [Test]
    public async Task List_UnsetConditions_AreWrittenAsNull()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.SavedViews.OnList = (_, _) =>
            new List<SavedView> { FakeSavedViewService.Sample(draft: new SavedViewDraft("全部", SavedViewLayout.Table, new SavedViewQuery(null, null, null, null, null, null, null))) };
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expectedQuery = JsonNode.Parse("""{"type":null,"state":null,"assignee":null,"q":null,"sort":null,"due":null,"priority":null,"cycle":null,"milestone":null,"fields":null,"group":null,"category":null,"project":null,"createdBy":null,"mine":null}""");
        Assert.That(JsonNode.DeepEquals(data?[0]?["query"], expectedQuery), Is.True, data?.ToJsonString());
        Assert.That(data?[0]?["layout"]?.GetValue<string>(), Is.EqualTo("table"));
    }

    [Test]
    public async Task Create_ValidBody_Returns201WithLocationAndPassesTheDraft()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(FullBody));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/projects/WEB/saved-views/{ViewId}"));
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(ExpectedView)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.SavedViews.CreateCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB", FullDraft) }));
        }
    }

    [TestCase("calendar", SavedViewLayout.Calendar)]
    [TestCase("timeline", SavedViewLayout.Timeline)]
    public async Task Create_CalendarAndTimelineLayouts_AreReadAndWrittenInSnakeCase(string name, SavedViewLayout layout)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            Route, ApiRequests.Json(new { name = "排程", layout = name, query = new { } }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        Assert.That(factory.Modules.SavedViews.CreateCalls.Single().Draft.Layout, Is.EqualTo(layout));
        Assert.That(data?["layout"]?.GetValue<string>(), Is.EqualTo(name));
    }

    [Test]
    public async Task Create_QueryWithoutConditions_PassesNulls()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            Route, ApiRequests.Json(new { name = "全部", layout = "table", query = new { } }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var call = factory.Modules.SavedViews.CreateCalls.Single();
        Assert.That(
            call.Draft, Is.EqualTo(new SavedViewDraft("全部", SavedViewLayout.Table, new SavedViewQuery(null, null, null, null, null, null, null))));
    }

    [Test]
    public async Task Create_FieldsAndGroup_ArePassedToTheModuleAndWrittenBack()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);
        const string body = """
            {"name":"依客戶","layout":"board",
             "query":{"fields":{"client":"client_a,_none","reviewer":"me"},"group":"field.client","sort":"field.hours:desc"}}
            """;

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var query = factory.Modules.SavedViews.CreateCalls.Single().Draft.Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                query.Fields,
                Is.EquivalentTo(new Dictionary<string, string> { ["client"] = "client_a,_none", ["reviewer"] = "me" }));
            Assert.That((query.Group, query.Sort), Is.EqualTo(("field.client", "field.hours:desc")));
            Assert.That(data?["query"]?["group"]?.GetValue<string>(), Is.EqualTo("field.client"));
            Assert.That(data?["query"]?["fields"] is JsonObject, Is.True, data?.ToJsonString());
        }
    }

    [TestCase("""{"name":"x","layout":"table","query":{"fields":"client=a"}}""", "query.fields")]
    [TestCase("""{"name":"x","layout":"table","query":{"fields":{"client":5}}}""", "query.fields.client")]
    [TestCase("""{"name":"x","layout":"table","query":{"group":5}}""", "query.group")]
    public async Task Create_FieldsOrGroupOfTheWrongShape_Returns400AndDoesNotCallTheModule(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), path);
        Assert.That(factory.Modules.SavedViews.CreateCalls, Is.Empty);
    }

    // 少了必填的屬性時，框架只說「缺了某個」，沒有指出路徑，和其他端點一樣。
    [TestCase("""{"layout":"table","query":{}}""", "")]
    [TestCase("""{"name":null,"layout":"table","query":{}}""", "name")]
    [TestCase("""{"name":"x","layout":"grid","query":{}}""", "layout")]
    [TestCase("""{"name":"x","layout":"Table","query":{}}""", "layout")]
    [TestCase("""{"name":"x","layout":0,"query":{}}""", "layout")]
    [TestCase("""{"name":"x","layout":"table"}""", "")]
    [TestCase("""{"name":"x","layout":"table","query":{"due":5}}""", "query.due")]
    [TestCase("""{"name":"x","layout":"table","query":{"search":"a"}}""", "query.search")]
    [TestCase("""{"name":"x","layout":"table","query":{},"extra":1}""", "extra")]
    public async Task Create_BodyOfTheWrongShape_Returns400AndDoesNotCallTheModule(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo(path));
            Assert.That(factory.Modules.SavedViews.CreateCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Create_NotJson_Returns415()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(FullBody, "text/plain"));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
        Assert.That(factory.Modules.SavedViews.CreateCalls, Is.Empty);
    }

    [Test]
    public async Task Replace_ValidBody_Returns200WithTheViewAndPassesTheId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"{Route}/{ViewId}", ApiRequests.Document(FullBody));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(ExpectedView)), Is.True, data?.ToJsonString());
            Assert.That(
                factory.Modules.SavedViews.ReplaceCalls,
                Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB", ViewId, FullDraft) }));
        }
    }

    [Test]
    public async Task Delete_Existing_Returns204WithoutBody()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.DeleteAsync($"{Route}/{ViewId}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
            Assert.That(factory.Modules.SavedViews.DeleteCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB", ViewId) }));
        }
    }

    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task IdIsNotAGuid_Returns404AndDoesNotCallTheModule(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{Route}/not-a-guid")
        {
            Content = method == "PUT" ? ApiRequests.Document(FullBody) : null,
        };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
        Assert.That(factory.Modules.SavedViews.ReplaceCalls.Count + factory.Modules.SavedViews.DeleteCalls.Count, Is.Zero);
    }

    [TestCase(FailureKind.NotFound, HttpStatusCode.NotFound, WorkErrorCodes.SavedViewNotFound)]
    [TestCase(FailureKind.NotFound, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound)]
    [TestCase(FailureKind.Conflict, HttpStatusCode.Conflict, WorkErrorCodes.SavedViewNameTaken)]
    [TestCase(FailureKind.Conflict, HttpStatusCode.Conflict, WorkErrorCodes.SavedViewLimitReached)]
    [TestCase(FailureKind.Forbidden, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden)]
    [TestCase(FailureKind.Invalid, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.SavedViewInvalid)]
    public async Task ModuleFailure_IsMappedToTheStatusAndCodeOnEveryEndpoint(FailureKind kind, HttpStatusCode status, string code)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failure = new Failure(kind, code, "原因") { Details = [new FailureDetail(code, "query.due", "到期不對")] };
        var views = factory.Modules.SavedViews;
        views.OnList = (_, _) => failure;
        views.OnCreate = (_, _, _) => failure;
        views.OnReplace = (_, _, _, _) => failure;
        views.OnDelete = (_, _, _) => failure;
        using var client = await SignedInClientAsync(factory);

        // Act
        var responses = new[]
        {
            await client.GetAsync(Route),
            await client.PostAsync(Route, ApiRequests.Document(FullBody)),
            await client.PutAsync($"{Route}/{ViewId}", ApiRequests.Document(FullBody)),
            await client.DeleteAsync($"{Route}/{ViewId}"),
        };

        // Assert
        foreach (var response in responses)
        {
            using (response)
            {
                var details = await ApiRequests.AssertFailureEnvelopeAsync(response, status, code);
                Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("query.due"));
            }
        }
    }

    [Test]
    public async Task EveryEndpoint_AccessTokenWithAnyScope_Returns403InsufficientScopeAndDoesNotCallTheModule()
    {
        // Arrange：檢視是網頁上的個人偏好，沒有宣告範圍，所以權杖不管有什麼範圍都打不進來。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();
        var requests = new[]
        {
            Request(HttpMethod.Get, Route, secret, null),
            Request(HttpMethod.Post, Route, secret, FullBody),
            Request(HttpMethod.Put, $"{Route}/{ViewId}", secret, FullBody),
            Request(HttpMethod.Delete, $"{Route}/{ViewId}", secret, null),
        };

        // Act and Assert
        foreach (var request in requests)
        {
            using (request)
            {
                using var response = await client.SendAsync(request);
                await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
            }
        }

        var views = factory.Modules.SavedViews;
        Assert.That(views.ListCalls.Count + views.CreateCalls.Count + views.ReplaceCalls.Count + views.DeleteCalls.Count, Is.Zero);
    }

    [TestCase("GET")]
    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task EveryEndpoint_NotSignedIn_Returns401(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        var route = method is "PUT" or "DELETE" ? $"{Route}/{ViewId}" : Route;
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = method is "POST" or "PUT" ? ApiRequests.Document(FullBody) : null,
        };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static HttpRequestMessage Request(HttpMethod method, string route, string secret, string? body)
    {
        var request = AccessTokenAuthenticationTests.Bearer(method, route, secret);
        if (body is not null)
        {
            request.Content = ApiRequests.Document(body);
        }

        return request;
    }
}
