using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>MCP 的 propose_new_items（給本機與主機代理）：只建立建議、範圍是 agent:run、錯誤照模組的說法。</summary>
[TestFixture]
public sealed class McpNewItemProposalToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite];

    [Test]
    public async Task Call_PassesEveryItemAndTheGrantingPersonToTheCreatorAndReturnsTheProposal()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_new_items", new
        {
            projectKey = "WEB",
            items = new object[]
            {
                new { @ref = "epic", type = "epic", title = "搬遷", description = "說明", priority = "high", startDate = "2026-12-01", dueDate = "2026-12-31", estimate = 3.5 },
                new { type = "task", title = "盤點", parentRef = "epic", assignee = "me", fields = new { area = "infra" } },
                new { type = "task", title = "收尾", parentKey = "WEB-1", assignee = "none" },
            },
        });

        // Assert
        var create = mcp.Modules.NewItemProposals.Creates.Single();
        var items = create.Draft.Items;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(create.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(create.Draft.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(create.Draft.ConversationId, Is.Null);
            Assert.That(items.Select(item => item.Title), Is.EqualTo(new[] { "搬遷", "盤點", "收尾" }));
            Assert.That((items[0].Ref, items[0].Priority, items[0].StartDate, items[0].DueDate, items[0].Estimate), Is.EqualTo(("epic", "high", "2026-12-01", "2026-12-31", (decimal?)3.5m)));
            Assert.That((items[1].ParentRef, items[1].AssigneeId), Is.EqualTo(("epic", (Guid?)TestUsers.Member.PersonId)));
            Assert.That(items[1].Fields["area"].GetString(), Is.EqualTo("infra"));
            Assert.That((items[2].ParentKey, items[2].AssigneeId), Is.EqualTo(("WEB-1", (Guid?)null)));
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeNewItemProposals.ProposalId));
            Assert.That(body["projectKey"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(body["items"]?.AsArray(), Has.Count.EqualTo(2), "結果回的是模組建立出來的建議");
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("全部建立").And.Contain("不要說已經建好了"));
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
            Assert.That(mcp.Modules.Items.GetCalls, Is.Empty, "建議不碰項目");
        }
    }

    [Test]
    public async Task Call_AssigneeAsEmail_IsConvertedToAPersonId()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_new_items", new
        {
            projectKey = "WEB", items = new[] { new { type = "task", title = "T", assignee = TestUsers.Owner.Email } },
        });
        var (unknown, body) = await mcp.CallAsync("propose_new_items", new
        {
            projectKey = "WEB", items = new[] { new { type = "task", title = "T", assignee = "nobody@example.com" } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.NewItemProposals.Creates.Single().Draft.Items.Single().AssigneeId, Is.EqualTo(TestUsers.Owner.PersonId));
            Assert.That(unknown, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("items[0]").And.Contain("找不到可以指派的人"));
        }
    }

    [Test]
    public async Task Call_AssigneeNotFound_ListsTheActiveProjectMembers()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_new_items", new
        {
            projectKey = "WEB", items = new[] { new { type = "task", title = "T", assignee = "nobody@example.com" } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("專案成員").And.Contain(TestUsers.Owner.DisplayName).And.Contain(TestUsers.Owner.PersonId.ToString()));
            Assert.That(mcp.Modules.NewItemProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task Call_MoreItemsThanTheLimit_IsRefusedBeforeReadingAnyItem()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act：每一項的負責人都找不到；先逐項讀取的話錯誤會是找不到人。
        var (isError, body) = await mcp.CallAsync("propose_new_items", new
        {
            projectKey = "WEB",
            items = Enumerable.Range(0, AiRules.MaxNewItemsPerProposal + 1).Select(_ => new { type = "task", title = "T", assignee = "nobody@example.com" }).ToArray(),
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain(AiRules.MaxNewItemsPerProposal.ToString()).And.Not.Contain("找不到可以指派的人"));
        }
    }

    [TestCase("""{"projectKey":"WEB","items":"not json"}""")]
    [TestCase("""{"projectKey":"WEB","items":{"type":"task","title":"x"}}""")]
    [TestCase("""{"projectKey":"WEB","items":[5]}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"title":"x"}]}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"type":"task"}]}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"type":"task","title":"x","status":"done"}]}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"type":"task","title":"x","estimate":"many"}]}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"type":"task","title":"x","fields":[1]}]}""")]
    [TestCase("""{"items":[{"type":"task","title":"x"}]}""")]
    [TestCase("""{"projectKey":"WEB"}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"type":"task","title":"x"}],"extra":1}""")]
    public async Task Call_BadArguments_AreToolErrorsAndNothingIsCreated(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_new_items", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.NewItemProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task Call_ItemsGivenAsAJsonString_AreAccepted()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync(
            "propose_new_items", new { projectKey = "WEB", items = """[{"type":"task","title":"新任務"}]""" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.NewItemProposals.Creates.Single().Draft.Items.Single().Title, Is.EqualTo("新任務"));
        }
    }

    [Test]
    public async Task Call_CreatorFailure_IsAToolErrorWithTheIssues()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.NewItemProposals.OnCreate = _ => new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "新增項目的建議不合規定，沒有建立。")
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "items[0].type", "沒有類型「x」。")],
        };

        // Act
        var (isError, body) = await mcp.CallAsync(
            "propose_new_items", new { projectKey = "WEB", items = new[] { new { type = "x", title = "T" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["code"]?.GetValue<string>(), Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(body["issues"]?[0]?["path"]?.GetValue<string>(), Is.EqualTo("items[0].type"));
        }
    }

    [Test]
    public async Task Call_OtherProjectThanTheConversations_IsRefusedAndNothingIsCreated()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync(
            "propose_new_items", new { projectKey = "OPS", items = new[] { new { type = "task", title = "T" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.NewItemProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task TokenWithoutAgentRun_DoesNotSeeTheToolAndCannotCallIt()
    {
        // Arrange：一般的 MCP 用戶端有項目與藍圖的全部範圍，就是沒有 agent:run；它新增項目用 create_item。
        await using var mcp = await McpHarness.StartAsync([.. AccessTokenScopes.Grantable]);

        // Act
        var tools = await mcp.Client.ListToolsAsync();
        var (isError, body) = await mcp.CallAsync(
            "propose_new_items", new { projectKey = "WEB", items = new[] { new { type = "task", title = "T" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(t => t.Name), Does.Not.Contain("propose_new_items").And.Contain("create_item"));
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("agent:run"));
            Assert.That(mcp.Modules.NewItemProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task AgentToken_SeesTheTool()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(t => t.Name), Does.Contain("propose_new_items"));
    }
}
