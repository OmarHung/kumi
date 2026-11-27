using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Blueprint.Model;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>MCP 的 list_comments、list_deleted_items、get_project_info、get_planning（items:read），以及 get_item 多的 cycle、milestone、attachments、mine。</summary>
[TestFixture]
public sealed class McpProjectDetailToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite];

    private static readonly string[] NewTools = ["list_comments", "list_deleted_items", "get_project_info", "get_planning"];

    [Test]
    public async Task ListComments_ReturnsOnlyCommentsInTheDocumentedShape_AndAsksForTheCommentKind()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        var replyTo = TestData.ActivityId(1);
        mcp.Modules.Activity.OnList = (_, _, _) => new ItemActivityPage(
            [
                TestData.Activity(ActivityKind.Commented, TestUsers.Owner.PersonId, 1) with { Comment = "第一則" },
                TestData.Activity(ActivityKind.Commented, TestUsers.Member.PersonId, 2) with { Comment = "我的回覆", ReplyToId = replyTo, EditedAt = TestData.FirstActivityAt.AddHours(1) },
                TestData.Activity(ActivityKind.Commented, TestUsers.Owner.PersonId, 3) with { Comment = null, DeletedAt = TestData.FirstActivityAt.AddHours(2), DeletedBy = TestUsers.Owner.PersonId },
                TestData.Activity(ActivityKind.Commented, TestUsers.Owner.PersonId, 4) with { Comment = "匯入的", ImportedFrom = "monday" },
            ],
            "next");

        // Act
        var (isError, body) = await mcp.CallAsync("list_comments", new { itemKey = "WEB-1", limit = 10 });

        // Assert
        var comments = body["comments"]!.AsArray();
        var call = mcp.Modules.Activity.ListCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(call.Query.Kind, Is.EqualTo(ActivityKind.Commented));
            Assert.That(call.Query.Limit, Is.EqualTo(10));
            Assert.That(body["nextCursor"]?.GetValue<string>(), Is.EqualTo("next"));
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
            Assert.That(comments[0]!["id"]?.GetValue<Guid>(), Is.EqualTo(replyTo));
            Assert.That(comments[0]!["mine"]?.GetValue<bool>(), Is.False);
            Assert.That(comments[0]!["author"]!["name"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(comments[1]!["mine"]?.GetValue<bool>(), Is.True);
            Assert.That(comments[1]!["replyToId"]?.GetValue<Guid>(), Is.EqualTo(replyTo));
            Assert.That(comments[1]!["editedAt"], Is.Not.Null);
            Assert.That(comments[2]!["deleted"]?.GetValue<bool>(), Is.True);
            Assert.That(comments[2]!["body"], Is.Null);
            Assert.That(comments[3]!["imported"]?.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task ListComments_LongBody_IsCutAtTheLimitAndMarkedTruncated()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.Activity.OnList = (_, _, _) => new ItemActivityPage(
            [TestData.Activity(ActivityKind.Commented, TestUsers.Owner.PersonId, 1) with { Comment = new string('字', 2_500) }], null);

        // Act
        var (_, body) = await mcp.CallAsync("list_comments", new { itemKey = "WEB-1" });

        // Assert
        var comment = body["comments"]![0]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(comment["body"]!.GetValue<string>(), Has.Length.EqualTo(2_000));
            Assert.That(comment["bodyTruncated"]?.GetValue<bool>(), Is.True);
            Assert.That(mcp.Modules.Activity.ListCalls.Single().Query.Limit, Is.EqualTo(30), "預設 30");
        }
    }

    [TestCase("""{"itemKey":"WEB-1","limit":0}""")]
    [TestCase("""{"itemKey":"WEB-1","limit":51}""")]
    [TestCase("""{"itemKey":"WEB-1","extra":1}""")]
    [TestCase("""{}""")]
    public async Task ListComments_BadArguments_AreToolErrors(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, _) = await mcp.CallAsync("list_comments", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.Activity.ListCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ListDeletedItems_ReturnsTheDeletedItemsWithWhoDeletedThem()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.Items.OnListDeleted = (_, _, _) => new DeletedItemPage(
            [new DeletedItemSummary("WEB-7", "WEB", 7, "task", "刪錯的任務", "待辦", StateCategory.NotStarted, "WEB-1", TestUsers.Owner.PersonId, TestData.FirstActivityAt)],
            "more");

        // Act
        var (isError, body) = await mcp.CallAsync("list_deleted_items", new { projectKey = "WEB", limit = 5, cursor = "c1" });

        // Assert
        var item = body["items"]![0]!;
        var call = mcp.Modules.Items.ListDeletedCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That((call.ProjectKey, call.Query.Limit, call.Query.Cursor), Is.EqualTo(("WEB", 5, "c1")));
            Assert.That(item["key"]?.GetValue<string>(), Is.EqualTo("WEB-7"));
            Assert.That(item["title"]?.GetValue<string>(), Is.EqualTo("刪錯的任務"));
            Assert.That(item["parentKey"]?.GetValue<string>(), Is.EqualTo("WEB-1"));
            Assert.That(item["deletedBy"]!["name"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(body["nextCursor"]?.GetValue<string>(), Is.EqualTo("more"));
            Assert.That(body["notice"], Is.Not.Null);
        }
    }

    [Test]
    public async Task GetProjectInfo_ReturnsTheTextWithWhoUpdatedIt_AndCutsAnOverlongOne()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.Descriptions.OnGet = (_, _) => new ProjectDescription(new string('a', AiRules.MaxEditableProjectDescriptionLength + 5), TestData.FirstActivityAt, TestUsers.Owner.PersonId);

        // Act
        var (isError, body) = await mcp.CallAsync("get_project_info", new { projectKey = "WEB" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["text"]!.GetValue<string>(), Has.Length.EqualTo(AiRules.MaxEditableProjectDescriptionLength));
            Assert.That(body["truncated"]?.GetValue<bool>(), Is.True);
            Assert.That(body["updatedBy"]!["name"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(body["updatedAt"], Is.Not.Null);
        }
    }

    [Test]
    public async Task GetProjectInfo_NeverWritten_IsAnEmptyShape()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, body) = await mcp.CallAsync("get_project_info", new { projectKey = "WEB" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(body["text"], Is.Null);
            Assert.That(body["truncated"]?.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task GetPlanning_DefaultsToCurrentAndFutureOnes_AndIncludePastReturnsAll()
    {
        // Arrange：測試的時鐘是 2026-11-13，組織的時區是 Asia/Taipei。
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        var progress = new ItemProgress(4, 1, 0, null, null);
        mcp.Modules.Planning.OnListCycles = () => new List<Cycle>
        {
            new(Guid.NewGuid(), "舊衝刺", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14), progress),
            new(FakePlanningService.CycleId, "進行中", new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 22), progress),
        };
        mcp.Modules.Planning.OnListMilestones = () => new List<Milestone>
        {
            new(Guid.NewGuid(), "已過期", new DateOnly(2026, 11, 1), null, progress),
            new(FakePlanningService.MilestoneId, "年底上線", new DateOnly(2026, 12, 31), "上線", progress),
        };

        // Act
        var (_, current) = await mcp.CallAsync("get_planning", new { projectKey = "WEB" });
        var (_, all) = await mcp.CallAsync("get_planning", new { projectKey = "WEB", includePast = true });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(current["cycles"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "進行中" }));
            Assert.That(current["cycles"]![0]!["startDate"]?.GetValue<string>(), Is.EqualTo("2026-11-09"));
            Assert.That(current["cycles"]![0]!["progress"]!["total"]?.GetValue<int>(), Is.EqualTo(4));
            Assert.That(current["milestones"]!.AsArray().Select(m => m!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "年底上線" }));
            Assert.That(current["milestones"]![0]!["dueDate"]?.GetValue<string>(), Is.EqualTo("2026-12-31"));
            Assert.That(all["cycles"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(all["milestones"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(current["notice"], Is.Not.Null);
        }
    }

    [TestCase("list_deleted_items")]
    [TestCase("get_project_info")]
    [TestCase("get_planning")]
    public async Task ProjectTools_AgentOfAnotherProject_AreRefusedWithoutCallingTheService(string tool)
    {
        // Arrange：連線限定 WEB。
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync(tool, new { projectKey = "OPS" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.Items.ListDeletedCalls, Is.Empty);
            Assert.That(mcp.Modules.Planning.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task ListComments_AgentOfAnotherProject_IsRefusedWithoutCallingTheService()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("list_comments", new { itemKey = "OPS-1" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.Activity.ListCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ListTools_OrganizationJob_DoesNotListTheProjectTools()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(tool => tool.Name), Does.Not.Contain(NewTools[0]).And.Not.Contain(NewTools[1]).And.Not.Contain(NewTools[2]).And.Not.Contain(NewTools[3]));
    }

    [Test]
    public async Task ListTools_AnOAuthGrantWithItemsRead_ListsTheFourReadTools()
    {
        // Arrange：外部 OAuth 的授權只有一般範圍。
        await using var mcp = await McpHarness.StartAsync([.. AccessTokenScopes.Grantable]);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(tool => tool.Name), Is.SupersetOf(NewTools));
    }

    [Test]
    public async Task GetItem_ReturnsCycleMilestoneAttachmentsAndMineOnEachActivity()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.Items.OnGet = (_, key) => TestData.Item(key) with { CycleId = FakePlanningService.CycleId, MilestoneId = FakePlanningService.MilestoneId };
        mcp.Modules.Activity.OnList = (_, _, _) => new ItemActivityPage(
            [
                TestData.Activity(ActivityKind.Commented, TestUsers.Member.PersonId, 1) with { Comment = "我的" },
                TestData.Activity(ActivityKind.Commented, TestUsers.Owner.PersonId, 2) with { Comment = "別人的", ReplyToId = TestData.ActivityId(1) },
            ],
            null);

        // Act
        var (isError, body) = await mcp.CallAsync("get_item", new { itemKey = "WEB-1" });

        // Assert
        var activities = body["activities"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["cycle"]!["id"]?.GetValue<Guid>(), Is.EqualTo(FakePlanningService.CycleId));
            Assert.That(body["cycle"]!["name"]?.GetValue<string>(), Is.EqualTo("衝刺 1"));
            Assert.That(body["milestone"]!["name"]?.GetValue<string>(), Is.EqualTo("v1.0 上線"));
            Assert.That(body["attachments"]![0]!["fileName"]?.GetValue<string>(), Is.EqualTo("規格.pdf"));
            Assert.That(body["attachments"]![0]!["sizeBytes"]?.GetValue<long>(), Is.EqualTo(11));
            Assert.That(body["attachments"]![0]!.AsObject().ContainsKey("sha256"), Is.False, "不給雜湊與內容");
            Assert.That(activities[0]!["id"]?.GetValue<Guid>(), Is.EqualTo(TestData.ActivityId(1)));
            Assert.That(activities[0]!["mine"]?.GetValue<bool>(), Is.True);
            Assert.That(activities[1]!["mine"]?.GetValue<bool>(), Is.False);
            Assert.That(activities[1]!["replyToId"]?.GetValue<Guid>(), Is.EqualTo(TestData.ActivityId(1)));
        }
    }

    [Test]
    public async Task GetItem_WithoutCycleOrMilestone_OmitsThemAndDoesNotAskThePlanningService()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, body) = await mcp.CallAsync("get_item", new { itemKey = "WEB-1" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(body["cycle"], Is.Null);
            Assert.That(body["milestone"], Is.Null);
            Assert.That(mcp.Modules.Planning.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task GetItem_PlanningOrAttachmentReadsFail_StillReturnsTheItemWithNullNamesAndNoAttachments()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.Items.OnGet = (_, key) => TestData.Item(key) with { CycleId = FakePlanningService.CycleId, MilestoneId = FakePlanningService.MilestoneId };
        mcp.Modules.Planning.OnListCycles = () => new Failure(FailureKind.Forbidden, "x.forbidden", "不行。");
        mcp.Modules.Planning.OnListMilestones = () => new Failure(FailureKind.Forbidden, "x.forbidden", "不行。");
        mcp.Modules.Attachments.OnList = (_, _) => new Failure(FailureKind.Forbidden, "x.forbidden", "不行。");

        // Act
        var (isError, body) = await mcp.CallAsync("get_item", new { itemKey = "WEB-1" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["item"]!["key"]?.GetValue<string>(), Is.EqualTo("WEB-1"));
            Assert.That(body["cycle"]!["id"]?.GetValue<Guid>(), Is.EqualTo(FakePlanningService.CycleId));
            Assert.That(body["cycle"]!["name"], Is.Null);
            Assert.That(body["milestone"]!["id"]?.GetValue<Guid>(), Is.EqualTo(FakePlanningService.MilestoneId));
            Assert.That(body["milestone"]!["name"], Is.Null);
            Assert.That(body["attachments"]!.AsArray(), Is.Empty);
        }
    }
}
