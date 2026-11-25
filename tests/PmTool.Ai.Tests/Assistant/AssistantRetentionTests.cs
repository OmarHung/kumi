using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Maintenance;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>對話的保存期限與「看過全部項目、之後只剩自己的」就刪（AI-06）。</summary>
[TestFixture]
public sealed class AssistantRetentionTests
{
    private static readonly IReadOnlyList<string> OwnOnly = [WorkPermissions.BlueprintRead, WorkPermissions.ItemReadOwn];

    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Reply_ByAPersonWhoCanReadAllItems_MarksTheConversation()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        await SendAsync(conversation.Id);

        // Assert
        Assert.That((await _h.ConversationRowAsync(conversation.Id)).SawAllItems, Is.True);
    }

    [Test]
    public async Task Reply_ByAPersonWhoOnlySeesTheirOwnItems_DoesNotMarkTheConversation()
    {
        // Arrange
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail(OwnOnly);
        var conversation = await _h.NewConversationAsync();

        // Act
        await SendAsync(conversation.Id);

        // Assert
        Assert.That((await _h.ConversationRowAsync(conversation.Id)).SawAllItems, Is.False);
    }

    [Test]
    public async Task Get_AfterTheyLostItemRead_DeletesTheConversationAndSaysItDoesNotExist()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        await SendAsync(conversation.Id);
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail(OwnOnly);

        // Act
        var read = await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That(await ConversationCountAsync(), Is.Zero);
            Assert.That(await MessageCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task List_AfterTheyLostItemRead_OmitsAndDeletesThoseButKeepsTheOthers()
    {
        // Arrange
        var seen = await _h.NewConversationAsync();
        await SendAsync(seen.Id);
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail(OwnOnly);
        var fresh = await _h.NewConversationAsync();

        // Act
        var listed = (await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Select(c => c.Id), Is.EqualTo(new[] { fresh.Id }));
            Assert.That(await ConversationCountAsync(), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Start_AfterTheyLostItemRead_DeletesTheConversationAndStoresNothing()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        await SendAsync(conversation.Id);
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail(OwnOnly);

        // Act
        var started = await _h.Service.StartAsync(
            _h.Actor, AssistantHarness.ProjectKey, conversation.Id, new AssistantMessageInput("再問", _h.Selection), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That(await ConversationCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Get_ConversationNeverMarked_StaysReadableWithoutItemRead()
    {
        // Arrange
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail(OwnOnly);
        var conversation = await _h.NewConversationAsync();
        await SendAsync(conversation.Id);

        // Act
        var read = await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Succeeded, Is.True, () => read.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task Get_ConversationStillReplying_IsHiddenButNotDeletedUnderTheRunningReply()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var gate = new TaskCompletionSource();
        _h.Script.Enqueue(Turn.Create().WaitFor(gate.Task).Text("好").Finish(Microsoft.Extensions.AI.ChatFinishReason.Stop));
        var run = await _h.StartAsync(conversation.Id);
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail(OwnOnly);

        // Act
        var read = await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);
        var stillThere = await ConversationCountAsync();
        gate.SetResult();
        await AssistantHarness.ReadAllAsync(run);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That(stillThere, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Sweep_DeletesConversationsNotUpdatedFor180DaysWithTheirMessages()
    {
        // Arrange
        var now = _h.Clock.GetUtcNow();
        var old = await SeedAsync(now - AiRules.ConversationRetention - TimeSpan.FromMinutes(1));
        var fresh = await SeedAsync(now - AiRules.ConversationRetention + TimeSpan.FromMinutes(1));

        // Act
        int removed;
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            removed = await ConversationRetention.SweepAsync(db, now, CancellationToken.None);
        }

        // Assert
        await using var check = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(await check.Conversations.Select(c => c.Id).ToListAsync(), Is.EqualTo(new[] { fresh }));
            Assert.That(await check.Messages.CountAsync(m => m.ConversationId == old), Is.Zero);
            Assert.That(await check.Messages.CountAsync(m => m.ConversationId == fresh), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Sweep_MoreThanOneBatch_KeepsGoingUntilNothingIsLeft()
    {
        // Arrange
        var now = _h.Clock.GetUtcNow();
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(now - AiRules.ConversationRetention - TimeSpan.FromDays(i + 1));
        }

        // Act
        int removed;
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            removed = await ConversationRetention.SweepAsync(db, now, CancellationToken.None, batchSize: 2);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.EqualTo(5));
            Assert.That(await ConversationCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Sweep_DoesNotCountOrDeleteTheUsageRows()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        await SendAsync(conversation.Id);
        _h.Clock.Advance(AiRules.ConversationRetention + TimeSpan.FromDays(1));

        // Act
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            await ConversationRetention.SweepAsync(db, _h.Clock.GetUtcNow(), CancellationToken.None);
        }

        // Assert
        await using var check = await _h.Host.CreateDbContextAsync();
        Assert.That(await check.ConnectionUsage.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task UsageSweep_DeletesRowsCreatedMoreThan400DaysAgoAndKeepsTheRest()
    {
        // Arrange
        var now = _h.Clock.GetUtcNow();
        await using (var seed = await _h.Host.CreateDbContextAsync())
        {
            seed.ConnectionUsage.AddRange(
                UsageRow(now - TimeSpan.FromDays(401)),
                UsageRow(now - TimeSpan.FromDays(399)),
                UsageRow(now));
            await seed.SaveChangesAsync();
        }

        // Act
        int removed;
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            removed = await ConnectionUsageRetention.SweepAsync(db, now, CancellationToken.None);
        }

        // Assert
        await using var check = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(await check.ConnectionUsage.CountAsync(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Maintenance_SweepAlsoClearsExpiredUsage()
    {
        // Arrange
        var now = _h.Clock.GetUtcNow();
        await using (var seed = await _h.Host.CreateDbContextAsync())
        {
            seed.ConnectionUsage.Add(UsageRow(now - TimeSpan.FromDays(500)));
            await seed.SaveChangesAsync();
        }

        // Act
        var result = await _h.Host.Get<IAiMaintenance>().SweepAsync(CancellationToken.None);

        // Assert
        Assert.That(result.ConnectionUsageRows, Is.EqualTo(1));
    }

    private AiConnectionUsageRow UsageRow(DateTimeOffset createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = _h.Actor.OrganizationId,
        ConnectionId = Guid.NewGuid(),
        PersonId = _h.Actor.PersonId,
        ProjectKey = "WEB",
        State = "settled",
        CreatedAt = createdAt,
    };

    private async Task SendAsync(Guid conversationId)
    {
        _h.Script.Reply("好");
        await _h.SendAsync(conversationId);
    }

    private async Task<int> ConversationCountAsync()
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.Conversations.CountAsync();
    }

    private async Task<int> MessageCountAsync()
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.Messages.CountAsync();
    }

    private async Task<Guid> SeedAsync(DateTimeOffset updatedAt)
    {
        var id = Guid.CreateVersion7();
        await using var db = await _h.Host.CreateDbContextAsync();
        db.Conversations.Add(new AiConversationRow
        {
            Id = id,
            OrganizationId = _h.OrganizationId,
            ProjectId = WorkSamples.ProjectId,
            PersonId = _h.Actor.PersonId,
            MessageCount = 1,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
        });
        db.Messages.Add(new AiMessageRow
        {
            Id = Guid.CreateVersion7(),
            ConversationId = id,
            OrganizationId = _h.OrganizationId,
            Sequence = 1,
            Role = "user",
            Parts = "[]",
            Transcript = "[]",
            CreatedAt = updatedAt,
        });
        await db.SaveChangesAsync();
        return id;
    }
}
