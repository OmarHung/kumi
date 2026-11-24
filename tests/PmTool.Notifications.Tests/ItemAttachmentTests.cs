using Microsoft.Extensions.Logging;

namespace PmTool.Notifications.Tests;

/// <summary>附件的新增與刪除（item_attachment_added、item_attachment_removed）：明確處理、什麼都不發，不當成不認得的事件。</summary>
[TestFixture]
public sealed class ItemAttachmentTests : ConsumerTestBase
{
    [Test]
    public async Task AttachmentAdded_NotifiesNobodyAndDoesNotChangeFollows()
    {
        // Arrange
        var (item, creator, assignee, uploader, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee));
        var notificationsBefore = (await Host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId);
        var attachmentAdded = item.AttachmentAdded(uploader, creator, assignee);

        // Act
        await HandleAsync(attachmentAdded);

        // Assert
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId), Is.EqualTo(notificationsBefore));
            Assert.That(subscriptions, Is.EquivalentTo(new[] { creator, assignee }), "上傳的人不會因此關注");
            Assert.That(Host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning), Is.Empty);
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(attachmentAdded.Position));
        }
    }

    [Test]
    public async Task AttachmentRemoved_NotifiesNobodyAndKeepsTheFollows()
    {
        // Arrange
        var (item, creator, assignee, remover, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee));
        var notificationsBefore = (await Host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId);
        var attachmentRemoved = item.AttachmentRemoved(remover, creator, assignee);

        // Act
        await HandleAsync(attachmentRemoved);

        // Assert
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId), Is.EqualTo(notificationsBefore));
            Assert.That(subscriptions, Is.EquivalentTo(new[] { creator, assignee }));
            Assert.That(Host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning), Is.Empty);
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(attachmentRemoved.Position));
        }
    }
}
