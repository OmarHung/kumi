using System.Net;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// list_comments、list_deleted_items、get_project_info、get_planning 從 /mcp 一路到資料庫：真的工作引擎。
/// 一般（OAuth）權杖讀得到的就是授權的人看得到的：只看自己項目的執行者讀不到別人項目的留言。
/// </summary>
[TestFixture]
public sealed class McpProjectReadFlowTests : RealStackTestBase
{
    private const string Project = "MCPRD";

    private SignedInClient _owner = null!;
    private SignedInClient _worker = null!;
    private string _ownerItemKey = string.Empty;
    private string _workerItemKey = string.Empty;
    private string _deletedItemKey = string.Empty;
    private Guid _ownerCommentId;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
        _worker = await AddMemberAsync(_owner, Project, "contributor", "Worker Example", "mcp-read-worker@example.com");
        var own = await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "老闆的任務" });
        var others = await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "執行者的任務", assigneeId = _worker.PersonId });
        var doomed = await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "刪掉的任務" });
        _ownerItemKey = own["key"]!.GetValue<string>();
        _workerItemKey = others["key"]!.GetValue<string>();
        _deletedItemKey = doomed["key"]!.GetValue<string>();
        _ownerCommentId = (await RealStackSteps.CommentAsync(_owner.Client, _ownerItemKey, "老闆自己的留言"))["id"]!.GetValue<Guid>();
        await RealStackSteps.CommentAsync(_owner.Client, _workerItemKey, "給執行者的留言");

        using var deleted = await _owner.Client.DeleteAsync($"/v1/items/{_deletedItemKey}");
        Assert.That(deleted.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.NoContent));
        using var description = await _owner.Client.PutAsync($"/v1/projects/{Project}/description", ApiRequests.Json(new { text = "## 網址\n- https://example.com" }));
        Assert.That(description.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var cycle = await _owner.Client.PostAsync(
            $"/v1/projects/{Project}/cycles", ApiRequests.Json(new { name = "遠期衝刺", startDate = "2099-01-01", endDate = "2099-01-14" }));
        Assert.That(cycle.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        using var past = await _owner.Client.PostAsync(
            $"/v1/projects/{Project}/cycles", ApiRequests.Json(new { name = "很久以前的衝刺", startDate = "2020-01-01", endDate = "2020-01-14" }));
        Assert.That(past.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _worker?.Dispose();
        _owner?.Dispose();
    }

    [Test]
    public async Task ListComments_OwnerReadsEveryItem_WorkerOnlyTheirOwn()
    {
        // Arrange
        await using var ownerMcp = await ConnectAsync(_owner);
        await using var workerMcp = await ConnectAsync(_worker);

        // Act
        var (ownerError, ownerBody) = await McpTesting.CallAsync(ownerMcp, "list_comments", new { itemKey = _ownerItemKey });
        var (workerOwnError, workerOwnBody) = await McpTesting.CallAsync(workerMcp, "list_comments", new { itemKey = _workerItemKey });
        var (workerOtherError, _) = await McpTesting.CallAsync(workerMcp, "list_comments", new { itemKey = _ownerItemKey });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ownerError, Is.False, ownerBody.ToJsonString());
            Assert.That(ownerBody["comments"]!.AsArray().Single()!["id"]!.GetValue<Guid>(), Is.EqualTo(_ownerCommentId));
            Assert.That(ownerBody["comments"]![0]!["mine"]!.GetValue<bool>(), Is.True);
            Assert.That(workerOwnError, Is.False, workerOwnBody.ToJsonString());
            Assert.That(workerOwnBody["comments"]![0]!["mine"]!.GetValue<bool>(), Is.False);
            Assert.That(workerOwnBody["comments"]![0]!["body"]!.GetValue<string>(), Is.EqualTo("給執行者的留言"));
            Assert.That(workerOtherError, Is.True, "看不到的項目當作不存在");
        }
    }

    [Test]
    public async Task ListDeletedItems_ShowsTheDeletedItemToTheOwner()
    {
        // Arrange
        await using var mcp = await ConnectAsync(_owner);

        // Act
        var (isError, body) = await McpTesting.CallAsync(mcp, "list_deleted_items", new { projectKey = Project });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["items"]!.AsArray().Select(item => item!["key"]!.GetValue<string>()), Is.EqualTo(new[] { _deletedItemKey }));
            Assert.That(body["items"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("刪掉的任務"));
        }
    }

    [Test]
    public async Task GetProjectInfo_ReturnsTheWrittenDescription()
    {
        // Arrange
        await using var mcp = await ConnectAsync(_owner);

        // Act
        var (isError, body) = await McpTesting.CallAsync(mcp, "get_project_info", new { projectKey = Project });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["text"]!.GetValue<string>(), Does.Contain("https://example.com"));
            Assert.That(body["truncated"]!.GetValue<bool>(), Is.False);
            Assert.That(body["updatedBy"]!["id"]!.GetValue<Guid>(), Is.EqualTo(_owner.PersonId));
        }
    }

    [Test]
    public async Task GetPlanning_HidesPastCyclesUnlessAsked()
    {
        // Arrange
        await using var mcp = await ConnectAsync(_owner);

        // Act
        var (_, current) = await McpTesting.CallAsync(mcp, "get_planning", new { projectKey = Project });
        var (_, all) = await McpTesting.CallAsync(mcp, "get_planning", new { projectKey = Project, includePast = true });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(NamesOf(current["cycles"]), Is.EqualTo(new[] { "遠期衝刺" }));
            Assert.That(NamesOf(all["cycles"]), Is.EquivalentTo(new[] { "遠期衝刺", "很久以前的衝刺" }));
        }
    }

    private static string[] NamesOf(JsonNode? list) => [.. list!.AsArray().Select(entry => entry!["name"]!.GetValue<string>())];

    private async Task<McpClient> ConnectAsync(SignedInClient person)
    {
        using var cli = Factory.CreateClient();
        var (access, _, _) = await OAuthFlowTests.ConnectAsync(person.Client, cli);
        var http = Factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, McpTesting.Route),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {access}" },
            },
            http,
            loggerFactory: null,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }
}
