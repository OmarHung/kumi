using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Assistant.AssistantConversationTests;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>送訊息之前的檢查：訊息、選擇、對話長度、租約。任何一項不過都不能存下東西。</summary>
[TestFixture]
public sealed class AssistantStartChecksTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [TestCase("")]
    [TestCase("   \n\t ")]
    public async Task StartAsync_EmptyText_IsMessageInvalid(string text)
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        var result = await StartRawAsync(conversation.Id, text);

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.MessageInvalid);
        await AssertNothingStoredAsync(conversation.Id);
    }

    [Test]
    public async Task StartAsync_TextLongerThanTheLimit_IsMessageInvalid()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        var result = await StartRawAsync(conversation.Id, new string('字', AiRules.MaxMessageLength + 1));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.MessageInvalid);
        await AssertNothingStoredAsync(conversation.Id);
    }

    [TestCase("a\0b")]
    [TestCase("a\u0007b")]
    [TestCase("a\u001bb")]
    public async Task StartAsync_ControlCharacters_AreMessageInvalid(string text)
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        var result = await StartRawAsync(conversation.Id, text);

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.MessageInvalid);
        await AssertNothingStoredAsync(conversation.Id);
    }

    [Test]
    public async Task StartAsync_NewlinesTabsAndSurroundingSpace_AreFine()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        var run = await _h.StartAsync(conversation.Id, "  第一行\r\n\t第二行  \n");
        await AssistantHarness.ReadAllAsync(run);

        // Assert
        var text = ((AssistantTextPart)run.UserMessage.Parts.Single()).Text;
        Assert.That(text, Is.EqualTo("第一行\r\n\t第二行"));
    }

    [Test]
    public async Task StartAsync_TextExactlyAtTheLimit_IsAccepted()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        var run = await _h.StartAsync(conversation.Id, new string('字', AiRules.MaxMessageLength));
        await AssistantHarness.ReadAllAsync(run);

        // Assert
        Assert.That(run.UserMessage.Role, Is.EqualTo(AssistantRole.User));
    }

    [Test]
    public async Task StartAsync_UnknownConnection_IsSelectionInvalid()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        var result = await StartRawAsync(conversation.Id, "你好", new AiSelection(Guid.NewGuid(), ScriptedGateway.ModelId, null));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.SelectionInvalid);
        await AssertNothingStoredAsync(conversation.Id);
    }

    [Test]
    public async Task StartAsync_EffortTheModelDoesNotOffer_IsSelectionInvalid()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        var result = await StartRawAsync(conversation.Id, "你好", _h.Selection with { Effort = AiEffort.Max });

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.SelectionInvalid);
        await AssertNothingStoredAsync(conversation.Id);
    }

    [Test]
    public async Task StartAsync_AiTurnedOff_IsUnavailable()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        await _h.Host.Get<IAiSettingsService>().SaveAsync(
            new Actor(_h.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true), new AiSettingsInput(false, null), CancellationToken.None);

        // Act
        var result = await StartRawAsync(conversation.Id, "你好");

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.Unavailable);
    }

    [Test]
    public async Task StartAsync_UnknownConversation_IsConversationNotFound()
    {
        // Act
        var result = await StartRawAsync(Guid.NewGuid(), "你好");

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.ConversationNotFound);
    }

    [Test]
    public async Task StartAsync_SomeoneElsesConversation_IsConversationNotFound()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync(_h.Admin);

        // Act
        var result = await StartRawAsync(conversation.Id, "你好");

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.ConversationNotFound);
        await AssertNothingStoredAsync(conversation.Id);
    }

    [Test]
    public async Task StartAsync_ConversationAtTheMessageLimit_IsTooLong()
    {
        // Arrange：已有 59 則，再加使用者與助理兩則就超過 60。
        var conversation = await _h.NewConversationAsync();
        await SetMessageCountAsync(conversation.Id, AiRules.MaxMessagesPerConversation - 1);

        // Act
        var result = await StartRawAsync(conversation.Id, "你好");

        // Assert
        AssertFailure(result.Failure, FailureKind.Conflict, AiErrorCodes.ConversationTooLong);
        Assert.That((await _h.ConversationRowAsync(conversation.Id)).RunningUntil, Is.Null);
    }

    [Test]
    public async Task StartAsync_ConversationJustBelowTheLimit_StillWorks()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        await SetMessageCountAsync(conversation.Id, AiRules.MaxMessagesPerConversation - 2);
        _h.Script.Reply("好");

        // Act
        var run = await _h.StartAsync(conversation.Id, "你好");
        await AssistantHarness.ReadAllAsync(run);

        // Assert
        Assert.That((await _h.ConversationRowAsync(conversation.Id)).MessageCount, Is.EqualTo(AiRules.MaxMessagesPerConversation));
    }

    [Test]
    public async Task StartAsync_SameConversationWhileReplying_IsBusyAndStoresNothingMore()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var gate = new TaskCompletionSource();
        _h.Script.Enqueue(Turn.Create().WaitFor(gate.Task).Text("好").Finish(ChatFinishReason.Stop));
        var first = await _h.StartAsync(conversation.Id);

        // Act
        var second = await StartRawAsync(conversation.Id, "再問一次");
        var summary = (await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Value.Single();
        gate.SetResult();
        await AssistantHarness.ReadAllAsync(first);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(second.Failure, FailureKind.Conflict, AiErrorCodes.ConversationBusy);
            Assert.That(summary.IsRunning, Is.True);
            Assert.That(summary.MessageCount, Is.EqualTo(1));
            Assert.That((await _h.MessagesAsync(conversation.Id)).Count, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task StartAsync_ThirdConversationAtOnce_IsTooManyRuns()
    {
        // Arrange
        var gate = new TaskCompletionSource();
        var conversations = new List<AssistantConversationSummary>();
        for (var i = 0; i < AiRules.MaxConcurrentRunsPerPerson + 1; i++)
        {
            conversations.Add(await _h.NewConversationAsync());
        }

        var running = new List<AssistantRun>();
        foreach (var conversation in conversations.Take(AiRules.MaxConcurrentRunsPerPerson))
        {
            _h.Script.Enqueue(Turn.Create().WaitFor(gate.Task).Text("好").Finish(ChatFinishReason.Stop));
            running.Add(await _h.StartAsync(conversation.Id));
        }

        // Act
        var third = await StartRawAsync(conversations[^1].Id, "你好");
        var busyOne = await StartRawAsync(conversations[0].Id, "你好");
        gate.SetResult();
        foreach (var run in running)
        {
            await AssistantHarness.ReadAllAsync(run);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(third.Failure, FailureKind.Conflict, AiErrorCodes.TooManyRuns);
            AssertFailure(busyOne.Failure, FailureKind.Conflict, AiErrorCodes.ConversationBusy);
        }
    }

    [Test]
    public async Task StartAsync_AfterOneReplyEnds_TheNextConversationCanStart()
    {
        // Arrange
        var gate = new TaskCompletionSource();
        var conversations = new List<AssistantConversationSummary>();
        for (var i = 0; i < AiRules.MaxConcurrentRunsPerPerson + 1; i++)
        {
            conversations.Add(await _h.NewConversationAsync());
        }

        _h.Script.Reply("快的");
        var quick = await _h.StartAsync(conversations[0].Id);
        _h.Script.Enqueue(Turn.Create().WaitFor(gate.Task).Text("好").Finish(ChatFinishReason.Stop));
        var slow = await _h.StartAsync(conversations[1].Id);
        await AssistantHarness.ReadAllAsync(quick);
        _h.Script.Reply("第三個");

        // Act
        var third = await _h.StartAsync(conversations[2].Id);
        await AssistantHarness.ReadAllAsync(third);
        gate.SetResult();
        await AssistantHarness.ReadAllAsync(slow);

        // Assert
        Assert.That((await _h.ConversationRowAsync(conversations[2].Id)).MessageCount, Is.EqualTo(2));
    }

    [Test]
    public async Task StartAsync_OtherPeopleReplies_DoNotCountAgainstMe()
    {
        // Arrange
        var gate = new TaskCompletionSource();
        var mine = new List<AssistantRun>();
        for (var i = 0; i < AiRules.MaxConcurrentRunsPerPerson; i++)
        {
            var conversation = await _h.NewConversationAsync(_h.Admin);
            _h.Script.Enqueue(Turn.Create().WaitFor(gate.Task).Text("好").Finish(ChatFinishReason.Stop));
            mine.Add(await _h.StartAsync(conversation.Id, actor: _h.Admin));
        }

        var other = await _h.NewConversationAsync();
        _h.Script.Reply("我的");

        // Act
        var run = await _h.StartAsync(other.Id);
        await AssistantHarness.ReadAllAsync(run);
        gate.SetResult();
        foreach (var running in mine)
        {
            await AssistantHarness.ReadAllAsync(running);
        }

        // Assert
        Assert.That((await _h.ConversationRowAsync(other.Id)).MessageCount, Is.EqualTo(2));
    }

    [Test]
    public async Task StartAsync_AfterTheLeaseExpires_CanSendAgain()
    {
        // Arrange：第一則回覆卡住（程序當掉的樣子），過了租約就不再算在回覆。
        var conversation = await _h.NewConversationAsync();
        var gate = new TaskCompletionSource();
        _h.Script.Enqueue(Turn.Create().WaitFor(gate.Task).Text("卡住的").Finish(ChatFinishReason.Stop));
        var stuck = await _h.StartAsync(conversation.Id);
        _h.Clock.Advance(AiRules.RunLease + TimeSpan.FromSeconds(1));
        _h.Script.Reply("新的回覆");

        // Act
        var listed = (await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Value.Single();
        var again = await StartRawAsync(conversation.Id, "再問一次");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.IsRunning, Is.False);
            Assert.That(again.Succeeded, Is.True, () => again.Failure?.Message ?? string.Empty);
        }

        gate.SetResult();
        await AssistantHarness.ReadAllAsync(stuck);
        await AssistantHarness.ReadAllAsync(again.Value);
    }

    [Test]
    public async Task StartAsync_OldReplyFinishingAfterTakeover_FailsToSaveAndKeepsTheNewLease()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var oldGate = new TaskCompletionSource();
        var newGate = new TaskCompletionSource();
        _h.Script.Enqueue(Turn.Create().WaitFor(oldGate.Task).Text("舊的").Finish(ChatFinishReason.Stop));
        var old = await _h.StartAsync(conversation.Id);
        _h.Clock.Advance(AiRules.RunLease + TimeSpan.FromSeconds(1));
        _h.Script.Enqueue(Turn.Create().WaitFor(newGate.Task).Text("新的").Finish(ChatFinishReason.Stop));
        var current = await _h.StartAsync(conversation.Id, "再問一次");

        // Act
        oldGate.SetResult();
        var oldEvents = await AssistantHarness.ReadAllAsync(old);
        var leaseWhileNewRuns = (await _h.ConversationRowAsync(conversation.Id)).RunningUntil;
        newGate.SetResult();
        var newEvents = await AssistantHarness.ReadAllAsync(current);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldEvents.OfType<AssistantRunCompleted>().Single().Message.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(leaseWhileNewRuns, Is.Not.Null);
            Assert.That(newEvents.OfType<AssistantRunCompleted>().Single().Message.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That((await _h.ConversationRowAsync(conversation.Id)).RunningUntil, Is.Null);
        }
    }

    [Test]
    public async Task StartAsync_OldMessageWithUnreadableTranscript_IsConversationUnreadableAndKeepsNoLease()
    {
        // Arrange：舊訊息的 transcript 是合法的 JSON，但角色不認得（資料壞掉或格式改過）。
        var conversation = await _h.NewConversationAsync();
        await InsertMessageAsync(conversation.Id, 1, """[{"role":"alien","contents":[]}]""");
        await SetMessageCountAsync(conversation.Id, 1);

        // Act
        var first = await StartRawAsync(conversation.Id, "你好");
        var second = await StartRawAsync(conversation.Id, "再試一次");

        // Assert：兩次都是同樣明確的錯誤，不是「正在回覆」，也沒有存下使用者的訊息、沒有留租約。
        await using var db = await _h.Host.CreateDbContextAsync();
        var row = await _h.ConversationRowAsync(conversation.Id);
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(first.Failure, FailureKind.Conflict, AiErrorCodes.ConversationUnreadable);
            AssertFailure(second.Failure, FailureKind.Conflict, AiErrorCodes.ConversationUnreadable);
            Assert.That(row.RunningUntil, Is.Null);
            Assert.That(row.MessageCount, Is.EqualTo(1));
            Assert.That(await db.Messages.CountAsync(m => m.ConversationId == conversation.Id), Is.EqualTo(1));
        }
    }

    private async Task InsertMessageAsync(Guid conversationId, int sequence, string transcript)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        db.Messages.Add(new PmTool.Ai.Persistence.AiMessageRow
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            OrganizationId = _h.OrganizationId,
            Sequence = sequence,
            Role = "user",
            Parts = "[]",
            Transcript = transcript,
            CreatedAt = _h.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
    }

    private async Task<Result<AssistantRun>> StartRawAsync(Guid conversationId, string text, AiSelection? selection = null) =>
        await _h.Service.StartAsync(
            _h.Actor, AssistantHarness.ProjectKey, conversationId, new AssistantMessageInput(text, selection ?? _h.Selection), CancellationToken.None);

    private async Task AssertNothingStoredAsync(Guid conversationId)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        var row = await _h.ConversationRowAsync(conversationId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await db.Messages.CountAsync(m => m.ConversationId == conversationId), Is.Zero);
            Assert.That(row.MessageCount, Is.Zero);
            Assert.That(row.RunningUntil, Is.Null);
            Assert.That(row.Title, Is.Null);
        }
    }

    private async Task SetMessageCountAsync(Guid conversationId, int count)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        await db.Conversations.Where(c => c.Id == conversationId).ExecuteUpdateAsync(set => set.SetProperty(c => c.MessageCount, count));
    }
}
