using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using PmTool.Work;
using static PmTool.Api.Tests.ProjectEndpointTests;

namespace PmTool.Api.Tests;

/// <summary>「我的工作」的檢視 <c>/v1/my-work/saved-views</c> 的 API 形狀：路徑、內文、回應與錯誤；存取權杖打不進來。</summary>
[TestFixture]
public sealed class MyWorkSavedViewEndpointTests
{
    private const string Route = "/v1/my-work/saved-views";
    private static readonly Guid ViewId = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    private const string Body = """
        {"name":"完成","layout":"board",
         "query":{"type":"task","category":"done","project":"CM","group":"project","due":"week","priority":"high","mine":"created"}}
        """;

    private static readonly SavedViewDraft Draft = new(
        "完成",
        SavedViewLayout.Board,
        new SavedViewQuery("task", null, null, null, null, "week", "high", Category: "done", Project: "CM", Group: "project", Mine: "created"));

    private static readonly string ExpectedView = """
        {"id":"018f0000-0000-7000-8000-000000000001","name":"完成","layout":"board",
         "query":{"type":"task","state":null,"assignee":null,"q":null,"sort":null,"due":"week","priority":"high","cycle":null,"milestone":null,
                  "fields":null,"group":"project","category":"done","project":"CM","createdBy":null,"mine":"created"},
         "createdAt":"2026-11-13T04:05:06Z","updatedAt":"2026-11-13T04:05:06Z"}
        """;

    [Test]
    public async Task List_SignedIn_PassesTheActorAndReturnsAnArrayOfViews()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.SavedViews.OnListMyWork = _ => new List<SavedView> { FakeSavedViewService.Sample(draft: Draft) };
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse($"[{ExpectedView}]")), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.SavedViews.ListMyWorkCalls, Is.EqualTo(new[] { TestUsers.Owner.ToActor() }));
            Assert.That(factory.Modules.SavedViews.ListCalls, Is.Empty, "和專案的檢視是不同的呼叫");
        }
    }

    [Test]
    public async Task Create_ValidBody_Returns201WithLocationAndPassesTheDraft()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(Body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/my-work/saved-views/{ViewId}"));
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(ExpectedView)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.SavedViews.CreateMyWorkCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), Draft) }));
        }
    }

    [Test]
    public async Task Create_QueryWithoutConditions_PassesNulls()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { name = "全部", layout = "table", query = new { } }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        Assert.That(
            factory.Modules.SavedViews.CreateMyWorkCalls.Single().Draft,
            Is.EqualTo(new SavedViewDraft("全部", SavedViewLayout.Table, new SavedViewQuery(null, null, null, null, null, null, null))));
    }

    [TestCase("""{"layout":"table","query":{}}""", "")]
    [TestCase("""{"name":"x","layout":"grid","query":{}}""", "layout")]
    [TestCase("""{"name":"x","layout":"table"}""", "")]
    [TestCase("""{"name":"x","layout":"table","query":{"category":5}}""", "query.category")]
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
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo(path));
        Assert.That(factory.Modules.SavedViews.CreateMyWorkCalls, Is.Empty);
    }

    [Test]
    public async Task Create_NotJson_Returns415()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(Body, "text/plain"));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
        Assert.That(factory.Modules.SavedViews.CreateMyWorkCalls, Is.Empty);
    }

    [Test]
    public async Task Replace_ValidBody_Returns200WithTheViewAndPassesTheId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"{Route}/{ViewId}", ApiRequests.Document(Body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(ExpectedView)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.SavedViews.ReplaceMyWorkCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), ViewId, Draft) }));
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
            Assert.That(factory.Modules.SavedViews.DeleteMyWorkCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), ViewId) }));
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
            Content = method == "PUT" ? ApiRequests.Document(Body) : null,
        };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
        Assert.That(factory.Modules.SavedViews.ReplaceMyWorkCalls.Count + factory.Modules.SavedViews.DeleteMyWorkCalls.Count, Is.Zero);
    }

    [TestCase(FailureKind.NotFound, HttpStatusCode.NotFound, WorkErrorCodes.SavedViewNotFound)]
    [TestCase(FailureKind.Conflict, HttpStatusCode.Conflict, WorkErrorCodes.SavedViewNameTaken)]
    [TestCase(FailureKind.Conflict, HttpStatusCode.Conflict, WorkErrorCodes.SavedViewLimitReached)]
    [TestCase(FailureKind.Invalid, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.SavedViewInvalid)]
    public async Task ModuleFailure_IsMappedToTheStatusAndCodeOnEveryWriteEndpoint(FailureKind kind, HttpStatusCode status, string code)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failure = new Failure(kind, code, "原因") { Details = [new FailureDetail(code, "query.category", "類別不對")] };
        var views = factory.Modules.SavedViews;
        views.OnCreateMyWork = (_, _) => failure;
        views.OnReplaceMyWork = (_, _, _) => failure;
        views.OnDeleteMyWork = (_, _) => failure;
        using var client = await SignedInClientAsync(factory);

        // Act
        var responses = new[]
        {
            await client.PostAsync(Route, ApiRequests.Document(Body)),
            await client.PutAsync($"{Route}/{ViewId}", ApiRequests.Document(Body)),
            await client.DeleteAsync($"{Route}/{ViewId}"),
        };

        // Assert
        foreach (var response in responses)
        {
            using (response)
            {
                var details = await ApiRequests.AssertFailureEnvelopeAsync(response, status, code);
                Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("query.category"));
            }
        }
    }

    [Test]
    public async Task EveryEndpoint_AccessTokenWithAnyScope_Returns403InsufficientScopeAndDoesNotCallTheModule()
    {
        // Arrange：和專案的檢視一樣是網頁上的個人偏好，沒有宣告範圍。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();
        var requests = new[]
        {
            Request(HttpMethod.Get, Route, secret, null),
            Request(HttpMethod.Post, Route, secret, Body),
            Request(HttpMethod.Put, $"{Route}/{ViewId}", secret, Body),
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
        Assert.That(
            views.ListMyWorkCalls.Count + views.CreateMyWorkCalls.Count + views.ReplaceMyWorkCalls.Count + views.DeleteMyWorkCalls.Count, Is.Zero);
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
            Content = method is "POST" or "PUT" ? ApiRequests.Document(Body) : null,
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
