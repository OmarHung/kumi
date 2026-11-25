using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>網頁的回覆交給本機的引擎：進度轉成事件、存下來的訊息、逾時、停止、失敗。</summary>
[TestFixture]
public sealed class LocalAgentReplyTests
{
    private LocalAgentHarness _h = null!;
    private AiConnectionOption _engine = null!;

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private async Task StartAsync(LocalAgentTimings? timings = null, AssistantTimings? assistantTimings = null)
    {
        _h = await LocalAgentHarness.StartAsync(timings ?? LocalAgentHarness.PatientTimings, assistantTimings: assistantTimings);
        _engine = await _h.RegisterAsync(name: "辦公室的 Mac");
    }

    [Test]
    public async Task Reply_TextAndToolProgress_BecomesEventsAndTheStoredMessage()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine, "把待辦改名", "sonnet", AiEffort.High);
        var job = await _h.ClaimAsync();

        // Act
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("先讀"), LocalAgentHarness.Text("設定。"));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("toolu_1", "get_blueprint", AssistantToolStatus.Running));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("toolu_1", "get_blueprint", AssistantToolStatus.Done));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("toolu_2", "propose_changeset", AssistantToolStatus.Running));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("toolu_2", "propose_changeset", AssistantToolStatus.Done, 12));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("變更集 #12 等你核准"));
        await _h.FinishAsync(job.JobId, usage: new AssistantUsage(1200, 80));
        var events = await run.ReadAllAsync();
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        var stored = (await _h.Assistant.MessagesAsync(run.ConversationId)).Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                events.Select(e => e switch
                {
                    AssistantTextDelta text => $"text:{text.Delta}",
                    AssistantToolUpdate tool => $"tool:{tool.Tool.CallId}:{tool.Tool.Name}:{tool.Tool.Status}:{tool.Tool.ChangeSetNumber}",
                    _ => e.GetType().Name,
                }),
                Is.EqualTo(new[]
                {
                    "text:先讀", "text:設定。",
                    "tool:c1:get_blueprint:Running:", "tool:c1:get_blueprint:Done:",
                    "tool:c2:propose_changeset:Running:", "tool:c2:propose_changeset:Done:12",
                    "text:變更集 #12 等你核准",
                    nameof(AssistantRunCompleted),
                }));
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(final.Error, Is.Null);
            Assert.That(
                final.Parts,
                Is.EqualTo(new AssistantPart[]
                {
                    new AssistantTextPart("先讀設定。"),
                    new AssistantToolPart("c1", "get_blueprint", AssistantToolStatus.Done, null, null),
                    new AssistantToolPart("c2", "propose_changeset", AssistantToolStatus.Done, 12, null),
                    new AssistantTextPart("變更集 #12 等你核准"),
                }));
            Assert.That(stored.Parts, Is.EqualTo(final.Parts));
            Assert.That(stored.Selection, Is.EqualTo(new AiSelection(_engine.Id, "sonnet", AiEffort.High)));
            Assert.That(stored.ConnectionName, Is.EqualTo("Claude Code（辦公室的 Mac）"));
            Assert.That(stored.Usage, Is.EqualTo(new AssistantUsage(1200, 80)));
            Assert.That(_h.Assistant.Gateway.ChatEndpoints, Is.Empty, "本機的回覆不經過任何供應商的 API。");
            Assert.That(await _h.EventCountAsync(job.JobId), Is.Zero, "回覆存完，進度就刪掉。");
            Assert.That((await _h.Assistant.ConversationRowAsync(run.ConversationId)).RunningUntil, Is.Null);
        }
    }

    [Test]
    public async Task Reply_TranscriptKeepsOnlyTheAssistantsText_SoALaterApiModelSeesAPlainAssistantMessage()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine, "第一句");
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("你"), LocalAgentHarness.Tool("t1", "get_blueprint", AssistantToolStatus.Done), LocalAgentHarness.Text("好"));
        await _h.FinishAsync(job.JobId);
        var first = await run.ReadAllAsync();
        _h.Assistant.Script.Reply("收到");

        // Act
        var second = await _h.Assistant.SendAsync(run.ConversationId, "第二句");
        var seen = _h.Assistant.Script.Calls.Single().Messages;
        var row = await _h.Assistant.RowAsync(first.OfType<AssistantRunCompleted>().Single().Message.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(row.Transcript, Does.Contain("你\\n好").And.Not.Contain("call"));
            Assert.That(
                seen.Where(m => m.Role != ChatRole.System).Select(m => (m.Role, m.Text)),
                Is.EqualTo(new[] { (ChatRole.User, "第一句"), (ChatRole.Assistant, "你\n好"), (ChatRole.User, "第二句") }));
        }
    }

    [Test]
    public async Task Reply_OnlyToolsAndNoText_StoresAShortNoteInsteadOfAnEmptyTranscript()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("t1", "describe_usage", AssistantToolStatus.Done));
        await _h.FinishAsync(job.JobId);

        // Act
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;
        var row = await _h.Assistant.RowAsync(final.Id);

        // Assert
        Assert.That(row.Transcript, Does.Contain("用了 describe_usage，沒有文字回覆"));
    }

    [Test]
    public async Task Reply_SecondMessage_SendsTheWholeConversationAgain()
    {
        // Arrange
        await StartAsync();
        var first = await _h.SendAsync(_engine, "第一句");
        var firstJob = await _h.ClaimAsync();
        await _h.ReportAsync(firstJob.JobId, LocalAgentHarness.Text("答一"), LocalAgentHarness.Tool("t1", "get_blueprint", AssistantToolStatus.Done));
        await _h.FinishAsync(firstJob.JobId);
        await first.ReadAllAsync();

        // Act
        await _h.SendAsync(_engine, "第二句", conversationId: first.ConversationId);
        var secondJob = await _h.ClaimAsync();

        // Assert
        Assert.That(secondJob.Prompt, Does.Contain("<<<使用者>>>\n第一句\n<<<結束>>>").And.Contain("<<<助理>>>\n答一\n<<<結束>>>").And.EndWith("<<<使用者>>>\n第二句\n<<<結束>>>"));
    }

    [Test]
    public async Task Reply_NobodyClaimsTheJob_FailsWithTheNoResponseMessage()
    {
        // Arrange
        await StartAsync(LocalAgentHarness.FastTimings);
        var run = await _h.SendAsync(_engine);

        // Act
        var events = await run.ReadAllAsync();
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        var late = await _h.CheckInAsync(capacity: 1, cancellationToken: new CancellationTokenSource(300).Token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(final.Error, Is.EqualTo("你電腦上的 Kumi 代理沒有回應，確認 kumi agent start 還開著。"));
            Assert.That(late.Value, Is.Null, "放棄之後才來的，不能再領走。");
            Assert.That((await _h.Assistant.ConversationRowAsync(run.ConversationId)).RunningUntil, Is.Null);
        }
    }

    [Test]
    public async Task Reply_ClaimedButNothingIsReported_FailsAndTheAgentIsToldToStop()
    {
        // Arrange
        await StartAsync(LocalAgentHarness.FastTimings);
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();

        // Act
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;
        var late = await _h.Agents.ReportAsync(_h.Me, job.JobId, [LocalAgentHarness.Text("太遲了")], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(final.Error, Does.Contain("中斷了回報"));
            Assert.That(late.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentJobNotFound), "存完之後工作列就刪了，本機代理收到 404 就停下");
            Assert.That(await _h.EventCountAsync(job.JobId), Is.Zero);
        }
    }

    [Test]
    public async Task Reply_AgentKeepsHeartbeatingWithoutProgress_IsNotTimedOut()
    {
        // Arrange
        await StartAsync(LocalAgentHarness.FastTimings);
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();

        // Act
        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(300);
            _h.Assistant.Clock.Advance(TimeSpan.FromSeconds(10));
            await _h.ReportAsync(job.JobId);
        }

        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("想好了"));
        await _h.FinishAsync(job.JobId);
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("想好了") }));
        }
    }

    [Test]
    public async Task Reply_AgentFinishesFailed_KeepsWhatCameInAndFailsRunningTools()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("讀到一半"), LocalAgentHarness.Tool("t1", "get_blueprint", AssistantToolStatus.Running));

        // Act
        await _h.FinishAsync(job.JobId, AssistantMessageStatus.Failed, "這台電腦的 Claude Code 沒有登入，請在終端機執行 claude 登入。");
        var events = await run.ReadAllAsync();
        var final = events.OfType<AssistantRunCompleted>().Single().Message;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(final.Error, Is.EqualTo("這台電腦的 Claude Code 沒有登入，請在終端機執行 claude 登入。"));
            Assert.That(
                final.Parts,
                Is.EqualTo(new AssistantPart[]
                {
                    new AssistantTextPart("讀到一半"),
                    new AssistantToolPart("c1", "get_blueprint", AssistantToolStatus.Failed, null, "回覆中斷了。"),
                }));
            Assert.That(events.OfType<AssistantToolUpdate>().Last().Tool.Status, Is.EqualTo(AssistantToolStatus.Failed));
        }
    }

    [Test]
    public async Task Reply_AgentFailsWithoutSayingWhy_GetsAGenericReason()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();

        // Act
        await _h.FinishAsync(job.JobId, AssistantMessageStatus.Failed);
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;

        // Assert
        Assert.That(final.Error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task Reply_AgentFinishesStopped_IsStoredAsStoppedWithItsReason()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("寫了一半"));

        // Act
        await _h.FinishAsync(job.JobId, AssistantMessageStatus.Stopped, "kumi agent 被關掉了。");
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
            Assert.That(final.Error, Is.EqualTo("kumi agent 被關掉了。"));
        }
    }

    [Test]
    public async Task Reply_UserStops_MarksTheJobCancelledAndWaitsForTheAgentToFinish()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("寫到一半"));
        var agent = Task.Run(async () =>
        {
            // 本機代理：每次回報都看有沒有被要求取消，有就結束引擎、送結束。
            while (!(await _h.ReportAsync(job.JobId)).Cancelled)
            {
                await Task.Delay(30);
            }

            await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("（最後一句）"));
            await _h.FinishAsync(job.JobId, AssistantMessageStatus.Stopped, usage: new AssistantUsage(50, 5));
        });

        // Act
        var events = await run.ReadUntilStopAsync(e => e is AssistantTextDelta);
        await agent;
        var final = events.OfType<AssistantRunCompleted>().Single().Message;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
            Assert.That(final.Error, Is.Null);
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("寫到一半（最後一句）") }));
            Assert.That(final.Usage, Is.EqualTo(new AssistantUsage(50, 5)));
            Assert.That((await _h.Assistant.ConversationRowAsync(run.ConversationId)).RunningUntil, Is.Null);
        }
    }

    [Test]
    public async Task Reply_UserStopsAndTheAgentNeverAnswers_GivesUpAfterTheGraceAndEndsTheJob()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("寫到一半"));

        // Act
        var events = await run.ReadUntilStopAsync(e => e is AssistantTextDelta);
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        var late = await _h.Agents.ReportAsync(_h.Me, job.JobId, [LocalAgentHarness.Text("太遲了")], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("寫到一半") }));
            Assert.That(late.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentJobNotFound));
        }
    }

    [Test]
    public async Task Reply_UserStopsBeforeAnyoneClaims_EndsTheJobAtOnceAndItCanNeverBeClaimed()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);

        // Act
        await run.LeaveAsync();
        var late = await _h.CheckInAsync(capacity: 1, cancellationToken: new CancellationTokenSource(300).Token);
        var stored = (await _h.Assistant.MessagesAsync(run.ConversationId)).Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(late.Value, Is.Null);
            Assert.That(stored.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
        }
    }

    [Test]
    public async Task Reply_TakesLongerThanTheReplyTimeout_IsStoppedWithTheTimeoutReasonAndTheAgentIsToldToStop()
    {
        // Arrange
        await StartAsync(assistantTimings: new AssistantTimings(TimeSpan.FromMilliseconds(1500), AiRules.RunLease));
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("慢慢想"));

        // Act
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;
        var late = await _h.Agents.ReportAsync(_h.Me, job.JobId, [], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
            Assert.That(final.Error, Is.EqualTo("超過 5 分鐘，回覆中斷了。"));
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("慢慢想") }));
            Assert.That(late.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentJobNotFound));
        }
    }

    [Test]
    public async Task Reply_AgentSendsFarTooMuchText_IsCutOffAndFailedWithTheTruncatedReason()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        var chunk = LocalAgentHarness.Text(new string('字', AiRules.MaxLocalAgentTextDelta));
        var batch = Enumerable.Repeat<LocalAgentEvent>(chunk, LocalReplyAccumulator.MaxReplyCharacters / AiRules.MaxLocalAgentTextDelta + 2).ToArray();

        // Act
        await _h.ReportAsync(job.JobId, batch);
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;
        var late = await _h.Agents.ReportAsync(_h.Me, job.JobId, [], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(final.Error, Is.EqualTo(AssistantReplyRunner.TruncatedMessage));
            Assert.That(((AssistantTextPart)final.Parts[0]).Text, Has.Length.EqualTo(LocalReplyAccumulator.MaxReplyCharacters));
            Assert.That(late.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentJobNotFound));
        }
    }

    [Test]
    public async Task Reply_TwoLocalRepliesAtOnce_CountTowardTheSameLimitOfTwo()
    {
        // Arrange
        await StartAsync();
        await _h.SendAsync(_engine);
        await _h.SendAsync(_engine);
        var third = await _h.Assistant.NewConversationAsync();

        // Act
        var started = await _h.Assistant.Service.StartAsync(
            _h.Me, AssistantHarness.ProjectKey, third.Id, new AssistantMessageInput("第三則", LocalAgentHarness.Select(_engine)), CancellationToken.None);

        // Assert
        Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.TooManyRuns));
    }

    [Test]
    public async Task Reply_EachJobGoesToTheEngineOfItsOwnSelection()
    {
        // Arrange
        await StartAsync();
        await _h.CheckInAsync(name: "辦公室的 Mac", engines: [LocalAgentHarness.ClaudeCode(), LocalAgentHarness.Codex()]);
        var options = await _h.Host.Get<IAiOptionsService>().GetAsync(_h.Me, CancellationToken.None);
        var codex = options.Connections.Single(c => c.Kind == AiProviderKind.Codex);

        // Act
        await _h.SendAsync(codex, "給 codex 的");
        var job = await _h.ClaimAsync(engines: [LocalAgentHarness.ClaudeCode(), LocalAgentHarness.Codex()]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(codex.Kind, Is.EqualTo(AiProviderKind.Codex));
            Assert.That(job.Engine, Is.EqualTo(AiProviderKind.Codex));
        }
    }

    [Test]
    public async Task Reply_OverTheTextLimitInTheSameRoundAsComplete_FailsAsTruncatedInsteadOfComplete()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        var chunk = LocalAgentHarness.Text(new string('字', AiRules.MaxLocalAgentTextDelta));
        var batch = Enumerable.Repeat<LocalAgentEvent>(chunk, LocalReplyAccumulator.MaxReplyCharacters / AiRules.MaxLocalAgentTextDelta + 2).ToArray();
        await _h.ReportAsync(job.JobId, batch);
        await _h.FinishAsync(job.JobId);

        // Act
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(final.Error, Is.EqualTo(AssistantReplyRunner.TruncatedMessage));
        }
    }

    [Test]
    public async Task Reply_StoppedWhileOverTheTextLimit_IsFailedNotStopped()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        var chunk = LocalAgentHarness.Text(new string('字', AiRules.MaxLocalAgentTextDelta));
        await _h.ReportAsync(job.JobId, Enumerable.Repeat<LocalAgentEvent>(chunk, LocalReplyAccumulator.MaxReplyCharacters / AiRules.MaxLocalAgentTextDelta + 2).ToArray());

        // Act
        var events = await run.ReadUntilStopAsync(_ => true);
        var final = events.OfType<AssistantRunCompleted>().Single().Message;

        // Assert
        Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
    }

    [Test]
    public async Task Reply_TooManyDistinctToolCalls_FailsAndTheOnesOverTheLimitAreNotStored()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        var tools = Enumerable.Range(0, AiRules.MaxToolCallsPerReply * 2 + 3)
            .Select(i => (LocalAgentEvent)LocalAgentHarness.Tool($"t{i}", "get_blueprint", AssistantToolStatus.Done))
            .ToArray();
        await _h.ReportAsync(job.JobId, tools);

        // Act
        var final = (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(final.Error, Is.EqualTo(LocalReplyAccumulator.ToolLimitMessage));
            Assert.That(final.Parts, Has.Count.EqualTo(AiRules.MaxToolCallsPerReply * 2));
        }
    }

    [Test]
    public async Task Reply_Saved_DeletesTheFinishedJobRowAndItsEventsAtOnce()
    {
        // Arrange
        await StartAsync();
        var run = await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("好"));
        await _h.FinishAsync(job.JobId);

        // Act
        await run.ReadAllAsync();
        await using var db = await _h.Host.CreateDbContextAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.LocalJobs), Is.Zero);
            Assert.That(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.LocalEvents), Is.Zero);
        }
    }
}
