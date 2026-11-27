using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class CodexTranslatorTests
{
    private static TranslatorRun Play(string fixture) => TranslatorRun.Play(new CodexTranslator(), fixture);

    [Test]
    public void Success_SendsOnlyWhatTheMessageGrewBy()
    {
        // Act
        var run = Play("codex-success.jsonl");

        // Assert
        Assert.That(run.Text, Is.EqualTo("先提出變更集。\n\n已提出 #7。"), "同一則訊息每次帶全文，只能送多出來的部分；新的一則隔一段");
    }

    [Test]
    public void Success_ReportsMcpToolCallsAndTheChangeSetNumber()
    {
        // Act
        var run = Play("codex-success.jsonl");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.Tools.Select(tool => (tool.CallId, tool.Name, tool.Status)), Is.EqualTo(new[]
            {
                ("item_1", "get_blueprint", "running"),
                ("item_1", "get_blueprint", "done"),
                ("item_3", "propose_changeset", "running"),
                ("item_3", "propose_changeset", "done"),
            }), "reasoning 略過");
            Assert.That(run.Tools[3].ChangeSetNumber, Is.EqualTo(7));
            Assert.That(run.Outcome, Is.EqualTo(new JobOutcome(JobStatus.Complete, null, 200, 40)));
        }
    }

    [Test]
    public void ItemProposal_Codex_CarriesTheProposalIdOnTheFinishedTool()
    {
        // Act
        var tools = Play("codex-item-proposal.jsonl").Tools;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => (tool.Name, tool.Status)), Is.EqualTo(new[]
            {
                ("get_item", "running"), ("get_item", "done"), ("propose_item_changes", "running"), ("propose_item_changes", "done"),
            }));
            Assert.That(tools[1].ItemProposalId, Is.Null, "get_item 只是一般的進度");
            Assert.That(tools[2].ItemProposalId, Is.Null, "開始時還沒有結果");
            Assert.That(tools[3].ItemProposalId, Is.EqualTo("3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e11"));
        }
    }

    [TestCase("codex-item-proposal-missing.jsonl")]
    [TestCase("codex-item-proposal-bad.jsonl")]
    public void ItemProposal_Codex_WithoutAGuidProposalId_OmitsIt(string fixture)
    {
        // Act
        var done = Play(fixture).Tools.Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Status, Is.EqualTo("done"));
            Assert.That(done.ItemProposalId, Is.Null);
        }
    }

    [Test]
    public void NewItemsProposal_Codex_CarriesTheProposalIdOnTheFinishedTool()
    {
        // Act
        var tools = Play("codex-new-items-proposal.jsonl").Tools;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Last().Name, Is.EqualTo("propose_new_items"));
            Assert.That(tools.Last().Status, Is.EqualTo("done"));
            Assert.That(tools.Last().NewItemProposalId, Is.EqualTo("3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e11"));
            Assert.That(tools.Last().ItemProposalId, Is.Null);
            Assert.That(tools.Take(tools.Count - 1).All(tool => tool.NewItemProposalId is null), Is.True, "開始時與別的工具都不帶");
        }
    }

    [Test]
    public void MemberProposal_Codex_CarriesTheProposalIdOnTheFinishedTool()
    {
        // Act
        var tools = Play("codex-member-proposal.jsonl").Tools;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Last().Name, Is.EqualTo("propose_member_changes"));
            Assert.That(tools.Last().Status, Is.EqualTo("done"));
            Assert.That(tools.Last().MemberProposalId, Is.EqualTo("3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e11"));
            Assert.That(tools.Last().ItemProposalId, Is.Null);
            Assert.That(tools.Last().NewItemProposalId, Is.Null);
            Assert.That(tools.Take(tools.Count - 1).All(tool => tool.MemberProposalId is null), Is.True, "開始時與別的工具都不帶");
        }
    }

    [TestCase("codex-planning-proposal.jsonl", "propose_planning_changes", "3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e41")]
    [TestCase("codex-project-change-proposal.jsonl", "propose_project_changes", "3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e42")]
    [TestCase("codex-comment-proposal.jsonl", "propose_comment_changes", "3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e43")]
    [TestCase("codex-restore-proposal.jsonl", "propose_item_restores", "3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e44")]
    public void ProjectWriteProposal_Codex_CarriesTheGenericProposalIdOnlyOnTheFinishedProposalTool(string fixture, string toolName, string proposalId)
    {
        // Act
        var tools = Play(fixture).Tools;
        var done = tools.Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((done.Name, done.Status), Is.EqualTo((toolName, "done")));
            Assert.That(done.ProposalId, Is.EqualTo(proposalId));
            Assert.That(done.ItemProposalId, Is.Null);
            Assert.That(done.ItemBatchProposalId, Is.Null);
            Assert.That(tools.Take(tools.Count - 1).All(tool => tool.ProposalId is null), Is.True, "開始時與讀取工具（結果裡就算有 proposalId）都不帶");
        }
    }

    [TestCase("codex-deletion-proposal.jsonl", "propose_item_deletions", "3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e22")]
    [TestCase("codex-bulk-proposal.jsonl", "propose_bulk_item_changes", "3f2b8c1e-9a4d-4e6b-8f10-2c5d7a9b0e33")]
    public void RevisionProposal_Codex_CarriesItsOwnProposalIdOnTheFinishedTool(string fixture, string toolName, string proposalId)
    {
        // Act
        var tools = Play(fixture).Tools;
        var done = tools.Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((done.Name, done.Status), Is.EqualTo((toolName, "done")));
            Assert.That(done.ItemDeletionProposalId, Is.EqualTo(toolName == "propose_item_deletions" ? proposalId : null));
            Assert.That(done.ItemBatchProposalId, Is.EqualTo(toolName == "propose_bulk_item_changes" ? proposalId : null));
            Assert.That(done.MemberProposalId, Is.Null);
            Assert.That(done.ItemProposalId, Is.Null);
            Assert.That(done.NewItemProposalId, Is.Null);
            Assert.That(tools.Take(tools.Count - 1).All(tool => tool.ItemDeletionProposalId is null && tool.ItemBatchProposalId is null), Is.True, "開始時與別的工具都不帶");
        }
    }

    [Test]
    public void WithdrawProposal_Codex_IsReportedAsAPlainToolWithoutAProposalId()
    {
        // Act
        var done = Play("codex-withdraw.jsonl").Tools.Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((done.Name, done.Status), Is.EqualTo(("withdraw_proposal", "done")));
            Assert.That(done.ItemDeletionProposalId, Is.Null);
            Assert.That(done.ItemBatchProposalId, Is.Null);
            Assert.That(done.MemberProposalId, Is.Null);
        }
    }

    [Test]
    public void NewItemsProposal_Codex_WithoutAGuidProposalId_OmitsIt()
    {
        // Act
        var done = Play("codex-new-items-proposal-bad.jsonl").Tools.Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Status, Is.EqualTo("done"));
            Assert.That(done.NewItemProposalId, Is.Null);
        }
    }

    [Test]
    public void FailedToolCall_CarriesTheSanitizedMessage()
    {
        // Act
        var failed = Play("codex-tool-failed.jsonl").Tools.Single(tool => tool.Status == "failed");

        // Assert
        Assert.That(failed.Error, Does.Contain("找不到狀態").And.Not.Contain("/Users/someone"));
    }

    [Test]
    public void CommandExecution_AbortsTheJobBeforeAnythingElseIsForwarded()
    {
        // Act
        var run = Play("codex-command.jsonl");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.Aborted, Is.True);
            Assert.That(run.Outcome, Is.EqualTo(new JobOutcome(JobStatus.Failed, "本機的 Codex 想用 Kumi 以外的工具，已經停下。")));
            Assert.That(run.Events, Is.Empty);
        }
    }

    [TestCase("file_change")]
    [TestCase("web_search")]
    [TestCase("collab_tool_call")]
    [TestCase("something_new_in_a_later_version")]
    public void OtherForbiddenItems_AbortToo(string type)
    {
        // Arrange
        var translator = new CodexTranslator();

        // Act
        var step = translator.Feed($$$"""{"type":"item.started","item":{"id":"x","type":"{{{type}}}"}}""");

        // Assert
        Assert.That(step.Abort, Is.True);
    }

    [Test]
    public void TurnFailed_IsAFailureWithTheMessage_AndEarlierReconnectNoticesAreNotOne()
    {
        // Act
        var run = Play("codex-failed.jsonl");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.Text, Is.EqualTo("這段話在重連前"));
            Assert.That(run.Outcome, Is.EqualTo(JobOutcome.Failed("本機的 Codex 回報錯誤：stream disconnected before completion")));
        }
    }

    [Test]
    public void ErrorEventsWithoutAnythingElse_FailTheJobWithTheLastOneWhenTheProcessEnds()
    {
        // Arrange
        var translator = new CodexTranslator();
        var run = TranslatorRun.Play(translator, "codex-error-only.jsonl");

        // Act
        var outcome = translator.Conclude(1, string.Empty);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.Outcome, Is.Null, "error 不一定是結束");
            Assert.That(outcome.Error, Does.Contain("401 Unauthorized"));
        }
    }

    [Test]
    public void MessageRewrittenInsteadOfExtended_SendsTheWholeTextOnANewLine()
    {
        // Act
        var run = Play("codex-rewrite.jsonl");

        // Assert
        Assert.That(run.Text, Is.EqualTo("草稿一\n改寫後的全文"));
    }

    [Test]
    public void Conclude_WithoutEvents_UsesTheErrorLineOfStandardErrorWithoutPaths()
    {
        // Arrange
        var translator = new CodexTranslator();
        var stderr = "2026-11-23T11:30:17Z ERROR codex_models_manager: refresh failed\nError: thread/start failed: cannot read /Users/someone/.codex/auth.json\n";

        // Act
        var outcome = translator.Conclude(1, stderr);

        // Assert
        Assert.That(outcome.Error, Does.StartWith("本機的 Codex 沒辦法執行（代碼 1）：Error: thread/start failed").And.Not.Contain("/Users/someone"));
    }

    [Test]
    public void Conclude_ExitZeroWithTextButNoTurnCompleted_CountsAsComplete()
    {
        // Arrange
        var translator = new CodexTranslator();
        translator.Feed("""{"type":"item.completed","item":{"id":"a","type":"agent_message","text":"好"}}""");

        // Act
        var outcome = translator.Conclude(0, string.Empty);

        // Assert
        Assert.That(outcome.Status, Is.EqualTo(JobStatus.Complete));
    }

    [Test]
    public void Conclude_ExitZeroWithNothing_Fails()
    {
        // Act
        var outcome = new CodexTranslator().Conclude(0, string.Empty);

        // Assert
        Assert.That(outcome.Status, Is.EqualTo(JobStatus.Failed));
    }

    [Test]
    public void ToolCompletedWithoutStarted_StillReportsTheStart()
    {
        // Arrange
        var translator = new CodexTranslator();

        // Act
        var step = translator.Feed("""{"type":"item.completed","item":{"id":"z","type":"mcp_tool_call","server":"kumi","tool":"get_template","status":"completed"}}""");

        // Assert
        Assert.That(step.Events.Cast<ToolEvent>().Select(tool => tool.Status), Is.EqualTo(new[] { "running", "done" }));
    }

    [Test]
    public void McpToolCallToAnotherServer_Aborts()
    {
        // Act
        var step = new CodexTranslator().Feed("""{"type":"item.started","item":{"id":"x","type":"mcp_tool_call","server":"other","tool":"get_blueprint"}}""");

        // Assert
        Assert.That(step.Abort, Is.True);
    }

    [TestCase("todo_list")]
    [TestCase("reasoning")]
    [TestCase("error")]
    public void WhitelistedItems_DoNotAbort(string type)
    {
        // Act
        var step = new CodexTranslator().Feed($$$"""{"type":"item.completed","item":{"id":"x","type":"{{{type}}}","text":""}}""");

        // Assert
        Assert.That(step.Abort, Is.False);
    }

    [Test]
    public void ItemWithoutAType_Aborts()
    {
        // Assert
        Assert.That(new CodexTranslator().Feed("""{"type":"item.started","item":{"id":"x"}}""").Abort, Is.True);
    }
}
