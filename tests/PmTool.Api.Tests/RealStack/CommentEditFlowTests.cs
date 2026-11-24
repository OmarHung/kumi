using System.Net;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 留言的編輯、刪除與復原走整個真的堆疊：版本照次序留著，刪除的留言留在原位但從 API 與 MCP 都讀不到內容，
/// 復原之後內容與版本都回來。
/// </summary>
[TestFixture]
public sealed class CommentEditFlowTests : RealStackTestBase
{
    [Test]
    public async Task EditTwiceDeleteAndRestore_KeepsVersionsHidesTheBodyEverywhereAndBringsItBack()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "EDIT", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "EDIT", new { type = "task", title = "寫首頁" });
        var comment = await RealStackSteps.CommentAsync(owner.Client, "EDIT-1", "第一版的內容");
        var route = $"/v1/items/EDIT-1/comments/{comment["id"]!.GetValue<Guid>():D}";
        var placeAt = comment["at"]!.GetValue<DateTimeOffset>();
        var itemBefore = await RealStackSteps.GetAsync(owner.Client, "/v1/items/EDIT-1");
        using var cli = Factory.CreateClient();
        var (access, _, _) = await OAuthFlowTests.ConnectAsync(owner.Client, cli);
        await using var mcp = await ConnectMcpAsync(access);

        // Act：編輯兩次
        var second = await SendDataAsync(owner.Client, HttpMethod.Patch, route, new { body = "第二版的內容" });
        var third = await SendDataAsync(owner.Client, HttpMethod.Patch, route, new { body = "第三版的內容" });
        var revisions = await RealStackSteps.GetAsync(owner.Client, $"{route}/revisions");
        var itemAfterEdits = await RealStackSteps.GetAsync(owner.Client, "/v1/items/EDIT-1");

        // Act：刪除
        var deleted = await SendDataAsync(owner.Client, HttpMethod.Delete, route);
        var activity = (await RealStackSteps.ActivityAsync(owner.Client, "EDIT-1"))["items"]!.AsArray();
        var revisionsOfDeleted = await owner.Client.GetAsync($"{route}/revisions");
        var editOfDeleted = await owner.Client.PatchAsync(route, ApiRequests.Json(new { body = "不該成功" }));
        var replyToDeleted = await owner.Client.PostAsync(
            "/v1/items/EDIT-1/comments", ApiRequests.Json(new { body = "回覆", replyTo = comment["id"]!.GetValue<Guid>() }));
        var (mcpError, mcpBody) = await McpTesting.CallAsync(mcp, "get_item", new { itemKey = "EDIT-1" });
        var deletedAgain = await SendDataAsync(owner.Client, HttpMethod.Delete, route);

        // Act：復原
        var restored = await SendDataAsync(owner.Client, HttpMethod.Post, $"{route}/restore");
        var revisionsAfterRestore = await RealStackSteps.GetAsync(owner.Client, $"{route}/revisions");
        var activityAfterRestore = (await RealStackSteps.ActivityAsync(owner.Client, "EDIT-1"))["items"]!.AsArray();

        // Assert
        var listedComment = activity.Single(a => a?["id"]?.GetValue<Guid>() == comment["id"]!.GetValue<Guid>())!;
        var viaMcp = mcpBody["activities"]!.AsArray().Single(a => a?["kind"]?.GetValue<string>() == "commented")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((second["comment"]!.GetValue<string>(), third["comment"]!.GetValue<string>()), Is.EqualTo(("第二版的內容", "第三版的內容")));
            Assert.That(third["editedAt"], Is.Not.Null);
            Assert.That(third["at"]!.GetValue<DateTimeOffset>(), Is.EqualTo(placeAt), "編輯不動時間軸上的位置");
            Assert.That(
                revisions["items"]!.AsArray().Select(r => (r!["number"]!.GetValue<int>(), r["body"]!.GetValue<string>(), r["isCurrent"]!.GetValue<bool>())),
                Is.EqualTo(new[] { (3, "第三版的內容", true), (2, "第二版的內容", false), (1, "第一版的內容", false) }));
            Assert.That(itemAfterEdits["updatedAt"]!.GetValue<DateTimeOffset>(), Is.EqualTo(itemBefore["updatedAt"]!.GetValue<DateTimeOffset>()));
            Assert.That(deleted["comment"], Is.Null);
            Assert.That(deleted["deletedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That((listedComment["comment"], listedComment["deletedAt"]), Is.EqualTo(((JsonNode?)null, listedComment["deletedAt"])));
            Assert.That(listedComment["deletedAt"], Is.Not.Null);
            Assert.That(listedComment.ToJsonString(), Does.Not.Contain("版的內容"), "API 的任何地方都不帶內容");
            Assert.That(mcpError, Is.False);
            Assert.That(viaMcp["comment"], Is.Null);
            Assert.That(viaMcp["deletedAt"], Is.Not.Null);
            Assert.That(mcpBody.ToJsonString(), Does.Not.Contain("版的內容"), "MCP 的 get_item 也讀不到內容");
            Assert.That(deletedAgain["deletedAt"]!.GetValue<DateTimeOffset>(), Is.EqualTo(deleted["deletedAt"]!.GetValue<DateTimeOffset>()));
            Assert.That(restored["comment"]!.GetValue<string>(), Is.EqualTo("第三版的內容"));
            Assert.That(restored["deletedAt"], Is.Null);
            Assert.That(restored["editedAt"], Is.Not.Null);
            Assert.That(revisionsAfterRestore["items"]!.AsArray(), Has.Count.EqualTo(3));
            Assert.That(
                activityAfterRestore.Single(a => a?["id"]?.GetValue<Guid>() == comment["id"]!.GetValue<Guid>())?["comment"]?.GetValue<string>(),
                Is.EqualTo("第三版的內容"));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(revisionsOfDeleted, HttpStatusCode.Conflict, WorkErrorCodes.CommentDeleted);
        await ApiRequests.AssertErrorEnvelopeAsync(editOfDeleted, HttpStatusCode.Conflict, WorkErrorCodes.CommentDeleted);
        await ApiRequests.AssertErrorEnvelopeAsync(replyToDeleted, HttpStatusCode.Conflict, WorkErrorCodes.CommentDeleted);
    }

    [Test]
    public async Task Edit_ByAnotherMember_Is403NotAuthorButAManagerCanDeleteIt()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "OTHR", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "OTHR", new { type = "task", title = "寫首頁" });
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, "Editor Example", "editor@example.com");
        await RealStackSteps.SetMemberAsync(owner.Client, "OTHR", person["personId"]!.GetValue<Guid>(), "editor");
        using var editor = await SignInAsAsync("editor@example.com", RealStackSteps.MemberPassword);
        var comment = await RealStackSteps.CommentAsync(editor.Client, "OTHR-1", "編輯者的留言");
        var route = $"/v1/items/OTHR-1/comments/{comment["id"]!.GetValue<Guid>():D}";

        // Act
        using var edit = await owner.Client.PatchAsync(route, ApiRequests.Json(new { body = "老闆改的" }));
        var deleted = await SendDataAsync(owner.Client, HttpMethod.Delete, route);
        using var restoreByAuthor = await editor.Client.PostAsync($"{route}/restore", content: null);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(edit, HttpStatusCode.Forbidden, WorkErrorCodes.CommentNotAuthor);
        await ApiRequests.AssertErrorEnvelopeAsync(restoreByAuthor, HttpStatusCode.Forbidden, WorkErrorCodes.CommentNotAuthor);
        Assert.That(deleted["deletedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
    }

    private async Task<McpClient> ConnectMcpAsync(string secret)
    {
        var http = Factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, McpTesting.Route),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {secret}" },
            },
            http,
            loggerFactory: null,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<JsonNode> SendDataAsync(HttpClient client, HttpMethod method, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = ApiRequests.Json(body);
        }

        using var response = await client.SendAsync(request);
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK) ?? throw new AssertionException("data 是 null");
    }
}
