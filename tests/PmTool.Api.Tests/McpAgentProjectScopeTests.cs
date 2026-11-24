using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>
/// 本機代理（agent:run）的 /mcp 請求一定要帶 X-Kumi-Project，所有工具只准碰那個專案；一般的權杖不受影響。
/// 標頭是傳輸層的設定，模型改不了：項目內容裡夾帶的指令因此碰不到別的專案。
/// </summary>
[TestFixture]
public sealed class McpAgentProjectScopeTests
{
    private static readonly AccessTokenScope[] Scopes = [.. AccessTokenScopes.All];

    [Test]
    public async Task Request_AgentTokenWithoutTheProjectHeader_IsRefusedAsAWholeAndNothingReachesAModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, Scopes);
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, secret);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("X-Kumi-Project"));
            Assert.That(factory.Modules.Items.GetCalls, Is.Empty);
        }
    }

    [TestCase("")]
    [TestCase("a b")]
    [TestCase("1WEB")]
    [TestCase("WEBWEBWEBWEB")]
    public async Task Request_AgentTokenWithABadProjectHeader_IsRefused(string project)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, Scopes);
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, secret, project: project);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Request_OrdinaryTokens_DoNotNeedTheHeader()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, secret);

        // Assert
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetItem_ItemOfAnotherProject_IsNotFoundWithoutAskingTheModule()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", Scopes);

        // Act
        var (isError, body) = await mcp.CallAsync("get_item", new { itemKey = "OPS-1" });
        var (otherCaseIsError, _) = await mcp.CallAsync("get_item", new { itemKey = "web-1" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("找不到項目"));
            Assert.That(mcp.Modules.Items.GetCalls.Select(call => call.ItemKey), Does.Not.Contain("OPS-1"));
            Assert.That(otherCaseIsError, Is.False, "同一個專案，大小寫不同照常");
        }
    }

    [TestCase("update_item", """{"itemKey":"OPS-1","title":"x"}""")]
    [TestCase("move_item", """{"itemKey":"OPS-1","state":"done"}""")]
    [TestCase("add_comment", """{"itemKey":"OPS-1","body":"x"}""")]
    [TestCase("create_item", """{"projectKey":"OPS","type":"task","title":"x"}""")]
    [TestCase("list_items", """{"projectKey":"OPS"}""")]
    [TestCase("get_blueprint", """{"projectKey":"OPS"}""")]
    [TestCase("describe_usage", """{"projectKey":"OPS"}""")]
    [TestCase("preview_changeset", """{"projectKey":"OPS","changeset":{"baseVersion":1}}""")]
    [TestCase("propose_changeset", """{"projectKey":"OPS","changeset":{"baseVersion":1}}""")]
    [TestCase("get_changeset", """{"projectKey":"OPS","number":1}""")]
    [TestCase("propose_item_changes", """{"projectKey":"OPS","itemKey":"OPS-1","comment":"x"}""")]
    [TestCase("propose_item_changes", """{"projectKey":"WEB","itemKey":"OPS-1","comment":"x"}""")]
    [TestCase("my_work", """{}""")]
    public async Task EveryProjectOrItemTool_AnotherProject_IsAToolErrorAndNoModuleIsCalled(string tool, string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", Scopes);

        // Act
        var (isError, _) = await mcp.CallAsync(tool, System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True, tool);
            Assert.That(mcp.Modules.Items.GetCalls, Is.Empty);
            Assert.That(mcp.Modules.Changes.TotalCalls, Is.Zero);
            Assert.That(mcp.Modules.ItemProposals.Creates, Is.Empty);
            Assert.That(mcp.Usage.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task ListProjects_ShowsOnlyTheScopedProject()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", Scopes);

        // Act
        var (isError, body) = await mcp.CallAsync("list_projects");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(body["projects"]!.AsArray().Select(project => project?["key"]?.GetValue<string>()), Is.All.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task SearchItems_SearchesOnlyInsideTheScopedProject()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("web", Scopes);

        // Act
        var (isError, _) = await mcp.CallAsync("search_items", new { query = "首頁" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.Items.SearchCalls, Is.Empty, "不用跨專案的搜尋");
            Assert.That(mcp.Modules.Items.ListCalls.Select(call => call.ProjectKey), Is.All.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task OrdinaryToken_CanStillTouchAnyProject()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, _) = await mcp.CallAsync("get_item", new { itemKey = "OPS-1" });

        // Assert
        Assert.That(mcp.Modules.Items.GetCalls.Select(call => call.ItemKey), Does.Contain("OPS-1"));
        Assert.That(isError, Is.False);
    }
}
