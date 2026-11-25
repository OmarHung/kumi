using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.Settings;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>組織連線的用量上限（AI-01）：每人每天、全組織每月的則數與 token；只算組織的連線；失敗退還；回覆結束記 token。</summary>
[TestFixture]
public sealed class AssistantUsageLimitTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Start_DailyLimitReached_IsRefusedWithTheUsageCodeAndNothingIsStored()
    {
        // Arrange
        await SetLimitsAsync(new AiUsageLimits(2, null, null));
        var conversation = await _h.NewConversationAsync();
        await SendOkAsync(conversation.Id);
        await SendOkAsync(conversation.Id);

        // Act
        var started = await StartRawAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.UsageLimitReached));
            Assert.That(started.Failure?.Message, Does.Contain("今天").And.Contain("每天 2 則"));
            Assert.That((await _h.ConversationRowAsync(conversation.Id)).MessageCount, Is.EqualTo(4));
            Assert.That(await CountUsageAsync(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Start_DailyLimitIsPerPerson_AnotherPersonIsNotAffected()
    {
        // Arrange
        await SetLimitsAsync(new AiUsageLimits(1, null, null));
        var mine = await _h.NewConversationAsync();
        await SendOkAsync(mine.Id);
        var theirs = await _h.NewConversationAsync(_h.Admin);

        // Act
        var other = await _h.Service.StartAsync(
            _h.Admin, AssistantHarness.ProjectKey, theirs.Id, new AssistantMessageInput("你好", _h.Selection), CancellationToken.None);

        // Assert
        Assert.That(other.Succeeded, Is.True, () => other.Failure?.Message ?? string.Empty);
        await AssistantHarness.ReadAllAsync(other.Value);
    }

    [Test]
    public async Task Start_NextDayInTheOrganizationTimeZone_CountsFromZeroAgain()
    {
        // Arrange
        await SetLimitsAsync(new AiUsageLimits(1, null, null));
        var conversation = await _h.NewConversationAsync();
        await SendOkAsync(conversation.Id);
        _h.Clock.Advance(TimeSpan.FromHours(24));

        // Act
        var started = await StartRawAsync(conversation.Id);

        // Assert
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        await AssistantHarness.ReadAllAsync(started.Value);
    }

    [Test]
    public async Task Start_MonthlyRequestsReached_IsRefusedForEveryone()
    {
        // Arrange
        await SetLimitsAsync(new AiUsageLimits(null, 2, null));
        var mine = await _h.NewConversationAsync();
        await SendOkAsync(mine.Id);
        await SendOkAsync(mine.Id);
        var theirs = await _h.NewConversationAsync(_h.Admin);

        // Act
        var started = await _h.Service.StartAsync(
            _h.Admin, AssistantHarness.ProjectKey, theirs.Id, new AssistantMessageInput("你好", _h.Selection), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.UsageLimitReached));
            Assert.That(started.Failure?.Message, Does.Contain("這個月").And.Contain("每月 2 則"));
        }
    }

    [Test]
    public async Task Start_MonthlyTokensReached_RefusesTheNextOneButNotTheOneThatCrossedIt()
    {
        // Arrange
        await SetLimitsAsync(new AiUsageLimits(null, null, 12));
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("好").Usage(10, 5).Finish(ChatFinishReason.Stop));
        await _h.SendAsync(conversation.Id);

        // Act
        var started = await StartRawAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.UsageLimitReached));
            Assert.That(started.Failure?.Message, Does.Contain("token"));
        }
    }

    [Test]
    public async Task Start_NextMonth_TheMonthlyLimitStartsOver()
    {
        // Arrange
        await SetLimitsAsync(new AiUsageLimits(null, 1, null));
        var conversation = await _h.NewConversationAsync();
        await SendOkAsync(conversation.Id);
        _h.Clock.Advance(TimeSpan.FromDays(31));

        // Act
        var started = await StartRawAsync(conversation.Id);

        // Assert
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        await AssistantHarness.ReadAllAsync(started.Value);
    }

    [Test]
    public async Task Start_SettingsDefault_LimitsTheDailyRequestsAt200()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        await SeedUsageAsync(AiRules.DefaultDailyRequestsPerPerson);

        // Act
        var started = await StartRawAsync(conversation.Id);

        // Assert
        Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.UsageLimitReached));
    }

    [Test]
    public async Task Start_FailsAfterReserving_TheReservationIsReleasedAndDoesNotCount()
    {
        // Arrange
        await SetLimitsAsync(new AiUsageLimits(2, null, null));
        var conversation = await _h.NewConversationAsync();
        var gate = new TaskCompletionSource();
        _h.Script.Enqueue(Turn.Create().WaitFor(gate.Task).Text("好").Finish(ChatFinishReason.Stop));
        var first = await _h.StartAsync(conversation.Id);

        // Act：同一則對話還在回覆，第二次拿不到租約，預留要退還。
        var busy = await StartRawAsync(conversation.Id);
        gate.SetResult();
        await AssistantHarness.ReadAllAsync(first);
        var afterwards = await StartRawAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(busy.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationBusy));
            Assert.That(afterwards.Succeeded, Is.True, () => afterwards.Failure?.Message ?? string.Empty);
            Assert.That(await CountUsageAsync(), Is.EqualTo(2));
        }

        await AssistantHarness.ReadAllAsync(afterwards.Value);
    }

    [Test]
    public async Task Reply_Complete_SettlesTheReservationWithTokens()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("好").Usage(120, 30).Finish(ChatFinishReason.Stop));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        var row = await db.ConnectionUsage.AsNoTracking().SingleAsync(u => u.Id == reply.Run.AssistantMessageId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.State, Is.EqualTo("settled"));
            Assert.That(row.FinishStatus, Is.EqualTo("complete"));
            Assert.That(row.InputTokens, Is.EqualTo(120));
            Assert.That(row.OutputTokens, Is.EqualTo(30));
            Assert.That(row.ConnectionId, Is.EqualTo(_h.Connection.Id));
            Assert.That(row.PersonId, Is.EqualTo(_h.Actor.PersonId));
            Assert.That(row.SettledAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task Reply_Failed_StillCountsAndIsSettledAsFailed()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Usage(40, 0).Throw(new InvalidOperationException("壞了")));

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        var row = await db.ConnectionUsage.AsNoTracking().SingleAsync(u => u.Id == reply.Run.AssistantMessageId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(row.State, Is.EqualTo("settled"));
            Assert.That(row.FinishStatus, Is.EqualTo("failed"));
        }
    }

    [Test]
    public async Task Reply_DeletingTheConversation_DoesNotResetTheQuota()
    {
        // Arrange
        await SetLimitsAsync(new AiUsageLimits(1, null, null));
        var conversation = await _h.NewConversationAsync();
        await SendOkAsync(conversation.Id);
        await _h.Service.DeleteAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);
        var fresh = await _h.NewConversationAsync();

        // Act
        var started = await StartRawAsync(fresh.Id);

        // Assert
        Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.UsageLimitReached));
    }

    [Test]
    public async Task Reply_PersonalConnection_IsNotCounted()
    {
        // Arrange
        await _h.Host.Get<IAiSettingsService>().SaveAsync(
            _h.Admin,
            new AiSettingsInput(true, new AiSelection(_h.Connection.Id, ScriptedGateway.ModelId, null)) { AllowPersonalConnections = true },
            CancellationToken.None);
        var mine = await _h.Host.Get<IPersonalAiConnectionService>().CreateAsync(
            _h.Actor,
            new AiConnectionInput("我的", AiProviderKind.Anthropic, null, "sk-personal-0001", [ScriptedGateway.ModelId], []),
            CancellationToken.None);
        var conversation = await _h.NewConversationAsync();

        // Act
        await _h.SendAsync(conversation.Id, selection: new AiSelection(mine.Value.Id, ScriptedGateway.ModelId, null));

        // Assert
        Assert.That(await CountUsageAsync(), Is.Zero);
    }

    private async Task SetLimitsAsync(AiUsageLimits limits)
    {
        var settings = _h.Host.Get<IAiSettingsService>();
        var current = (await settings.GetAsync(_h.Admin, CancellationToken.None)).Value;
        var saved = await settings.SaveAsync(
            _h.Admin, new AiSettingsInput(current.Enabled, current.Default) { Limits = limits }, CancellationToken.None);
        Assert.That(saved.Succeeded, Is.True, () => saved.Failure?.Message ?? string.Empty);
    }

    private async Task SendOkAsync(Guid conversationId)
    {
        _h.Script.Reply("好");
        await _h.SendAsync(conversationId);
    }

    private Task<Result<AssistantRun>> StartRawAsync(Guid conversationId) =>
        _h.Service.StartAsync(
            _h.Actor, AssistantHarness.ProjectKey, conversationId, new AssistantMessageInput("再一則", _h.Selection), CancellationToken.None);

    private async Task<int> CountUsageAsync()
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.ConnectionUsage.CountAsync(u => u.State != "released");
    }

    private async Task SeedUsageAsync(int count)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        for (var i = 0; i < count; i++)
        {
            db.ConnectionUsage.Add(new PmTool.Ai.Persistence.AiConnectionUsageRow
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = _h.OrganizationId,
                ConnectionId = _h.Connection.Id,
                PersonId = _h.Actor.PersonId,
                ProjectKey = AssistantHarness.ProjectKey,
                State = "settled",
                CreatedAt = _h.Clock.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync();
    }
}
