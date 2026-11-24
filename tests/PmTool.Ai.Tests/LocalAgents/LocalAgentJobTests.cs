using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>領工作、回報進度、結束：同一件只被領一次、只有自己的那台電腦領得到、回報與結束的檢查。</summary>
[TestFixture]
public sealed class LocalAgentJobTests
{
    private LocalAgentHarness _h = null!;
    private AiConnectionOption _engine = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        _engine = await _h.RegisterAsync();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Claim_WaitingJob_ReturnsTheJobWithEverythingTheMachineNeeds()
    {
        // Arrange
        await _h.SendAsync(_engine, "把待辦改名", "sonnet", AiEffort.High);

        // Act
        var job = await _h.ClaimAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Engine, Is.EqualTo(AiProviderKind.ClaudeCode));
            Assert.That(job.Model, Is.EqualTo("sonnet"));
            Assert.That(job.Effort, Is.EqualTo(AiEffort.High));
            Assert.That(job.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(job.McpUrl, Is.EqualTo(LocalAgentHarness.McpUrl));
            Assert.That(job.MaxTurns, Is.EqualTo(AiRules.MaxToolCallsPerReply + 1));
            Assert.That(job.Prompt, Does.EndWith("使用者：把待辦改名"));
            Assert.That(
                job.AllowedTools,
                Is.EquivalentTo(new[]
                {
                    "get_blueprint", "list_templates", "get_template", "describe_usage", "preview_changeset", "propose_changeset", "get_changeset",
                    "get_item", "search_items", "propose_item_changes",
                }));
            Assert.That(job.SystemPrompt, Does.Contain("projectKey"));
            Assert.That(job.SystemPrompt, Does.Contain("WEB"));
            Assert.That((await _h.JobRowAsync(job.JobId)).State, Is.EqualTo("claimed"));
        }
    }

    [Test]
    public async Task Claim_TwoPollsAtTheSameTime_OnlyOneGetsTheJob()
    {
        // Arrange
        await _h.SendAsync(_engine);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var polls = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => _h.CheckInAsync(capacity: 1, cancellationToken: timeout.Token)));

        // Assert
        Assert.That(polls.Count(p => p.Value is not null), Is.EqualTo(1));
    }

    [Test]
    public async Task Claim_AlreadyClaimed_IsNotHandedOutAgain()
    {
        // Arrange
        await _h.SendAsync(_engine);
        await _h.ClaimAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act
        var again = await _h.CheckInAsync(capacity: 1, cancellationToken: timeout.Token);

        // Assert
        Assert.That(again.Value, Is.Null);
    }

    [Test]
    public async Task Claim_AnotherPersonsMachine_CannotClaimMyJob()
    {
        // Arrange
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);
        await _h.RegisterAsync(other, "machine-0001", "別人的電腦");
        var run = await _h.SendAsync(_engine);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        // Act
        var stolen = await _h.CheckInAsync(other, "machine-0001", "別人的電腦", capacity: 1, cancellationToken: timeout.Token);
        var mine = await _h.ClaimAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stolen.Value, Is.Null);
            Assert.That(mine.JobId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(run.ConversationId, Is.Not.EqualTo(Guid.Empty));
        }
    }

    [Test]
    public async Task Claim_AnotherMachineOfMine_CannotClaimAJobForThisMachinesEngine()
    {
        // Arrange
        await _h.RegisterAsync(machineKey: "machine-0002", name: "第二台");
        await _h.SendAsync(_engine);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        // Act
        var second = await _h.CheckInAsync(machineKey: "machine-0002", name: "第二台", capacity: 1, cancellationToken: timeout.Token);

        // Assert
        Assert.That(second.Value, Is.Null);
    }

    [Test]
    public async Task Claim_CapacityZero_RegistersWithoutClaiming()
    {
        // Arrange
        await _h.SendAsync(_engine);

        // Act
        var polled = await _h.CheckInAsync(capacity: 0);

        // Assert
        Assert.That(polled.Value, Is.Null);
    }

    [Test]
    public async Task Claim_NothingWaitingAndTheCallerDisconnects_ReturnsAtOnceWithNothing()
    {
        // Arrange
        using var cancel = new CancellationTokenSource();
        var started = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var polling = _h.CheckInAsync(capacity: 1, cancellationToken: cancel.Token);
        await Task.Delay(150);
        await cancel.CancelAsync();
        var result = await polling;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value, Is.Null);
            Assert.That(started.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        }
    }

    [Test]
    public async Task Claim_JobCreatedWhileWaiting_IsPickedUp()
    {
        // Arrange
        var polling = _h.ClaimAsync();
        await Task.Delay(200);

        // Act
        await _h.SendAsync(_engine);

        // Assert
        Assert.That((await polling).Engine, Is.EqualTo(AiProviderKind.ClaudeCode));
    }

    [Test]
    public async Task Report_EventsInOrder_AreStoredWithConsecutiveNumbers()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var first = await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("你"), LocalAgentHarness.Text("好"));
        var second = await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("t1", "get_blueprint", AssistantToolStatus.Running));
        await using var db = await _h.Host.CreateDbContextAsync();
        var rows = await db.LocalEvents.Where(e => e.JobId == job.JobId).OrderBy(e => e.Seq).ToListAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Cancelled, Is.False);
            Assert.That(second.Cancelled, Is.False);
            Assert.That(rows.Select(r => r.Seq), Is.EqualTo(new[] { 1, 2, 3 }));
            // jsonb 會把空白與鍵的順序重排，只看內容。
            Assert.That(rows[0].Content, Does.Contain("text").And.Contain("你"));
            Assert.That(rows[2].Content, Does.Contain("tool").And.Contain("get_blueprint"));
        }
    }

    [Test]
    public async Task Report_CancelRequested_AcksCancelled()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            await db.LocalJobs.Where(j => j.Id == job.JobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.CancelRequested, true));
        }

        // Act
        var ack = await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("還在寫"));

        // Assert
        Assert.That(ack.Cancelled, Is.True);
    }

    [Test]
    public async Task Report_NoEvents_IsAHeartbeatThatRefreshesTheLastReport()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        _h.Assistant.Clock.Advance(TimeSpan.FromSeconds(30));

        // Act
        await _h.ReportAsync(job.JobId);
        var row = await _h.JobRowAsync(job.JobId);

        // Assert
        Assert.That(row.LastReportAt, Is.EqualTo(_h.Assistant.Clock.GetUtcNow()));
    }

    [Test]
    public async Task Report_JobOfAnotherPerson_ReturnsNotFound()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);

        // Act
        var result = await _h.Agents.ReportAsync(other, job.JobId, [LocalAgentHarness.Text("x")], CancellationToken.None);

        // Assert
        AssertNotFound(result.Failure);
        Assert.That(await _h.EventCountAsync(job.JobId), Is.Zero);
    }

    [Test]
    public async Task Report_UnknownJobOrOneNotClaimedYet_ReturnsNotFound()
    {
        // Arrange
        await _h.SendAsync(_engine);
        Guid waiting;
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            waiting = (await db.LocalJobs.SingleAsync()).Id;
        }

        // Act
        var unknown = await _h.Agents.ReportAsync(_h.Me, Guid.CreateVersion7(), [], CancellationToken.None);
        var unclaimed = await _h.Agents.ReportAsync(_h.Me, waiting, [LocalAgentHarness.Text("x")], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertNotFound(unknown.Failure);
            AssertNotFound(unclaimed.Failure);
        }
    }

    [Test]
    public async Task Report_AfterTheAgentFinished_ReturnsNotFound()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        await _h.FinishAsync(job.JobId);

        // Act
        var result = await _h.Agents.ReportAsync(_h.Me, job.JobId, [LocalAgentHarness.Text("遲到")], CancellationToken.None);

        // Assert
        AssertNotFound(result.Failure);
    }

    [Test]
    public async Task Report_AfterTheServerEndedTheJob_AcksCancelledWithoutStoringAnything()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            await db.LocalJobs.Where(j => j.Id == job.JobId).ExecuteUpdateAsync(
                s => s.SetProperty(j => j.State, "finished").SetProperty(j => j.CancelRequested, true));
        }

        // Act
        var ack = await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("遲到"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ack.Cancelled, Is.True);
            Assert.That(await _h.EventCountAsync(job.JobId), Is.Zero);
        }
    }

    [Test]
    public async Task Report_Text_RemovesNulAndTurnsOtherControlCharactersIntoSpacesButKeepsNewlines()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("a\0b\u0007c\nd\te"));
        await using var db = await _h.Host.CreateDbContextAsync();
        var content = (await db.LocalEvents.SingleAsync(e => e.JobId == job.JobId)).Content;

        // Assert
        Assert.That(content, Does.Contain("ab c\\nd\\te"));
    }

    [Test]
    public async Task Report_TextTooLong_Returns422AndStoresNothingOfTheBatch()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var result = await _h.Agents.ReportAsync(
            _h.Me,
            job.JobId,
            [LocalAgentHarness.Text("好的"), LocalAgentHarness.Text(new string('x', AiRules.MaxLocalAgentTextDelta + 1))],
            CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertAgentInvalid(result.Failure);
            Assert.That(await _h.EventCountAsync(job.JobId), Is.Zero);
        }
    }

    [Test]
    public async Task Report_TooManyEventsInOneBatch_Returns422()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        var events = Enumerable.Repeat<LocalAgentEvent>(LocalAgentHarness.Text("x"), AiRules.MaxLocalAgentEventsPerReport + 1).ToList();

        // Act
        var result = await _h.Agents.ReportAsync(_h.Me, job.JobId, events, CancellationToken.None);

        // Assert
        AssertAgentInvalid(result.Failure);
    }

    [TestCase("", TestName = "工具編號是空的")]
    [TestCase("bad\0id", TestName = "工具編號有 NUL")]
    public async Task Report_BadCallId_Returns422(string callId)
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var result = await _h.Agents.ReportAsync(
            _h.Me, job.JobId, [LocalAgentHarness.Tool(callId, "get_blueprint", AssistantToolStatus.Running)], CancellationToken.None);

        // Assert
        AssertAgentInvalid(result.Failure);
    }

    [TestCase("", TestName = "工具名稱是空的")]
    [TestCase("get-blueprint", TestName = "工具名稱有連字號")]
    [TestCase("mcp__kumi__get_blueprint ", TestName = "工具名稱有空白")]
    public async Task Report_BadToolName_Returns422(string name)
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var result = await _h.Agents.ReportAsync(
            _h.Me, job.JobId, [LocalAgentHarness.Tool("t1", name, AssistantToolStatus.Running)], CancellationToken.None);

        // Assert
        AssertAgentInvalid(result.Failure);
    }

    [Test]
    public async Task Report_ToolNameAndCallIdAtTheirLimits_AreAccepted()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var result = await _h.Agents.ReportAsync(
            _h.Me, job.JobId, [LocalAgentHarness.Tool(new string('c', 100), new string('n', 100), AssistantToolStatus.Running)], CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task Report_ToolErrorTooLong_Returns422()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var result = await _h.Agents.ReportAsync(
            _h.Me,
            job.JobId,
            [LocalAgentHarness.Tool("t1", "get_blueprint", AssistantToolStatus.Failed, error: new string('錯', AiRules.MaxErrorLength + 1))],
            CancellationToken.None);

        // Assert
        AssertAgentInvalid(result.Failure);
    }

    [Test]
    public async Task Report_WhenTheOrganizationTurnedPersonalAccountsOff_Returns403()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        await _h.Host.Get<IAiSettingsService>().SaveAsync(
            _h.Assistant.Admin, new AiSettingsInput(false, null) { AllowPersonalConnections = false }, CancellationToken.None);

        // Act
        var report = await _h.Agents.ReportAsync(_h.Me, job.JobId, [], CancellationToken.None);
        var finish = await _h.Agents.FinishAsync(_h.Me, job.JobId, new LocalAgentFinish(AssistantMessageStatus.Complete, null, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Failure?.Code, Is.EqualTo(AiErrorCodes.PersonalDisabled));
            Assert.That(finish.Failure?.Code, Is.EqualTo(AiErrorCodes.PersonalDisabled));
        }
    }

    [Test]
    public async Task Finish_RecordsTheResultAndUsage()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        await _h.FinishAsync(job.JobId, AssistantMessageStatus.Failed, "引擎沒有登入\0。", new AssistantUsage(120, 30));
        var row = await _h.JobRowAsync(job.JobId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.State, Is.EqualTo("finished"));
            Assert.That(row.FinishStatus, Is.EqualTo("failed"));
            Assert.That(row.FinishError, Is.EqualTo("引擎沒有登入。"));
            Assert.That(row.InputTokens, Is.EqualTo(120));
            Assert.That(row.OutputTokens, Is.EqualTo(30));
        }
    }

    [Test]
    public async Task Finish_Twice_SucceedsAndKeepsTheFirstResult()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        await _h.FinishAsync(job.JobId, AssistantMessageStatus.Complete, null, new AssistantUsage(1, 2));

        // Act
        var again = await _h.Agents.FinishAsync(
            _h.Me, job.JobId, new LocalAgentFinish(AssistantMessageStatus.Failed, "第二次", new AssistantUsage(9, 9)), CancellationToken.None);
        var row = await _h.JobRowAsync(job.JobId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Succeeded, Is.True);
            Assert.That(row.FinishStatus, Is.EqualTo("complete"));
            Assert.That(row.FinishError, Is.Null);
            Assert.That(row.InputTokens, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Finish_JobOfAnotherPersonOrUnknownOrNotClaimed_ReturnsNotFound()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);
        await _h.SendAsync(_engine, "第二則");
        Guid waiting;
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            waiting = (await db.LocalJobs.SingleAsync(j => j.State == "waiting")).Id;
        }

        var done = new LocalAgentFinish(AssistantMessageStatus.Complete, null, null);

        // Act
        var foreign = await _h.Agents.FinishAsync(other, job.JobId, done, CancellationToken.None);
        var unknown = await _h.Agents.FinishAsync(_h.Me, Guid.CreateVersion7(), done, CancellationToken.None);
        var unclaimed = await _h.Agents.FinishAsync(_h.Me, waiting, done, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertNotFound(foreign.Failure);
            AssertNotFound(unknown.Failure);
            AssertNotFound(unclaimed.Failure);
            Assert.That((await _h.JobRowAsync(job.JobId)).State, Is.EqualTo("claimed"));
        }
    }

    [Test]
    public async Task Finish_UnknownStatusOrNegativeUsage_Returns422()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        var status = await _h.Agents.FinishAsync(_h.Me, job.JobId, new LocalAgentFinish((AssistantMessageStatus)9, null, null), CancellationToken.None);
        var usage = await _h.Agents.FinishAsync(
            _h.Me, job.JobId, new LocalAgentFinish(AssistantMessageStatus.Complete, null, new AssistantUsage(-1, 0)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertAgentInvalid(status.Failure);
            AssertAgentInvalid(usage.Failure);
        }
    }

    [Test]
    public async Task CheckIn_DropsLeftoverJobsOlderThanAnHour()
    {
        // Arrange
        await _h.SendAsync(_engine);
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("x"));
        _h.Assistant.Clock.Advance(TimeSpan.FromHours(2));

        // Act
        await _h.CheckInAsync();
        await using var db = await _h.Host.CreateDbContextAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await db.LocalJobs.CountAsync(), Is.Zero);
            Assert.That(await db.LocalEvents.CountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Claim_JobLeftOverFromBeforeARestart_IsNotHandedOut()
    {
        // Arrange：回覆那一頭早就放棄了（領走的逾時加寬限都過了）。
        await _h.SendAsync(_engine);
        await _h.WaitForJobRowsAsync(1);
        _h.Assistant.Clock.Advance(LocalAgentHarness.PatientTimings.ClaimTimeout + TimeSpan.FromMinutes(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        // Act
        var polled = await _h.CheckInAsync(capacity: 1, cancellationToken: timeout.Token);

        // Assert
        Assert.That(polled.Value, Is.Null);
    }

    [Test]
    public async Task CheckIn_ExpiresClaimedAndWaitingJobsOlderThanTheReplyTimeout_SoTheAgentIsToldToStop()
    {
        // Arrange
        var claimed = await ClaimedJobAsync();
        await _h.SendAsync(_engine, "第二則");
        await _h.WaitForJobRowsAsync(2);
        _h.Assistant.Clock.Advance(AiRules.ReplyTimeout + TimeSpan.FromMinutes(1));

        // Act
        await _h.CheckInAsync();
        var ack = await _h.ReportAsync(claimed.JobId, LocalAgentHarness.Text("遲到"));
        await using var db = await _h.Host.CreateDbContextAsync();
        var states = await db.LocalJobs.Select(j => new { j.State, j.CancelRequested }).ToListAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ack.Cancelled, Is.True);
            Assert.That(states, Has.All.Matches<dynamic>(j => j.State == "finished" && j.CancelRequested));
            Assert.That(await _h.EventCountAsync(claimed.JobId), Is.Zero);
        }
    }

    [Test]
    public async Task Report_ClaimedJobOlderThanTheReplyTimeout_AcksCancelledWithoutStoringAnything()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        _h.Assistant.Clock.Advance(AiRules.ReplyTimeout + TimeSpan.FromMinutes(1));

        // Act
        var ack = await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("遲到"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ack.Cancelled, Is.True);
            Assert.That(await _h.EventCountAsync(job.JobId), Is.Zero);
            Assert.That((await _h.JobRowAsync(job.JobId)).State, Is.EqualTo("finished"));
        }
    }

    [Test]
    public async Task Report_EmptyTextIsNotStoredButStillCountsAsAHeartbeat()
    {
        // Arrange
        var job = await ClaimedJobAsync();

        // Act
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text(string.Empty), LocalAgentHarness.Text("有字"), LocalAgentHarness.Text(string.Empty));

        // Assert
        Assert.That(await _h.EventCountAsync(job.JobId), Is.EqualTo(1));
    }

    [Test]
    public async Task Report_MoreEventsThanTheJobLimit_Returns422AndKeepsWhatWasStored()
    {
        // Arrange
        var job = await ClaimedJobAsync();
        var batch = Enumerable.Repeat<LocalAgentEvent>(LocalAgentHarness.Text("x"), AiRules.MaxLocalAgentEventsPerReport).ToArray();
        for (var i = 0; i < 25; i++)
        {
            await _h.ReportAsync(job.JobId, batch);
        }

        // Act
        var over = await _h.Agents.ReportAsync(_h.Me, job.JobId, [LocalAgentHarness.Text("x")], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(over.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentInvalid));
            Assert.That(await _h.EventCountAsync(job.JobId), Is.EqualTo(5000));
        }
    }

    [Test]
    public async Task Claim_JobCreatedInTheSameProcess_WakesTheWaitingPollWithoutWaitingForTheDatabasePoll()
    {
        // Arrange：備援的查詢間隔是 2 秒；訊號要讓等的人在遠短於它的時間內領到。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = System.Diagnostics.Stopwatch.StartNew();
        var polling = _h.CheckInAsync(capacity: 1, cancellationToken: timeout.Token);
        await Task.Delay(300);

        // Act
        await _h.SendAsync(_engine);
        var job = (await polling).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job, Is.Not.Null);
            Assert.That(started.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(1800)));
        }
    }

    private async Task<LocalAgentJob> ClaimedJobAsync()
    {
        await _h.SendAsync(_engine);
        return await _h.ClaimAsync();
    }

    private static void AssertNotFound(Failure? failure)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(failure?.Code, Is.EqualTo(AiErrorCodes.AgentJobNotFound));
        }
    }

    private static void AssertAgentInvalid(Failure? failure)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(failure?.Code, Is.EqualTo(AiErrorCodes.AgentInvalid));
        }
    }
}
