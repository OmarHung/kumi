using System.Net.Http;
using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>回覆的過程與各種結束的方式：事件的順序、存下來的樣子、停止、逾時、失敗、拒絕與截斷。</summary>
[TestFixture]
public sealed class AssistantReplyTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Reply_TextOnly_StreamsDeltasAndEndsWithTheStoredMessage()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("[REPLY]\n你").Text("好").Usage(10, 2).Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Events.Select(e => e.GetType()), Is.EqualTo(new[]
            {
                typeof(AssistantTextDelta), typeof(AssistantTextDelta), typeof(AssistantRunCompleted),
            }));
            Assert.That(reply.Text, Is.EqualTo("你好"));
            Assert.That(reply.Final.Id, Is.EqualTo(reply.Run.AssistantMessageId));
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(reply.Final.Error, Is.Null);
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("你好") }));
        }
    }

    [Test]
    public async Task Reply_StoredMessage_CarriesTheSelectionConnectionNameAndUsage()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("好").Usage(120, 30).Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);
        var stored = (await _h.MessagesAsync(conversation.Id)).Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.Id, Is.EqualTo(reply.Final.Id));
            Assert.That(stored.Parts, Is.EqualTo(reply.Final.Parts));
            Assert.That(stored.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(stored.Selection, Is.EqualTo(_h.Selection));
            Assert.That(stored.ConnectionName, Is.EqualTo("測試連線"));
            Assert.That(stored.Usage, Is.EqualTo(new AssistantUsage(120, 30)));
        }
    }

    [Test]
    public async Task Reply_ProviderReportsNoUsage_UsageIsNull()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        Assert.That(reply.Final.Usage, Is.Null);
    }

    [Test]
    public async Task Reply_UsageOfEveryModelCall_IsAddedUp()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Call("a", "get_blueprint").Usage(100, 20))
            .Enqueue(Turn.Create().Call("b", "describe_usage").Usage(150, 25))
            .Enqueue(Turn.Create().Text("好").Usage(200, 30).Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        Assert.That(reply.Final.Usage, Is.EqualTo(new AssistantUsage(450, 75)));
    }

    [Test]
    public async Task Reply_WithToolCalls_SendsRunningThenDoneInOrder_AndSplitsTheTextAroundThem()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Text("[REPLY]\n我先讀設定。").Call("toolu_01", "get_blueprint"))
            .Enqueue(Turn.Create().Text("[REPLY]\n讀好了。").Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Events.Select(Describe), Is.EqualTo(new[]
            {
                "text:我先讀設定。", "tool:c1:get_blueprint:Running", "tool:c1:get_blueprint:Done", "text:讀好了。", "completed",
            }));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[]
            {
                new AssistantTextPart("我先讀設定。"),
                new AssistantToolPart("c1", "get_blueprint", AssistantToolStatus.Done, null, null),
                new AssistantTextPart("讀好了。"),
            }));
        }
    }

    [Test]
    public async Task Reply_ToolsRunAsTheStartingPerson_OnTheConversationsProject()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("a", "get_blueprint")).Reply("好");

        // Act
        await _h.SendAsync(conversation.Id);

        // Assert：開對話、送訊息各讀一次專案，工具再讀一次；每一次都是發起的人。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_h.Projects.Gets, Has.Count.EqualTo(3));
            Assert.That(_h.Projects.Gets.Select(g => g.Actor), Has.All.EqualTo(_h.Actor));
            Assert.That(_h.Projects.Gets.Select(g => g.Key), Has.All.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task Reply_ProposingAChangeSet_ShowsTheCardNumberOnTheToolPart()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Call("p", "propose_changeset", new { changeset = new { baseVersion = 3, summary = "改名" } }))
            .Enqueue(Turn.Create().Text("變更集 #12 等你核准").Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        var done = reply.ToolUpdates.Last();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Status, Is.EqualTo(AssistantToolStatus.Done));
            Assert.That(done.ChangeSetNumber, Is.EqualTo(12));
            Assert.That(reply.ToolUpdates.First().ChangeSetNumber, Is.Null);
            Assert.That(reply.Final.Parts.OfType<AssistantToolPart>().Single().ChangeSetNumber, Is.EqualTo(12));
        }
    }

    [Test]
    public async Task Reply_ToolFailure_IsFailedWithTheReason_AndTheModelSeesIt()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Call("t", "get_template", new { templateKey = "nope" }))
            .Enqueue(Turn.Create().Text("沒有這個模板").Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        var failed = reply.ToolUpdates.Last();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Status, Is.EqualTo(AssistantToolStatus.Failed));
            Assert.That(failed.Error, Does.Contain("沒有這個模板"));
            Assert.That(failed.ChangeSetNumber, Is.Null);
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
        }
    }

    [Test]
    public async Task Reply_ReasoningFromTheModel_IsNeitherShownNorStored()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Reasoning("秘密的思考過程").Text("答案").Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);
        var row = await _h.RowAsync(reply.Final.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Text, Is.EqualTo("答案"));
            Assert.That(row.Parts, Does.Not.Contain("秘密"));
            Assert.That(row.Transcript, Does.Not.Contain("秘密"));
        }
    }

    [Test]
    public async Task Reply_NulInModelText_IsDroppedSoTheMessageStillSaves()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("a\0b").Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("ab") }));
        }
    }

    [Test]
    public async Task Reply_ReaderStops_SavesWhatItHasAsStopped()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("[REPLY]\n寫到一半").Hang());
        var run = await _h.StartAsync(conversation.Id);
        using var stop = new CancellationTokenSource();
        var events = new List<AssistantRunEvent>();

        // Act
        await foreach (var runEvent in run.Events.WithCancellation(stop.Token))
        {
            events.Add(runEvent);
            if (runEvent is AssistantTextDelta)
            {
                await stop.CancelAsync();
            }
        }

        // Assert
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        var row = await _h.ConversationRowAsync(conversation.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
            Assert.That(final.Error, Is.Null);
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("寫到一半") }));
            Assert.That(row.RunningUntil, Is.Null);
            Assert.That(row.MessageCount, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Reply_ReaderLeavesEarly_StillSavesAsStoppedBeforeDisposeReturns()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("[REPLY]\n一段").Hang());
        var run = await _h.StartAsync(conversation.Id);

        // Act
        await foreach (var runEvent in run.Events)
        {
            if (runEvent is AssistantTextDelta)
            {
                break;
            }
        }

        // Assert
        var stored = (await _h.MessagesAsync(conversation.Id)).Last();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
            Assert.That((await _h.ConversationRowAsync(conversation.Id)).RunningUntil, Is.Null);
        }
    }

    [Test]
    public async Task Reply_TakesLongerThanTheTimeout_IsStoppedWithTheTimeoutReason()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await AssistantHarness.StartAsync(timings: new AssistantTimings(TimeSpan.FromMilliseconds(300), AiRules.RunLease));
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("[REPLY]\n慢慢想").Hang());

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
            Assert.That(reply.Final.Error, Is.EqualTo("超過 5 分鐘，回覆中斷了。"));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("慢慢想") }));
        }
    }

    [Test]
    public async Task Reply_ProviderThrows_IsFailedWithAReasonThatLeaksNoSecrets()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("開頭").Throw(new HttpRequestException("連線失敗 key=sk-test-key-0001")));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(reply.Final.Error, Is.Not.Empty);
            Assert.That(reply.Final.Error, Does.Not.Contain("sk-test-key"));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("開頭") }));
            Assert.That((await _h.ConversationRowAsync(conversation.Id)).RunningUntil, Is.Null);
        }
    }

    [Test]
    public async Task Reply_FinishingStepThrows_ReleasesTheLeaseAndStillSendsTheCompletedEvent()
    {
        // Arrange：模型回完之後，收尾要讀時間（建立資料列），這一次讓它丟例外。
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("好").Do(_h.Clock.FailNextRead).Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        var row = await _h.ConversationRowAsync(conversation.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(reply.Final.Error, Is.EqualTo(AssistantReplyRunner.SaveFailedMessage));
            Assert.That(row.RunningUntil, Is.Null);
            Assert.That(reply.Events.Last(), Is.TypeOf<AssistantRunCompleted>());
        }

        // 租約放掉了，下一則訊息可以馬上送。
        _h.Script.Reply("再好");
        var again = await _h.SendAsync(conversation.Id, "再來一次");
        Assert.That(again.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
    }

    [Test]
    public async Task Reply_FailureMessageWithAnEmojiAtTheCutPoint_StillSavesTheReply()
    {
        // Arrange：錯誤訊息很長，要切的位置剛好落在 emoji（代理對）中間；切壞了就是孤立的高代理，資料庫寫不進去。
        var conversation = await _h.NewConversationAsync();
        var message = new string('錯', AiRules.MaxErrorLength - 20) + string.Concat(Enumerable.Repeat("😀", 20));
        _h.Script.Enqueue(Turn.Create().Throw(new PmTool.Ai.Providers.AiProviderException(message)));

        // Act
        var reply = await _h.SendAsync(conversation.Id);
        var stored = await _h.RowAsync(reply.Final.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(stored.Status, Is.EqualTo("failed"));
            Assert.That(stored.Error, Does.StartWith("錯錯錯"));
            Assert.That(Providers.AiProviderFailuresTests.HasLoneSurrogate(stored.Error!), Is.False);
            Assert.That(stored.Error!.Length, Is.LessThanOrEqualTo(AiRules.MaxErrorLength));
        }
    }

    [Test]
    public async Task Reply_ContentFilter_IsFailedWithTheRefusalMessage()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Finish(ChatFinishReason.ContentFilter));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(reply.Final.Error, Is.EqualTo("模型拒絕了這個要求，可以換一個模型或換個說法再試。"));
        }
    }

    [Test]
    public async Task Reply_LengthLimit_IsFailedWithTheTruncationMessage()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("被截斷的").Finish(ChatFinishReason.Length));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(reply.Final.Error, Is.EqualTo("回覆超過長度上限被截斷了，請把要求拆小一點再試。"));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("被截斷的") }));
        }
    }

    [Test]
    public async Task Reply_TooManyToolCalls_StopsWithANoteAndNothingStuckRunning()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        for (var i = 0; i < AiRules.MaxToolCallsPerReply + 3; i++)
        {
            _h.Script.Enqueue(Turn.Create().Call($"call-{i}", "describe_usage"));
        }

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        var tools = reply.Final.Parts.OfType<AssistantToolPart>().ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(tools, Has.None.Matches<AssistantToolPart>(t => t.Status == AssistantToolStatus.Running));
            Assert.That(tools.Count(t => t.Status == AssistantToolStatus.Done), Is.LessThanOrEqualTo(AiRules.MaxToolCallsPerReply));
            Assert.That(reply.Final.Parts.Last(), Is.TypeOf<AssistantTextPart>());
        }
    }

    [Test]
    public async Task Reply_ModelCall_GetsTheSixteenToolsAndTheOutputLimit()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendAsync(conversation.Id);

        // Assert
        var options = _h.Script.Calls.Single().Options!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Tools!.Select(t => t.Name), Is.EquivalentTo(new[]
            {
                "get_blueprint", "list_templates", "get_template", "describe_usage", "preview_changeset", "propose_changeset",
                "get_item", "search_items", "propose_item_changes", "propose_new_items",
                "get_project", "list_people", "propose_member_changes",
                "propose_item_deletions", "propose_bulk_item_changes", "withdraw_proposal",
            }));
            Assert.That(options.MaxOutputTokens, Is.EqualTo(AiRules.MaxOutputTokens));
            Assert.That(_h.Gateway.ChatEndpoints.Single().Kind, Is.EqualTo(AiProviderKind.Anthropic));
        }
    }

    [Test]
    public async Task Reply_Events_CanOnlyBeReadOnce()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        var run = await _h.StartAsync(conversation.Id);
        await AssistantHarness.ReadAllAsync(run);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => run.Events.GetAsyncEnumerator());
    }

    [Test]
    public async Task Title_ShowsAMentionAsTheName_NotTheMentionSyntax()
    {
        // Arrange：輸入框的 @ 送出時是提及語法；標題照原文會變成「請 @[Ivan](mention:01a1…」。
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendAsync(conversation.Id, "請 @[Ivan](mention:019b2c3d-4e5f-7a6b-8c7d-9e0f1a2b3c4d) 負責換憑證");
        var row = await _h.ConversationRowAsync(conversation.Id);

        // Assert
        Assert.That(row.Title, Is.EqualTo("請 @Ivan 負責換憑證"));
    }

    [Test]
    public async Task Title_IsTheFirstMessage_WithNewlinesFlattened_AndCutAtTheLimit()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好").Reply("好");
        var long40 = new string('長', AiRules.MaxTitleLength + 10);

        // Act
        await _h.SendAsync(conversation.Id, $"第一行\n第二行 {long40}");
        var afterFirst = await _h.ConversationRowAsync(conversation.Id);
        await _h.SendAsync(conversation.Id, "第二則不會改標題");
        var afterSecond = await _h.ConversationRowAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterFirst.Title, Does.StartWith("第一行 第二行 長"));
            Assert.That(afterFirst.Title!.Length, Is.LessThanOrEqualTo(AiRules.MaxTitleLength));
            Assert.That(afterSecond.Title, Is.EqualTo(afterFirst.Title));
        }
    }

    private static string Describe(AssistantRunEvent runEvent) => runEvent switch
    {
        AssistantTextDelta text => $"text:{text.Delta}",
        AssistantToolUpdate tool => $"tool:{tool.Tool.CallId}:{tool.Tool.Name}:{tool.Tool.Status}",
        AssistantRunCompleted => "completed",
        _ => runEvent.GetType().Name,
    };
}
