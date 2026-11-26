using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>維護：對話刪掉之後的孤兒與過期的待送，先刪檔再刪資料列；送出的、還沒過期的不動。</summary>
[TestFixture]
public sealed class AttachmentMaintenanceTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private IAiMaintenance Maintenance => _h.Host.Get<IAiMaintenance>();

    private async Task<(Guid ConversationId, AssistantAttachment Attachment)> SendOneAsync(string fileName = "圖.png")
    {
        var attachment = await _h.UploadAsync(fileName, Image());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        await _h.SendWithAsync(conversation.Id, "看", attachment);
        return (conversation.Id, attachment);
    }

    [Test]
    public async Task DeletingAConversation_LeavesTheAttachmentAsAnOrphanUntilTheSweepRemovesFileThenRow()
    {
        // Arrange
        var (conversationId, attachment) = await SendOneAsync();
        var kept = await SendOneAsync("留著.png");

        // Act
        await _h.Service.DeleteAsync(_h.Actor, "WEB", conversationId, CancellationToken.None);
        await using var before = await _h.Host.CreateDbContextAsync();
        var orphan = await before.Attachments.AsNoTracking().SingleAsync(a => a.Id == attachment.Id);
        var result = await Maintenance.SweepAsync(CancellationToken.None);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((orphan.MessageId, orphan.ConversationId), Is.EqualTo(((Guid?)null, (Guid?)null)));
            Assert.That(orphan.BoundAt, Is.Not.Null);
            Assert.That(result.Attachments, Is.EqualTo(1));
            Assert.That(_h.Store().Contains(attachment.Id), Is.False);
            Assert.That(await db.Attachments.AnyAsync(a => a.Id == attachment.Id), Is.False);
            Assert.That(_h.Store().Contains(kept.Attachment.Id), Is.True, "還在對話裡的不動");
            Assert.That(await db.Attachments.AnyAsync(a => a.Id == kept.Attachment.Id), Is.True);
        }
    }

    [Test]
    public async Task Sweep_PendingAttachmentsOlderThanTheLifetime_AreRemovedButFreshOnesAreKept()
    {
        // Arrange
        var stale = await _h.UploadAsync("舊.png", Image());
        _h.Clock.Advance(AssistantAttachmentRules.PendingLifetime + TimeSpan.FromMinutes(10));
        var fresh = await _h.UploadAsync("新.png", Image());

        // Act
        var result = await Maintenance.SweepAsync(CancellationToken.None);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Attachments, Is.EqualTo(1));
            Assert.That(_h.Store().Contains(stale.Id), Is.False);
            Assert.That(_h.Store().Contains(fresh.Id), Is.True);
            Assert.That(await db.Attachments.Select(a => a.Id).ToListAsync(), Is.EqualTo(new[] { fresh.Id }));
        }
    }

    [Test]
    public async Task Sweep_JustPastTheLifetimeButInsideTheGrace_KeepsThePending()
    {
        // Arrange
        var attachment = await _h.UploadAsync("a.png", Image());
        _h.Clock.Advance(AssistantAttachmentRules.PendingLifetime + TimeSpan.FromMinutes(1));

        // Act
        var result = await Maintenance.SweepAsync(CancellationToken.None);

        // Assert
        Assert.That((result.Attachments, _h.Store().Contains(attachment.Id)), Is.EqualTo((0, true)));
    }

    [Test]
    public async Task Sweep_WhenTheFileCannotBeDeleted_KeepsTheRowAndRetriesNextTime()
    {
        // Arrange
        var (conversationId, attachment) = await SendOneAsync();
        await _h.Service.DeleteAsync(_h.Actor, "WEB", conversationId, CancellationToken.None);
        _h.Store().FailDeletes = true;

        // Act
        var failed = await Maintenance.SweepAsync(CancellationToken.None);
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            Assert.That(await db.Attachments.AnyAsync(a => a.Id == attachment.Id), Is.True, "檔案還在，容量不能先放掉");
        }

        _h.Store().FailDeletes = false;
        var retried = await Maintenance.SweepAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Attachments, Is.Zero);
            Assert.That(retried.Attachments, Is.EqualTo(1));
            Assert.That(_h.Store().Contains(attachment.Id), Is.False);
        }
    }

    [Test]
    public async Task Sweep_ConversationsExpiredByRetentionLoseTheirAttachmentsInTheSameRound()
    {
        // Arrange
        var (_, attachment) = await SendOneAsync();
        _h.Clock.Advance(AiRules.ConversationRetention + TimeSpan.FromDays(1));

        // Act
        var result = await Maintenance.SweepAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Conversations, result.Attachments), Is.EqualTo((1, 1)));
            Assert.That(_h.Store().Contains(attachment.Id), Is.False);
        }
    }

    [Test]
    public async Task Sweep_WithNothingToDo_ReportsZero()
    {
        // Arrange
        await SendOneAsync();

        // Act
        var result = await Maintenance.SweepAsync(CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(new AiMaintenanceResult(0, 0, 0, 0)));
    }
}
