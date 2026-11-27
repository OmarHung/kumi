using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>MCP 的 find_items（items:read）：和內建助理同一份 <see cref="IAssistantItemFinder"/>，參數只轉換、不重做規則。</summary>
[TestFixture]
public sealed class McpFindItemsToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite];

    [Test]
    public async Task FindItems_OrdinaryToken_ReturnsTheDocumentedShapeAndPassesNoConversation()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("find_items", new { assignee = "anyone" });

        // Assert
        var call = mcp.Modules.ItemFinder.Calls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(call.ConversationId, Is.Null, "一般的 MCP 用戶端沒有對話，不記讀過的專案");
            Assert.That(call.Query.Limit, Is.EqualTo(30));
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
            Assert.That(body["hasMore"]?.GetValue<bool>(), Is.True);
            var first = body["items"]![0]!;
            Assert.That(first["key"]?.GetValue<string>(), Is.EqualTo("WEB-1"));
            Assert.That(first["projectKey"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(first["projectName"]?.GetValue<string>(), Is.EqualTo("網站"));
            Assert.That(first["stateCategory"]?.GetValue<string>(), Is.EqualTo("in_progress"));
            Assert.That(first["dueDate"]?.GetValue<string>(), Is.EqualTo("2026-12-01"));
            Assert.That(first["assignee"]?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeAssistantItemFinder.FoundPersonId));
            Assert.That(first["createdBy"]?["name"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.DisplayName));
            Assert.That(body["items"]![1]!["assignee"], Is.Null);
        }
    }

    [Test]
    public async Task FindItems_LocalAgentJob_PassesTheConversationOfTheJobFoundOnTheServer()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("find_items", new { assignee = "me" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(mcp.Modules.ItemFinder.Calls.Single().ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
        }
    }

    [Test]
    public async Task FindItems_LocalAgentProjectJob_CanLookAtOtherProjects()
    {
        // Arrange：跨專案查是這個工具的目的，讀過的專案由服務記下，不靠工具的範圍擋。
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("find_items", new { projectKey = "OPS" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.ItemFinder.Calls.Single().Query.ProjectKey, Is.EqualTo("OPS"));
        }
    }

    [Test]
    public async Task FindItems_OrganizationJob_IsListedAndCallableWithTheJobsConversation()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var tools = await mcp.Client.ListToolsAsync();
        var (isError, body) = await mcp.CallAsync("find_items", new { assignee = "none" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => tool.Name), Does.Contain("find_items").And.Contain("list_people"));
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(mcp.Modules.ItemFinder.Calls.Single().ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
        }
    }

    [Test]
    public async Task FindItems_HostGrant_PassesTheConversationOfTheHostJob()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostGrants.Grant = factory.Modules.HostGrants.Grant! with { AllowedTools = ["find_items"] };
        await using var client = await McpTesting.ConnectAsync(factory, FakeHostToolGrants.Token);

        // Act
        var (isError, body) = await McpTesting.CallAsync(client, "find_items", new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(factory.Modules.ItemFinder.Calls.Single().ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
        }
    }

    [Test]
    public async Task FindItems_ConvertsEveryArgument()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        var personId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        // Act
        var (isError, body) = await mcp.CallAsync("find_items", new
        {
            assignee = personId.ToString(),
            createdBy = creatorId.ToString(),
            projectKey = "web",
            stateCategory = new[] { "not_started", "in_progress" },
            dueFrom = "2026-11-30",
            dueTo = "2026-12-06",
            text = "首頁",
            limit = 5,
        });

        // Assert
        var query = mcp.Modules.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That((query.Assignee, query.AssigneeId), Is.EqualTo((ItemFindAssignee.Person, (Guid?)personId)));
            Assert.That(query.CreatedBy, Is.EqualTo(creatorId));
            Assert.That(query.ProjectKey, Is.EqualTo("web"));
            Assert.That(query.StateCategories, Is.EqualTo(new[] { "not_started", "in_progress" }));
            Assert.That((query.DueFrom, query.DueTo), Is.EqualTo(((DateOnly?)new DateOnly(2026, 11, 30), (DateOnly?)new DateOnly(2026, 12, 6))));
            Assert.That(query.Text, Is.EqualTo("首頁"));
            Assert.That(query.Limit, Is.EqualTo(5));
        }
    }

    [TestCase("anyone", ItemFindAssignee.Anyone)]
    [TestCase("ME", ItemFindAssignee.Me)]
    [TestCase("none", ItemFindAssignee.Nobody)]
    public async Task FindItems_NamedAssignees_MapToTheEnum(string text, ItemFindAssignee expected)
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        await mcp.CallAsync("find_items", new { assignee = text });

        // Assert
        var query = mcp.Modules.ItemFinder.Calls.Single().Query;
        Assert.That((query.Assignee, query.AssigneeId), Is.EqualTo((expected, (Guid?)null)));
    }

    [Test]
    public async Task FindItems_DefaultsAndSingleStringCategoryAndCreatedByMe()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        await mcp.CallAsync("find_items", new { stateCategory = "done", createdBy = "me", overdue = true });

        // Assert
        var query = mcp.Modules.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.Assignee, Is.EqualTo(ItemFindAssignee.Anyone));
            Assert.That(query.StateCategories, Is.EqualTo(new[] { "done" }));
            Assert.That(query.CreatedBy, Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(query.Overdue, Is.True);
        }
    }

    [TestCase("assignee", "小明")]
    [TestCase("createdBy", "someone")]
    [TestCase("dueFrom", "明天")]
    [TestCase("dueTo", "2026/11/01")]
    [TestCase("stateCategory", "finished")]
    [TestCase("text", "a\u0000b")]
    [TestCase("limit", 0)]
    [TestCase("limit", 51)]
    public async Task FindItems_BadArguments_AreToolErrorsWithoutCallingTheService(string name, object value)
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("find_items", new Dictionary<string, object> { [name] = value });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain(name));
            Assert.That(mcp.Modules.ItemFinder.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task FindItems_ServiceFailure_IsReturnedAsAToolErrorWithItsCodeAndDetails()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.ItemFinder.OnFind = _ => new Failure(FailureKind.NotFound, "project.not_found", "找不到專案。");

        // Act
        var (isError, body) = await mcp.CallAsync("find_items", new { projectKey = "NOPE" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["code"]?.GetValue<string>(), Is.EqualTo("project.not_found"));
        }
    }

    [Test]
    public async Task FindItems_WithoutItemsReadScope_IsNotListedAndRefused()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.BlueprintRead);

        // Act
        var tools = await mcp.Client.ListToolsAsync();
        var (isError, _) = await mcp.CallAsync("find_items");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => tool.Name), Does.Not.Contain("find_items"));
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ItemFinder.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task ListPeople_OrganizationJob_WorksWithoutAProject()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("list_people", new { text = "前端" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            var call = mcp.Modules.Staffing.PeopleCalls.Single();
            Assert.That(call.Query.ProjectKey, Is.Null);
            Assert.That(call.Query.Text, Is.EqualTo("前端"));
        }
    }

    [Test]
    public async Task ListPeople_OrganizationJobGivingAProjectKey_ListsThatProjectAndRemembersItOnTheConversation()
    {
        // Arrange：組織對話的專案工具帶 projectKey：伺服器記下對話讀過這個專案。
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("list_people", new { projectKey = "WEB" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.Staffing.PeopleCalls.Single().Query.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(mcp.Modules.ConversationReads.Calls.Single().ProjectKeys, Is.EqualTo(new[] { "WEB" }));
        }
    }
}
