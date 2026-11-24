using ModelContextProtocol;
using PmTool.Api.Mcp;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>每個人每分鐘的工具呼叫次數；不認得的工具是協定的錯誤。</summary>
[TestFixture]
public sealed class McpToolLimitTests
{
    [Test]
    public async Task ToolCalls_BeyondThePerMinuteLimit_AreRefusedAsAToolError_AndOtherPeopleAreNotAffected()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        for (var call = 0; call < McpLimits.ToolCallsPerMinute; call++)
        {
            var (isError, _) = await mcp.CallAsync("list_projects");
            Assert.That(isError, Is.False, $"第 {call + 1} 次還在額度內");
        }

        // Act
        var (overLimit, body) = await mcp.CallAsync("list_projects");
        var ownerSecret = McpTesting.GrantToken(mcp.Factory, TestUsers.Owner, McpTesting.AllScopes);
        await using var owner = await McpTesting.ConnectAsync(mcp.Factory, ownerSecret);
        var (ownerError, _) = await McpTesting.CallAsync(owner, "list_projects");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overLimit, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("太頻繁").And.Contain("60"));
            Assert.That(ownerError, Is.False, "額度依人算，別人不受影響");
            Assert.That(mcp.Modules.Projects.ListCalls, Has.Count.EqualTo(McpLimits.ToolCallsPerMinute + 1), "超過的那次沒有走到模組");
        }
    }

    [Test]
    public async Task ToolCalls_RefusedForMissingScope_DoNotSpendTheLimit()
    {
        // Arrange：被範圍擋下的呼叫沒有做任何事，不該把額度耗光。
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        for (var call = 0; call < McpLimits.ToolCallsPerMinute + 5; call++)
        {
            await mcp.CallAsync("create_item", new { projectKey = "WEB", type = "task", title = "x" });
        }

        // Act
        var (isError, _) = await mcp.CallAsync("list_projects");

        // Assert
        Assert.That(isError, Is.False);
    }

    [Test]
    public async Task CallTool_UnknownName_IsAProtocolError()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var exception = Assert.ThrowsAsync<McpProtocolException>(async () => await mcp.CallAsync("drop_database"));

        // Assert
        Assert.That(exception!.Message, Does.Contain("drop_database"));
    }
}
