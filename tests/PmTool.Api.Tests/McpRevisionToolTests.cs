using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Mcp;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// MCP 的 propose_item_deletions、propose_bulk_item_changes、withdraw_proposal（給本機與主機代理）：
/// 前兩個只建立建議、第三個用工作找到的對話撤回；範圍都是 agent:run，錯誤照模組的說法。
/// </summary>
[TestFixture]
public sealed class McpRevisionToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite];

    private static readonly string[] RevisionTools = ["propose_item_deletions", "propose_bulk_item_changes", "withdraw_proposal"];

    [Test]
    public async Task ProposeItemDeletions_PassesKeysAndReasonToTheCreatorAndReturnsTheProposal()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_item_deletions", new { projectKey = "web", itemKeys = new[] { "WEB-45", "WEB-46" }, reason = "重複建立" });

        // Assert
        var create = mcp.Modules.ItemDeletionProposals.Creates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(create.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(create.Draft.ProjectKey, Is.EqualTo("web"), "專案代號大小寫交給模組");
            Assert.That(create.Draft.ItemKeys, Is.EqualTo(new[] { "WEB-45", "WEB-46" }));
            Assert.That(create.Draft.Reason, Is.EqualTo("重複建立"));
            Assert.That(create.Draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId), "對話編號來自伺服器找到的工作，撤回才找得到剛提出的建議");
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeItemDeletionProposals.ProposalId));
            Assert.That(body["itemKeys"]?.AsArray().Select(key => key?.GetValue<string>()), Is.EqualTo(new[] { "WEB-45", "WEB-46" }));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("刪除").And.Contain("不要說已經刪了"));
            Assert.That(mcp.Modules.Items.GetCalls, Is.Empty, "建議不碰項目");
        }
    }

    [TestCase("""{"projectKey":"WEB"}""", Description = "沒有 itemKeys")]
    [TestCase("""{"projectKey":"WEB","itemKeys":"WEB-1"}""", Description = "不是陣列")]
    [TestCase("""{"projectKey":"WEB","itemKeys":[5]}""")]
    [TestCase("""{"projectKey":"WEB","itemKeys":[""]}""")]
    [TestCase("""{"projectKey":"WEB","itemKeys":["WEB-1\u0000"]}""")]
    [TestCase("""{"projectKey":"WEB","itemKeys":["OPS-1"]}""", Description = "不是對話的專案")]
    [TestCase("""{"projectKey":"OPS","itemKeys":["OPS-1"]}""")]
    [TestCase("""{"projectKey":"WEB","itemKeys":["WEB-1"],"reason":"a\nb"}""", Description = "原因含換行")]
    [TestCase("""{"projectKey":"WEB","itemKeys":["WEB-1"],"extra":1}""")]
    public async Task ProposeItemDeletions_BadArguments_AreToolErrorsAndNothingIsCreated(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_item_deletions", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ItemDeletionProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeItemDeletions_MoreItemsThanTheLimit_IsRefusedBeforeCallingTheCreator()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_item_deletions", new
        {
            projectKey = "WEB",
            itemKeys = Enumerable.Range(1, AiRules.MaxItemsPerDeletionProposal + 1).Select(n => $"WEB-{n}").ToArray(),
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain(AiRules.MaxItemsPerDeletionProposal.ToString()));
            Assert.That(mcp.Modules.ItemDeletionProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeItemDeletions_CreatorFailure_IsGivenToTheModelAsIs()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.ItemDeletionProposals.OnCreate = _ =>
            new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "WEB-45 底下還有沒刪除的下層：WEB-46。");

        // Act
        var (isError, body) = await mcp.CallAsync("propose_item_deletions", new { projectKey = "WEB", itemKeys = new[] { "WEB-45" } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("WEB-46"));
            Assert.That(body["code"]?.GetValue<string>(), Is.EqualTo(AiErrorCodes.FieldInvalid));
        }
    }

    [Test]
    public async Task ProposeBulkItemChanges_PassesEveryEntryAndResolvesAssigneesToPersonIds()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_bulk_item_changes", new
        {
            projectKey = "WEB",
            items = new object[]
            {
                new { itemKey = "WEB-12", changes = new object[] { new { field = "dueDate", value = "2026-12-05" }, new { field = "assignee", value = "me" } }, comment = "順延一週" },
                new { itemKey = "WEB-13", changes = new object[] { new { field = "description", value = "新說明" } }, expectedDescriptionRevision = "rev-1" },
                new { itemKey = "WEB-14", comment = "只留言" },
            },
        });

        // Assert
        var draft = mcp.Modules.ItemBatchProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(draft.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId), "對話編號來自伺服器找到的工作，撤回才找得到剛提出的建議");
            Assert.That(draft.Items.Select(item => item.ItemKey), Is.EqualTo(new[] { "WEB-12", "WEB-13", "WEB-14" }));
            Assert.That(draft.Items[0].Changes.Select(change => change.Field), Is.EqualTo(new[] { "dueDate", "assignee" }));
            Assert.That(draft.Items[0].Changes[0].Value.GetString(), Is.EqualTo("2026-12-05"));
            Assert.That(draft.Items[0].Changes[1].Value.GetString(), Is.EqualTo(TestUsers.Member.PersonId.ToString()), "me 換成人員編號");
            Assert.That(draft.Items[0].Comment, Is.EqualTo("順延一週"));
            Assert.That(draft.Items[1].ExpectedDescriptionRevision, Is.EqualTo("rev-1"));
            Assert.That(draft.Items[2].Changes, Is.Empty);
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeItemBatchProposals.ProposalId));
            Assert.That(body["itemKeys"]?.AsArray().Select(key => key?.GetValue<string>()), Is.EqualTo(new[] { "WEB-12", "WEB-13" }), "結果回的是模組建立出來的建議");
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("全部套用").And.Contain("不要說已經改好了"));
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
        }
    }

    [TestCase("""{"projectKey":"WEB"}""", Description = "沒有 items")]
    [TestCase("""{"projectKey":"WEB","items":[5]}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"changes":[]}]}""", Description = "沒有 itemKey")]
    [TestCase("""{"projectKey":"WEB","items":[{"itemKey":"WEB-1","extra":1}]}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"itemKey":"OPS-1"}]}""", Description = "不是對話的專案")]
    [TestCase("""{"projectKey":"WEB","items":[{"itemKey":"WEB-1","changes":[{"value":"x"}]}]}""", Description = "changes 沒有 field")]
    [TestCase("""{"projectKey":"WEB","items":[{"itemKey":"WEB-1","changes":[{"field":"assignee","value":"nobody@example.com"}]}]}""")]
    [TestCase("""{"projectKey":"WEB","items":[{"itemKey":"WEB-1","expectedDescriptionRevision":"a\u0000"}]}""")]
    [TestCase("""{"projectKey":"WEB","items":[],"extra":1}""")]
    public async Task ProposeBulkItemChanges_BadArguments_AreToolErrorsAndNothingIsCreated(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_bulk_item_changes", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ItemBatchProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeBulkItemChanges_ABadEntry_ReportsItsIndex()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_bulk_item_changes", new
        {
            projectKey = "WEB", items = new object[] { new { itemKey = "WEB-1" }, new { itemKey = "WEB-2", surprise = true } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("items[1]").And.Contain("surprise"));
        }
    }

    [Test]
    public async Task ProposeBulkItemChanges_MoreItemsThanTheLimit_IsRefusedBeforeReadingAnyEntry()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act：每一筆都是壞的；先逐筆讀的話錯誤會是別的。
        var (isError, body) = await mcp.CallAsync("propose_bulk_item_changes", new
        {
            projectKey = "WEB", items = Enumerable.Range(0, AiRules.MaxItemsPerBatchProposal + 1).Select(_ => 5).ToArray(),
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain(AiRules.MaxItemsPerBatchProposal.ToString()));
        }
    }

    [Test]
    public async Task ProposeBulkItemChanges_CreatorFailureWithDetails_IsGivenToTheModelWithThePaths()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.ItemBatchProposals.OnCreate = _ => new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "有欄位不對。")
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "items[1].changes[0].value", "日期要是 yyyy-MM-dd。")],
        };

        // Act
        var (isError, body) = await mcp.CallAsync("propose_bulk_item_changes", new { projectKey = "WEB", items = new[] { new { itemKey = "WEB-1", comment = "x" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["issues"]?[0]?["path"]?.GetValue<string>(), Is.EqualTo("items[1].changes[0].value"));
        }
    }

    [Test]
    public async Task WithdrawProposal_UsesTheJobsConversationAndReturnsTheEntry()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        var reference = Guid.NewGuid().ToString();

        // Act
        var (isError, body) = await mcp.CallAsync("withdraw_proposal", new { proposalId = reference });

        // Assert
        var call = mcp.Modules.ProposalLedger.Withdrawals.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(call, Is.EqualTo((TestUsers.Member.ToActor(), FakeAgentToolBudget.ConversationId, reference)), "對話是伺服器從工作找到的");
            Assert.That(body["proposalId"]?.GetValue<string>(), Is.EqualTo(reference));
            Assert.That(body["kind"]?.GetValue<string>(), Is.EqualTo("new_items"));
            Assert.That(body["state"]?.GetValue<string>(), Is.EqualTo("dismissed"));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("已經撤回"));
        }
    }

    [Test]
    public async Task WithdrawProposal_DoesNotUseUpAProposalSlot()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        await mcp.CallAsync("withdraw_proposal", new { proposalId = "changeset:12" });

        // Assert
        Assert.That(mcp.Modules.AgentBudget.LocalCalls.Single().IsProposal, Is.False);
    }

    [TestCase(FailureKind.NotFound, AiErrorCodes.ProposalNotFound)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.ProposalClosed)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.ProposalNotWithdrawable)]
    public async Task WithdrawProposal_LedgerFailure_IsGivenToTheModelAsIs(FailureKind kind, string code)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.ProposalLedger.OnWithdraw = _ => new Failure(kind, code, "撤回不了。");

        // Act
        var (isError, body) = await mcp.CallAsync("withdraw_proposal", new { proposalId = "x" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["code"]?.GetValue<string>(), Is.EqualTo(code));
        }
    }

    [Test]
    public async Task WithdrawProposal_MissingProposalId_IsAToolError()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("withdraw_proposal", new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ProposalLedger.Withdrawals, Is.Empty);
        }
    }

    [Test]
    public async Task WithdrawProposal_OrganizationJob_IsAllowedAndUsesTheJobsConversation()
    {
        // Arrange：不選專案的組織對話也能撤回新專案建議。
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var tools = await mcp.Client.ListToolsAsync();
        var (isError, _) = await mcp.CallAsync("withdraw_proposal", new { proposalId = Guid.NewGuid().ToString() });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => tool.Name), Does.Contain("withdraw_proposal").And.Contain("propose_item_deletions").And.Contain("propose_bulk_item_changes"));
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.ProposalLedger.Withdrawals.Single().ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
        }
    }

    [Test]
    public async Task OrganizationJob_CanProposeDeletionsWithAProjectKey_OnTheJobsConversation()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (deletion, body) = await mcp.CallAsync("propose_item_deletions", new { projectKey = "WEB", itemKeys = new[] { "WEB-1" } });

        // Assert
        var created = mcp.Modules.ItemDeletionProposals.Creates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deletion, Is.False, body.ToJsonString());
            Assert.That(created.Draft.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(created.Draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(mcp.Modules.ConversationReads.Calls.Single().ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
        }
    }

    [Test]
    public async Task GeneralToken_NeverSeesTheRevisionTools()
    {
        // Arrange：一般的 MCP 用戶端沒有 agent:run。
        await using var mcp = await McpHarness.StartAsync([.. AccessTokenScopes.Grantable]);

        // Act
        var tools = await mcp.Client.ListToolsAsync();
        var (isError, body) = await mcp.CallAsync("propose_item_deletions", new { projectKey = "WEB", itemKeys = new[] { "WEB-1" } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => tool.Name), Does.Not.Contain("propose_item_deletions").And.Not.Contain("propose_bulk_item_changes").And.Not.Contain("withdraw_proposal"));
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("agent:run"));
            Assert.That(mcp.Modules.ItemDeletionProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task AgentToken_SeesAllThreeToolsAndTheyAreInTheAgentAllowList()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => tool.Name), Is.SupersetOf(RevisionTools));
            Assert.That(LocalAgentTools.Allowed, Is.SupersetOf(RevisionTools));
        }
    }

    [TestCase("propose_item_deletions", true)]
    [TestCase("propose_bulk_item_changes", true)]
    [TestCase("withdraw_proposal", false)]
    public void HostProposalTools_CountsOnlyTheProposeToolsAsProposals(string tool, bool expected) =>
        Assert.That(HostProposalTools.IsProposal(tool), Is.EqualTo(expected));
}
