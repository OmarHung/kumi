using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Mcp;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// MCP 的 propose_planning_changes、propose_comment_changes、propose_item_restores、propose_project_changes（agent:run，只建立建議），
/// 以及 propose_item_changes、propose_bulk_item_changes 多的 replyTo。
/// </summary>
[TestFixture]
public sealed class McpProjectWriteProposalToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite];

    private static readonly string[] ProposalTools =
        ["propose_planning_changes", "propose_comment_changes", "propose_item_restores", "propose_project_changes"];

    [Test]
    public async Task ListTools_LocalAgentToken_IncludesTheFourProposalToolsAndMatchesTheAllowedList()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", [.. AccessTokenScopes.All]);

        // Act
        var tools = (await mcp.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools, Is.SupersetOf(ProposalTools));
            Assert.That(tools, Is.SupersetOf(new[] { "list_comments", "list_deleted_items", "get_project_info", "get_planning" }));
            Assert.That(tools, Is.EquivalentTo(LocalAgentTools.Allowed));
        }
    }

    [Test]
    public async Task ProposePlanningChanges_ReadsEveryFieldIntoTheDraft_AndReturnsTheProposalId()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        var cycleId = Guid.NewGuid();

        // Act
        var (isError, body) = await mcp.CallAsync("propose_planning_changes", new
        {
            projectKey = "WEB",
            changes = new object[]
            {
                new { target = "cycle", action = "create", name = "第 49 週", startDate = "2026-11-30", endDate = "2026-12-04" },
                new { target = "milestone", action = "update", id = cycleId, name = "上線", dueDate = "2026-12-31", description = "" },
                new { target = "cycle", action = "delete", id = cycleId },
            },
            reason = "下週開始新的週期",
        });

        // Assert
        var draft = mcp.Modules.PlanningProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakePlanningProposals.ProposalId));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("不要說已經改好了"));
            Assert.That(draft.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(draft.Reason, Is.EqualTo("下週開始新的週期"));
            Assert.That(draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(draft.Changes, Has.Count.EqualTo(3));
            Assert.That(draft.Changes[0], Is.EqualTo(new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Create, null)
            {
                Name = "第 49 週", StartDate = new DateOnly(2026, 11, 30), EndDate = new DateOnly(2026, 12, 4),
            }));
            Assert.That(draft.Changes[1], Is.EqualTo(new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Update, cycleId)
            {
                Name = "上線", DueDate = new DateOnly(2026, 12, 31), Description = "",
            }));
            Assert.That(draft.Changes[2].Action, Is.EqualTo(PlanningAction.Delete));
        }
    }

    [TestCase("""{"projectKey":"WEB","changes":[]}""", Description = "空的也交給模組擋，這裡只擋形狀")]
    [TestCase("""{"projectKey":"WEB"}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"target":"week","action":"create"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"target":"cycle","action":"rename"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"target":"cycle","action":"create","startDate":"11/30"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"target":"cycle","action":"update","id":"not-a-guid"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"target":"cycle","action":"create","extra":1}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[5]}""")]
    [TestCase("""{"projectKey":"OPS","changes":[{"target":"cycle","action":"create"}]}""")]
    public async Task ProposePlanningChanges_BadShapes_AreToolErrorsBeforeTheCreator(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.PlanningProposals.OnCreate = _ => new Failure(FailureKind.Invalid, "ai.field_invalid", "模組擋下。");

        // Act
        var (isError, _) = await mcp.CallAsync("propose_planning_changes", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        Assert.That(isError, Is.True);
        if (!json.Contains("\"changes\":[]"))
        {
            Assert.That(mcp.Modules.PlanningProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposePlanningChanges_MoreThanTheLimit_IsRefusedBeforeTheCreator()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        var changes = Enumerable.Range(0, AiRules.MaxPlanningChangesPerProposal + 1)
            .Select(_ => new { target = "cycle", action = "delete", id = Guid.NewGuid() }).ToArray();

        // Act
        var (isError, body) = await mcp.CallAsync("propose_planning_changes", new { projectKey = "WEB", changes });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain(AiRules.MaxPlanningChangesPerProposal.ToString()));
            Assert.That(mcp.Modules.PlanningProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposePlanningChanges_CreatorFailure_IsAToolErrorWithTheModulesIssues()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.PlanningProposals.OnCreate = _ => new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "你沒有改週期的權限。");

        // Act
        var (isError, body) = await mcp.CallAsync("propose_planning_changes", new
        {
            projectKey = "WEB",
            changes = new[] { new { target = "cycle", action = "delete", id = Guid.NewGuid() } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Is.EqualTo("你沒有改週期的權限。"));
            Assert.That(body["code"]?.GetValue<string>(), Is.EqualTo(AiErrorCodes.Forbidden));
        }
    }

    [Test]
    public async Task ProposeCommentChanges_ReadsEachChangeIntoTheDraft()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        var commentId = Guid.NewGuid();

        // Act
        var (isError, body) = await mcp.CallAsync("propose_comment_changes", new
        {
            projectKey = "WEB",
            changes = new object[]
            {
                new { itemKey = "WEB-12", commentId, action = "edit", body = "改過的留言" },
                new { itemKey = "WEB-13", commentId = FakeCommentProposals.CommentId, action = "delete" },
                new { itemKey = "WEB-13", commentId = Guid.NewGuid(), action = "restore" },
            },
        });

        // Assert
        var create = mcp.Modules.CommentProposals.Creates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(create.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeCommentProposals.ProposalId));
            Assert.That(create.Draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(create.Draft.Changes[0], Is.EqualTo(new CommentChangeDraft("WEB-12", commentId, CommentAction.Edit, "改過的留言")));
            Assert.That(create.Draft.Changes[1].Action, Is.EqualTo(CommentAction.Delete));
            Assert.That(create.Draft.Changes[1].Body, Is.Null);
            Assert.That(create.Draft.Changes[2].Action, Is.EqualTo(CommentAction.Restore));
        }
    }

    [TestCase("""{"projectKey":"WEB","changes":[{"itemKey":"WEB-1","action":"edit","body":"x"}]}""", Description = "缺 commentId")]
    [TestCase("""{"projectKey":"WEB","changes":[{"itemKey":"WEB-1","commentId":"nope","action":"edit"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"itemKey":"WEB-1","commentId":"0190a000-0000-7000-8000-000000000001","action":"remove"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"itemKey":"OPS-1","commentId":"0190a000-0000-7000-8000-000000000001","action":"delete"}]}""", Description = "不是對話的專案")]
    [TestCase("""{"projectKey":"WEB","changes":[{"itemKey":"WEB-1","commentId":"0190a000-0000-7000-8000-000000000001","action":"delete","extra":1}]}""")]
    public async Task ProposeCommentChanges_BadShapes_AreToolErrorsBeforeTheCreator(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_comment_changes", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.CommentProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeItemRestores_PassesKeysAndReasonAndReturnsTheProposal()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_item_restores", new { projectKey = "WEB", itemKeys = new[] { "WEB-7", "WEB-8" }, reason = "刪錯了" });

        // Assert
        var draft = mcp.Modules.ItemRestoreProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeItemRestoreProposals.ProposalId));
            Assert.That(draft.ItemKeys, Is.EqualTo(new[] { "WEB-7", "WEB-8" }));
            Assert.That(draft.Reason, Is.EqualTo("刪錯了"));
            Assert.That(draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("不要說已經復原了"));
        }
    }

    [TestCase("""{"projectKey":"WEB"}""")]
    [TestCase("""{"projectKey":"WEB","itemKeys":[5]}""")]
    [TestCase("""{"projectKey":"WEB","itemKeys":["OPS-1"]}""")]
    [TestCase("""{"projectKey":"WEB","itemKeys":["WEB-1\u0000"]}""")]
    public async Task ProposeItemRestores_BadArguments_AreToolErrors(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_item_restores", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ItemRestoreProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeProjectChanges_NullOrMissingDescriptionMeansNoChange_OnlyBlankClearsIt()
    {
        // Arrange：模型常把沒用到的參數寫成 null，不能因此清掉專案說明。
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError1, body) = await mcp.CallAsync("propose_project_changes", new { projectKey = "WEB", name = "官網改版", archived = true });
        var (isError2, _) = await mcp.CallAsync("propose_project_changes", JsonSerializer.Deserialize<JsonElement>("""{"projectKey":"WEB","name":"新名稱","description":null}"""));
        var (isError3, _) = await mcp.CallAsync("propose_project_changes", new { projectKey = "WEB", description = "## 網址\n- https://example.com", reason = "補資訊" });
        var (isError4, _) = await mcp.CallAsync("propose_project_changes", new { projectKey = "WEB", description = "" });

        // Assert
        var creates = mcp.Modules.ProjectChangeProposals.Creates;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new[] { isError1, isError2, isError3, isError4 }, Is.All.False);
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeProjectChangeProposals.ProposalId));
            Assert.That(creates, Has.Count.EqualTo(4));
            Assert.That((creates[0].Draft.Name, creates[0].Draft.Archived, creates[0].Draft.Description.HasValue), Is.EqualTo(("官網改版", (bool?)true, false)));
            Assert.That(creates[1].Draft.Description.HasValue, Is.False, "null 是不改");
            Assert.That(creates[2].Draft.Description.Value, Does.StartWith("## 網址"));
            Assert.That(creates[2].Draft.Reason, Is.EqualTo("補資訊"));
            Assert.That(creates[2].Draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(creates[3].Draft.Description.HasValue, Is.True);
            Assert.That(creates[3].Draft.Description.Value, Is.Empty, "空字串是清掉，交給模組");
        }
    }

    [TestCase("""{"projectKey":"WEB","name":5}""")]
    [TestCase("""{"projectKey":"WEB","archived":"yes"}""")]
    [TestCase("""{"projectKey":"OPS","name":"x"}""")]
    [TestCase("""{"projectKey":"WEB","extra":1}""")]
    public async Task ProposeProjectChanges_BadArguments_AreToolErrors(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_project_changes", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ProjectChangeProposals.Creates, Is.Empty);
        }
    }

    [TestCase("propose_planning_changes")]
    [TestCase("propose_comment_changes")]
    [TestCase("propose_item_restores")]
    [TestCase("propose_project_changes")]
    public async Task ProposalTools_CountAsProposalsInTheBudget(string tool)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        await mcp.CallAsync(tool, new { projectKey = "WEB", changes = Array.Empty<object>(), itemKeys = Array.Empty<string>() });

        // Assert
        Assert.That(mcp.Modules.AgentBudget.LocalCalls.Single().IsProposal, Is.True);
    }

    [Test]
    public async Task ProposalTools_BudgetExhausted_AreRefusedWithoutCallingTheCreator()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.AgentBudget.Outcome = AgentToolBudgetOutcome.ProposalsExhausted;

        // Act
        var (isError, _) = await mcp.CallAsync("propose_project_changes", new { projectKey = "WEB", name = "新名稱" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ProjectChangeProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeItemChanges_PassesReplyToToTheDraft()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        var replyTo = Guid.NewGuid();

        // Act
        var (isError, body) = await mcp.CallAsync("propose_item_changes", new
        {
            projectKey = "WEB",
            itemKey = "WEB-1",
            changes = new[] { new { field = "parentKey", value = (string?)"WEB-2" }, new { field = "cycleId", value = (string?)null } },
            comment = "已處理",
            replyTo,
        });

        // Assert
        var draft = mcp.Modules.ItemProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(draft.ReplyTo, Is.EqualTo(replyTo));
            Assert.That(draft.Changes.Select(change => change.Field), Is.EqualTo(new[] { "parentKey", "cycleId" }), "新欄位交給模組檢查");
        }
    }

    [Test]
    public async Task ProposeItemChanges_ReplyToThatIsNotAGuid_IsAToolError()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_item_changes", new { projectKey = "WEB", itemKey = "WEB-1", comment = "x", replyTo = "abc" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ItemProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeBulkItemChanges_PassesEachEntrysReplyTo()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        var replyTo = Guid.NewGuid();

        // Act
        var (isError, body) = await mcp.CallAsync("propose_bulk_item_changes", new
        {
            projectKey = "WEB",
            items = new object[]
            {
                new { itemKey = "WEB-1", comment = "回覆", replyTo },
                new { itemKey = "WEB-2", comment = "新的留言" },
            },
        });

        // Assert
        var draft = mcp.Modules.ItemBatchProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(draft.Items[0].ReplyTo, Is.EqualTo(replyTo));
            Assert.That(draft.Items[1].ReplyTo, Is.Null);
        }
    }

    [Test]
    public async Task ProposeBulkItemChanges_ReplyToThatIsNotAGuid_NamesTheEntry()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_bulk_item_changes", new { projectKey = "WEB", items = new[] { new { itemKey = "WEB-1", comment = "x", replyTo = "abc" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("items[0]"));
        }
    }
}
