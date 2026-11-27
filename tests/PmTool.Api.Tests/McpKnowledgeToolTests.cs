using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Api.Tests;

/// <summary>MCP 的知識庫工具：四個讀取（items:read）與 propose_knowledge_changes（agent:run），限定專案與只限組織的代理也能用。</summary>
[TestFixture]
public sealed class McpKnowledgeToolTests
{
    private static readonly AccessTokenScope[] AgentScopes = [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead];

    [Test]
    public async Task ListSpaces_ReturnsTheSpacesWithTheirPermissionsAndTheNotice()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.KnowledgeSpaces.OnList = _ => [KnowledgeSamples.Space(), KnowledgeSamples.ProjectSpace()];

        // Act
        var (isError, body) = await mcp.CallAsync("list_spaces");

        // Assert
        var spaces = body["spaces"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
            Assert.That(spaces[0]!["key"]?.GetValue<string>(), Is.EqualTo("HR"));
            Assert.That(spaces[0]!["canManage"]?.GetValue<bool>(), Is.True);
            Assert.That(spaces[1]!["project"]!["key"]?.GetValue<string>(), Is.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task GetPageTree_ReturnsParentNumbersAndRestrictions()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, body) = await mcp.CallAsync("get_page_tree", new { spaceKey = "HR" });

        // Assert
        var pages = body["pages"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(pages[0]!["parentNumber"], Is.Null);
            Assert.That(pages[1]!["parentNumber"]?.GetValue<int>(), Is.EqualTo(1));
            Assert.That(pages[1]!["restricted"]?.GetValue<bool>(), Is.True);
            Assert.That(mcp.Modules.KnowledgePages.Calls.Single(), Is.EqualTo(("tree", "HR", 0)));
        }
    }

    [Test]
    public async Task GetPage_ReturnsTheRevisionTheRestrictionAndMarksLongBodiesTruncated()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.KnowledgePages.OnGet = number =>
            KnowledgeSamples.Page(number, new string('字', AiRules.MaxEditableProjectDescriptionLength + 3), revision: 9);

        // Act
        var (isError, body) = await mcp.CallAsync("get_page", new { spaceKey = "HR", number = 2 });

        // Assert
        var page = body["page"]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(page["revision"]?.GetValue<long>(), Is.EqualTo(9));
            Assert.That(page["truncated"]?.GetValue<bool>(), Is.True);
            Assert.That(page["body"]!.GetValue<string>(), Has.Length.EqualTo(AiRules.MaxEditableProjectDescriptionLength));
            Assert.That(page["parentNumber"]?.GetValue<int>(), Is.EqualTo(1));
            Assert.That(body["restriction"]!["people"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(body["restriction"]!["canRelax"], Is.Not.Null);
            Assert.That(body["notice"]?.GetValue<string>(), Is.Not.Empty);
        }
    }

    [Test]
    public async Task ListPageTrash_ReturnsBatchesAndPassesTheCursor()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, body) = await mcp.CallAsync("list_page_trash", new { spaceKey = "HR", cursor = "c1" });

        // Assert
        var batch = body["batches"]![0]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(mcp.Modules.KnowledgePages.TrashQueries.Single().Query.Cursor, Is.EqualTo("c1"));
            Assert.That(batch["number"]?.GetValue<int>(), Is.EqualTo(2));
            Assert.That(batch["pageCount"]?.GetValue<int>(), Is.EqualTo(3));
            Assert.That(batch["formerParent"]!["title"]?.GetValue<string>(), Is.EqualTo("首頁"));
            Assert.That(body["notice"]?.GetValue<string>(), Is.Not.Empty);
        }
    }

    [TestCase("get_page", """{"spaceKey":"HR"}""")]
    [TestCase("get_page", """{"spaceKey":"HR","number":0}""")]
    [TestCase("get_page", """{"spaceKey":"HR","number":2,"extra":1}""")]
    [TestCase("get_page_tree", """{}""")]
    [TestCase("list_page_trash", """{"spaceKey":"H\u0000R"}""")]
    public async Task ReadTools_BadArguments_AreToolErrors(string tool, string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, _) = await mcp.CallAsync(tool, JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.KnowledgePages.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task ReadTools_WorkForProjectScopedAndOrganizationOnlyAgents()
    {
        // Arrange
        await using var projectAgent = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        await using var organizationAgent = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var project = await projectAgent.CallAsync("list_spaces");
        var organization = await organizationAgent.CallAsync("get_page_tree", new { spaceKey = "HR" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(project.IsError, Is.False, project.Body.ToJsonString());
            Assert.That(organization.IsError, Is.False, organization.Body.ToJsonString());
        }
    }

    [Test]
    public async Task ProposeKnowledgeChanges_CreatesAKnowledgeDraftWithoutAProject()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);
        var changes = new object[]
        {
            new { action = "create_page", spaceKey = "HR", title = "請假規則", body = "# 請假", @ref = "a" },
            new { action = "create_page", spaceKey = "HR", title = "細節", parentRef = "a" },
        };

        // Act
        var (isError, body) = await mcp.CallAsync("propose_knowledge_changes", new { changes, reason = "補文件" });

        // Assert
        var (actor, draft) = mcp.Modules.ActionProposals.Creates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeActionProposals.ProposalId));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("按「套用」"));
            Assert.That(actor.PersonId, Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That((draft.Kind, draft.ProjectKey, draft.Reason), Is.EqualTo((ActionKinds.Knowledge, (string?)null, "補文件")));
            Assert.That(draft.ConversationId, Is.Not.Null, "本機代理的工作對得到對話");
            Assert.That(draft.Payload.GetProperty("changes").GetArrayLength(), Is.EqualTo(2));
            Assert.That(draft.Payload.GetProperty("changes")[1].GetProperty("parentRef").GetString(), Is.EqualTo("a"));
        }
    }

    [Test]
    public async Task ProposeKnowledgeChanges_WhenTheCreatorRefuses_ReturnsTheIssuesAsAToolError()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);
        mcp.Modules.ActionProposals.OnCreate = _ => new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "知識庫建議裡有你沒有權限做的事，沒有建立。")
        {
            Details = [new FailureDetail(AiErrorCodes.Forbidden, "changes[0]", "只有組織管理員能建立組織空間。")],
        };

        // Act
        var (isError, body) = await mcp.CallAsync("propose_knowledge_changes", new { changes = new object[] { new { action = "create_space", key = "OPS", name = "維運" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["issues"]![0]!["message"]?.GetValue<string>(), Does.Contain("組織管理員"));
        }
    }

    [TestCase("""{}""")]
    [TestCase("""{"changes":"nope"}""")]
    [TestCase("""{"changes":[],"extra":1}""")]
    public async Task ProposeKnowledgeChanges_BadArguments_NeverReachTheCreator(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_knowledge_changes", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ActionProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeKnowledgeChanges_NeedsTheAgentRunScope()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var tools = (await mcp.Client.ListToolsAsync()).Select(t => t.Name).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools, Does.Contain("list_spaces").And.Contain("get_page"));
            Assert.That(tools, Does.Not.Contain("propose_knowledge_changes"));
        }
    }
}
