using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class ClaudeCodeTranslatorTests
{
    private const string ValidInit = """{"type":"system","subtype":"init","tools":[],"mcp_servers":[{"name":"kumi","status":"connected"}],"skills":[],"slash_commands":[]}""";

    private static TranslatorRun Play(string fixture) => TranslatorRun.Play(new ClaudeCodeTranslator(), fixture);

    [Test]
    public void Success_TurnsDeltasIntoTextAndKeepsTurnsApart()
    {
        // Act
        var run = Play("claude-success.jsonl");

        // Assert
        Assert.That(run.Text, Is.EqualTo("我先看一下藍圖。\n\n提出變更集。\n\n已提出變更集 #12。"));
    }

    [Test]
    public void Success_ReportsToolsStartedOnceAndFinishedWithTheChangeSetNumber()
    {
        // Act
        var tools = Play("claude-success.jsonl").Tools;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => (tool.CallId, tool.Name, tool.Status)), Is.EqualTo(new[]
            {
                ("toolu_1", "get_blueprint", "running"),
                ("toolu_1", "get_blueprint", "done"),
                ("toolu_2", "propose_changeset", "running"),
                ("toolu_2", "propose_changeset", "done"),
            }), "同一個工具的重複事件不能算兩次");
            Assert.That(tools[1].ChangeSetNumber, Is.Null);
            Assert.That(tools[3].ChangeSetNumber, Is.EqualTo(12));
        }
    }

    [Test]
    public void ItemProposal_Claude_CarriesTheProposalIdOnTheFinishedTool()
    {
        // Act
        var tools = Play("claude-item-proposal.jsonl").Tools;

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

    [TestCase("claude-item-proposal-missing.jsonl")]
    [TestCase("claude-item-proposal-bad.jsonl")]
    public void ItemProposal_Claude_WithoutAGuidProposalId_OmitsIt(string fixture)
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
    public void NewItemsProposal_Claude_CarriesTheProposalIdOnTheFinishedTool()
    {
        // Act
        var tools = Play("claude-new-items-proposal.jsonl").Tools;

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
    public void MemberProposal_Claude_CarriesTheProposalIdOnTheFinishedTool()
    {
        // Act
        var tools = Play("claude-member-proposal.jsonl").Tools;

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

    [Test]
    public void NewItemsProposal_Claude_WithoutAGuidProposalId_OmitsIt()
    {
        // Act
        var done = Play("claude-new-items-proposal-bad.jsonl").Tools.Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Status, Is.EqualTo("done"));
            Assert.That(done.NewItemProposalId, Is.Null);
        }
    }

    [Test]
    public void Success_CountsCachedTokensAsInput()
    {
        // Act
        var outcome = Play("claude-success.jsonl").Outcome;

        // Assert
        Assert.That(outcome, Is.EqualTo(new JobOutcome(JobStatus.Complete, null, 160, 55)));
    }

    [Test]
    public void ToolError_BecomesFailedToolWithoutTokensOrPaths()
    {
        // Act
        var run = Play("claude-tool-error.jsonl");

        // Assert
        var failed = run.Tools.Single(tool => tool.Status == "failed");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Name, Is.EqualTo("preview_changeset"));
            Assert.That(failed.Error, Does.Contain("狀態 review 不存在"));
            Assert.That(failed.Error, Does.Not.Contain("/Users/someone").And.Not.Contain("pmt_abc123"));
            Assert.That(run.Outcome?.Status, Is.EqualTo(JobStatus.Complete), "工具失敗不等於回覆失敗，模型會自己處理");
        }
    }

    [Test]
    public void NotLoggedIn_FailsWithHowToLogIn()
    {
        // Act
        var outcome = Play("claude-not-logged-in.jsonl").Outcome;

        // Assert
        Assert.That(outcome, Is.EqualTo(new JobOutcome(JobStatus.Failed, ClaudeCodeTranslator.NotLoggedInMessage, 0, 0)));
    }

    [Test]
    public void MaxTurns_FailsWithAReasonEvenWhenTheEngineGivesNone()
    {
        // Act
        var outcome = Play("claude-max-turns.jsonl").Outcome;

        // Assert
        Assert.That(outcome, Is.EqualTo(new JobOutcome(JobStatus.Failed, "引擎來回的次數超過上限，已經停下。", 10, 4)));
    }

    [Test]
    public void Init_WithBuiltInTools_AbortsImmediately()
    {
        // Act
        var run = Play("claude-builtin-tools.jsonl");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.Aborted, Is.True);
            Assert.That(run.Outcome?.Error, Does.Contain("沒有照預期關掉內建工具"));
            Assert.That(run.Events, Is.Empty, "停下之後不能再把引擎的話轉出去");
        }
    }

    [Test]
    public void Init_WhenMcpIsNotConnected_AbortsImmediately()
    {
        // Act
        var run = Play("claude-mcp-down.jsonl");

        // Assert
        Assert.That(run.Aborted, Is.True);
        Assert.That(run.Outcome?.Error, Does.Contain("連不上 Kumi 的 MCP"));
    }

    [Test]
    public void Result_WithoutStreamingDeltas_UsesTheFinalTextSoNothingIsLost()
    {
        // Arrange
        var translator = new ClaudeCodeTranslator();
        translator.Feed(ValidInit);

        // Act
        var step = translator.Feed("""{"type":"result","subtype":"success","is_error":false,"result":"全文","usage":{"input_tokens":1,"output_tokens":2}}""");

        // Assert
        Assert.That(step.Events, Is.EqualTo(new AgentEvent[] { new TextEvent("全文") }));
    }

    [Test]
    public void Feed_IgnoresNoiseAndEverythingAfterTheResult()
    {
        // Arrange
        var translator = new ClaudeCodeTranslator();
        translator.Feed(ValidInit);
        translator.Feed("""{"type":"result","subtype":"success","is_error":false,"result":""}""");

        // Act
        var noise = translator.Feed("不是 JSON 的雜訊");
        var late = translator.Feed("""{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"太晚"}}}""");

        // Assert
        Assert.That(noise.Events.Concat(late.Events), Is.Empty);
    }

    [Test]
    public void Conclude_WithoutAResultEvent_ExplainsFromTheExitCodeAndStandardError()
    {
        // Arrange
        var translator = new ClaudeCodeTranslator();

        // Act
        var crashed = translator.Conclude(1, "boom\nsomething failed at /Users/someone/.claude/x.json\n");
        var silent = new ClaudeCodeTranslator().Conclude(0, string.Empty);
        var loggedOut = new ClaudeCodeTranslator().Conclude(1, "Not logged in");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(crashed.Error, Does.StartWith("本機的 Claude Code 結束了（代碼 1）：").And.Not.Contain("/Users/someone"));
            Assert.That(silent.Error, Does.Contain("沒有回報結果"));
            Assert.That(loggedOut.Error, Is.EqualTo(ClaudeCodeTranslator.NotLoggedInMessage));
        }
    }

    [TestCase("""{"type":"system","subtype":"init","mcp_servers":[{"name":"kumi","status":"connected"}]}""")]
    [TestCase("""{"type":"system","subtype":"init","tools":"none","mcp_servers":[{"name":"kumi","status":"connected"}]}""")]
    [TestCase("""{"type":"system","subtype":"init","tools":[]}""")]
    [TestCase("""{"type":"system","subtype":"init","tools":[],"mcp_servers":{"kumi":"connected"}}""")]
    [TestCase("""{"type":"system","subtype":"init","tools":[],"mcp_servers":[{"name":"kumi","status":"connected"}],"skills":["pdf"]}""")]
    [TestCase("""{"type":"system","subtype":"init","tools":[],"mcp_servers":[{"name":"kumi","status":"connected"}],"slash_commands":["init"]}""")]
    [TestCase("""{"type":"system","subtype":"init","tools":[],"mcp_servers":[{"name":"kumi","status":"connected"}],"skills":"x"}""")]
    public void Init_ThatIsMissingFieldsOrHasTheWrongShape_IsNotAccepted(string init)
    {
        // Act
        var step = new ClaudeCodeTranslator().Feed(init);

        // Assert
        Assert.That(step.Abort, Is.True);
    }

    [TestCase("""{"type":"assistant","message":{"content":[]}}""")]
    [TestCase("""{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"x"}}}""")]
    [TestCase("""{"type":"result","subtype":"success","is_error":false,"result":"x"}""")]
    public void EventsBeforeAnAcceptedInit_StopTheJob(string line)
    {
        // Act
        var step = new ClaudeCodeTranslator().Feed(line);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(step.Abort, Is.True);
            Assert.That(step.Outcome?.Error, Does.Contain("沒有先回報工具清單"));
            Assert.That(step.Events, Is.Empty);
        }
    }

    [Test]
    public void Init_WithEmptySkillsAndCommandsAndExtraPlugins_IsAccepted()
    {
        // Act
        var step = new ClaudeCodeTranslator().Feed("""{"type":"system","subtype":"init","tools":["mcp__kumi__get_blueprint"],"mcp_servers":[{"name":"kumi","status":"connected"}],"skills":[],"slash_commands":[],"plugins":[{"name":"builtin"}],"agents":["general"]}""");

        // Assert
        Assert.That(step.Abort, Is.False);
    }
}
