using System.Text.Json;
using ModelContextProtocol.Protocol;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>工具清單：名稱與範圍是對外的合約，參數的 Schema 只能用允許的關鍵字。</summary>
[TestFixture]
public sealed class McpToolCatalogTests
{
    private static readonly IReadOnlyDictionary<string, (AccessTokenScope Scope, bool ReadOnly)> Expected =
        new Dictionary<string, (AccessTokenScope, bool)>
        {
            ["list_projects"] = (AccessTokenScope.ItemsRead, true),
            ["search_items"] = (AccessTokenScope.ItemsRead, true),
            ["list_items"] = (AccessTokenScope.ItemsRead, true),
            ["my_work"] = (AccessTokenScope.ItemsRead, true),
            ["get_item"] = (AccessTokenScope.ItemsRead, true),
            ["create_item"] = (AccessTokenScope.ItemsWrite, false),
            ["update_item"] = (AccessTokenScope.ItemsWrite, false),
            ["move_item"] = (AccessTokenScope.ItemsWrite, false),
            ["add_comment"] = (AccessTokenScope.ItemsWrite, false),
            ["get_blueprint"] = (AccessTokenScope.BlueprintRead, true),
            ["list_templates"] = (AccessTokenScope.BlueprintRead, true),
            ["get_template"] = (AccessTokenScope.BlueprintRead, true),
            ["describe_usage"] = (AccessTokenScope.BlueprintRead, true),
            ["preview_changeset"] = (AccessTokenScope.BlueprintRead, true),
            ["get_changeset"] = (AccessTokenScope.BlueprintRead, true),
            ["propose_changeset"] = (AccessTokenScope.BlueprintWrite, false),
            ["propose_item_changes"] = (AccessTokenScope.AgentRun, false),
            ["propose_project"] = (AccessTokenScope.AgentRun, false),
            ["get_project"] = (AccessTokenScope.ItemsRead, true),
            ["list_people"] = (AccessTokenScope.ItemsRead, true),
            ["propose_member_changes"] = (AccessTokenScope.AgentRun, false),
        };

    // CLAUDE.md：JSON Schema 只能用這幾個關鍵字（同一份 Schema 要能直接拿去約束語言模型的輸出），description 是說明，不算約束。
    private static readonly HashSet<string> AllowedKeywords =
        ["type", "properties", "required", "additionalProperties", "items", "enum", "const", "$ref", "description"];

    [Test]
    public async Task ListTools_ReturnsTheEighteenToolsForAnOAuthGrant_WithoutTheAgentOnlyOnes()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(tool => tool.Name), Is.EquivalentTo(Expected.Keys.Where(name => name is not ("propose_item_changes" or "propose_project" or "propose_member_changes"))));
    }

    [Test]
    public async Task ListTools_LocalAgentToken_ReturnsOnlyTheTenLocalAgentTools()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", [.. AccessTokenScopes.All]);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(tool => tool.Name), Is.EquivalentTo(LocalAgentTools.Allowed));
    }

    [Test]
    public async Task ListTools_OnlyListsToolsTheTokensScopesCover()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(
            tools.Select(tool => tool.Name),
            Is.EquivalentTo(Expected.Where(pair => pair.Value.Scope is AccessTokenScope.ItemsRead or AccessTokenScope.BlueprintRead).Select(pair => pair.Key)));
    }

    [Test]
    public async Task ListTools_ATokenWithoutAgentRun_NeverSeesTheProposeTools()
    {
        // Arrange：OAuth 授權發的權杖沒有 agent:run。
        await using var mcp = await McpHarness.StartAsync([.. AccessTokenScopes.Grantable]);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => tool.Name), Does.Not.Contain("propose_item_changes").And.Not.Contain("propose_project").And.Not.Contain("propose_member_changes"));
            Assert.That(tools, Has.Count.EqualTo(Expected.Count - 3));
        }
    }

    [Test]
    public async Task ListTools_EveryToolHasAChineseDescriptionAndAnObjectSchemaThatRejectsExtraProperties()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in tools)
            {
                var schema = tool.ProtocolTool.InputSchema;
                Assert.That(tool.Description, Does.Match("[一-鿿]"), $"{tool.Name} 的描述要用繁體中文");
                Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"), tool.Name);
                Assert.That(schema.GetProperty("additionalProperties").ValueKind, Is.EqualTo(JsonValueKind.False), tool.Name);
            }
        }
    }

    [Test]
    public async Task ListTools_SchemasUseOnlyTheAllowedKeywords_AndEveryPropertyIsDescribed()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in tools)
            {
                AssertAllowedKeywords(tool.ProtocolTool.InputSchema, tool.Name);
                foreach (var property in tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject())
                {
                    Assert.That(property.Value.TryGetProperty("description", out _), Is.True, $"{tool.Name}.{property.Name} 要有說明");
                }
            }
        }
    }

    [Test]
    public async Task ListTools_ReadOnlyHintFollowsWhetherTheToolWrites()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in tools)
            {
                Assert.That(tool.ProtocolTool.Annotations?.ReadOnlyHint, Is.EqualTo(Expected[tool.Name].ReadOnly), tool.Name);
                Assert.That(tool.ProtocolTool.Annotations?.DestructiveHint, Is.False, tool.Name);
            }
        }
    }

    [Test]
    public async Task EveryTool_RefusesWhenTheTokenLacksItsScope_WithoutTouchingAnyModule()
    {
        // Arrange：每個工具各用一把「除了它的範圍以外都有」的權杖。
        foreach (var (name, (scope, _)) in Expected)
        {
            var others = McpTesting.AllScopes.Where(other => other != scope).ToArray();
            await using var mcp = await McpHarness.StartAsync(others);

            // Act
            var (isError, body) = await mcp.CallAsync(name, ValidArgumentsFor(name));

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(isError, Is.True, name);
                Assert.That(body["error"]?.GetValue<string>(), Does.Contain(AccessTokenScopes.NameOf(scope)).And.Contain("我的帳號"), name);
                Assert.That(mcp.Modules.Items.GetCalls, Is.Empty, name);
                Assert.That(mcp.Modules.Changes.TotalCalls, Is.Zero, name);
                Assert.That(mcp.Modules.Projects.ListCalls, Is.Empty, name);
            }
        }
    }

    private static void AssertAllowedKeywords(JsonElement schema, string tool)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in schema.EnumerateObject())
        {
            // properties 底下的名字是參數名稱，不是關鍵字；它們的值才是子 Schema。
            if (property.Name == "properties")
            {
                foreach (var parameter in property.Value.EnumerateObject())
                {
                    AssertAllowedKeywords(parameter.Value, tool);
                }

                continue;
            }

            Assert.That(AllowedKeywords, Does.Contain(property.Name), $"{tool} 的 Schema 用了不允許的關鍵字 {property.Name}");
            if (property.Name == "items")
            {
                AssertAllowedKeywords(property.Value, tool);
            }
        }
    }

    /// <summary>每個工具的一組合格參數：範圍不夠的測試要證明是被範圍擋下，不是被參數擋下。</summary>
    public static object ValidArgumentsFor(string tool) => tool switch
    {
        "search_items" => new { query = "首頁" },
        "list_items" or "get_blueprint" or "describe_usage" => new { projectKey = "WEB" },
        "get_item" => new { itemKey = "WEB-1" },
        "create_item" => new { projectKey = "WEB", type = "task", title = "新項目" },
        "update_item" => new { itemKey = "WEB-1", title = "新標題" },
        "move_item" => new { itemKey = "WEB-1", state = "doing" },
        "add_comment" => new { itemKey = "WEB-1", body = "好" },
        "get_template" => new { templateKey = "simple_tasks" },
        "propose_item_changes" => new { projectKey = "WEB", itemKey = "WEB-1", changes = new[] { new { field = "title", value = "x" } } },
        "preview_changeset" or "propose_changeset" => new { projectKey = "WEB", changeset = new { baseVersion = 1 } },
        "get_changeset" => new { projectKey = "WEB", number = 1 },
        _ => new { },
    };
}
