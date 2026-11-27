using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 本機代理的權杖（agent:run）在 /mcp 上，能列也能叫的工具是本機代理的工具清單與權杖範圍的交集：
/// 引擎在成員的電腦上，工具的上限由伺服器定，不靠送給引擎的設定。
/// </summary>
[TestFixture]
public sealed class McpAgentToolLimitTests
{
    private static readonly string[] AgentTools =
    [
        "get_blueprint", "list_templates", "get_template", "describe_usage", "preview_changeset", "propose_changeset", "get_changeset",
        "get_item", "search_items", "propose_item_changes", "propose_new_items", "my_work",
        "get_project", "list_people", "propose_member_changes",
        "propose_item_deletions", "propose_bulk_item_changes", "withdraw_proposal",
    ];

    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite, AccessTokenScope.ItemsRead];

    private static readonly AccessTokenScope[] LegacyScopes = [.. AgentScopes, AccessTokenScope.ItemsWrite];

    [Test]
    public async Task ListTools_AgentRunToken_ListsOnlyTheLocalAgentToolsItsScopesCover()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(tool => tool.Name), Is.EquivalentTo(AgentTools));
    }

    [Test]
    public async Task ListTools_AgentRunTokenWithOnlyBlueprintRead_ListsTheReadToolsOfThatScope()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync(
            "WEB", AccessTokenScope.AgentRun, AccessTokenScope.BlueprintRead);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(
            tools.Select(tool => tool.Name),
            Is.EquivalentTo(new[] { "get_blueprint", "list_templates", "get_template", "describe_usage", "preview_changeset", "get_changeset", "propose_item_changes", "propose_new_items", "propose_member_changes", "propose_item_deletions", "propose_bulk_item_changes", "withdraw_proposal" }));
    }

    [Test]
    public async Task ListTools_AgentRunTokenWithItemsWrite_DoesNotListTheItemWriteTools()
    {
        // Arrange：舊的權杖（限制加上之前建立的）可能同時有 items:write。
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", LegacyScopes);

        // Act
        var tools = (await mcp.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        // Assert
        Assert.That(tools, Is.EquivalentTo(AgentTools));
    }

    [TestCase("update_item", """{"itemKey":"WEB-1","title":"x"}""")]
    [TestCase("create_item", """{"projectKey":"WEB","type":"task","title":"x"}""")]
    [TestCase("move_item", """{"itemKey":"WEB-1","state":"done"}""")]
    [TestCase("add_comment", """{"itemKey":"WEB-1","body":"x"}""")]
    [TestCase("list_items", """{"projectKey":"WEB"}""")]
    [TestCase("list_projects", """{}""")]
    public async Task CallTool_AgentRunTokenOutsideTheLocalAgentList_IsAToolErrorEvenWithTheScope(string tool, string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", LegacyScopes);
        var arguments = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Act
        var (isError, body) = await mcp.CallAsync(tool, arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("本機代理").And.Contain(tool));
            Assert.That(mcp.Modules.Items.GetCalls, Is.Empty);
            Assert.That(mcp.Modules.Items.ListCalls, Is.Empty);
        }
    }

    [Test]
    public async Task CallTool_AgentRunTokenOnTheLocalAgentList_StillWorks()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", LegacyScopes);

        // Act
        var (itemIsError, _) = await mcp.CallAsync("get_item", new { itemKey = "WEB-1" });
        var (blueprintIsError, _) = await mcp.CallAsync("get_blueprint", new { projectKey = "WEB" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(itemIsError, Is.False);
            Assert.That(blueprintIsError, Is.False);
            Assert.That(mcp.Modules.Items.GetCalls.Select(call => call.ItemKey), Does.Contain("WEB-1"));
        }
    }

    [Test]
    public async Task ListTools_OrdinaryTokenWithAllGrantableScopes_ListsEveryToolItsScopesCover()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(McpTesting.AllScopes);

        // Act
        var tools = (await mcp.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools, Does.Contain("update_item").And.Contain("list_projects").And.Contain("my_work"));
            Assert.That(tools, Does.Not.Contain("propose_item_changes"));
            Assert.That(tools, Has.Length.EqualTo(18));
        }
    }
}
