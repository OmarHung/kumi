using Microsoft.Extensions.Logging;
using PmTool.Notifications.Events;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 直接交給 EventApplier 的留言事件沒有 ActivityId（consumer 平常會先擋掉）：
/// 沒有防的話 EF 會翻成 comment_id IS NULL，把整個組織沒有留言編號的通知刪光或改光。
/// </summary>
[TestFixture]
public sealed class EventApplierNullActivityTests : ConsumerTestBase
{
    [Test]
    public async Task ApplyAsync_DeletedEventWithoutActivityId_KeepsEveryNotificationOfTheOrganization()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee));
        var before = (await Host.AllNotificationsAsync()).Where(n => n.OrganizationId == item.OrganizationId).ToList();

        // Act
        var notified = await ApplyDirectlyAsync(EventBuilder.Legacy(item.CommentDeleted(Guid.CreateVersion7(), creator)));

        // Assert
        var after = (await Host.AllNotificationsAsync()).Where(n => n.OrganizationId == item.OrganizationId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.Not.Empty);
            Assert.That(after.Select(n => n.Id), Is.EquivalentTo(before.Select(n => n.Id)));
            Assert.That(notified, Is.Empty);
        }
    }

    [Test]
    public async Task ApplyAsync_EditedEventWithoutActivityId_LeavesTheExcerptsOfOtherNotificationsAlone()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee));
        var before = (await Host.AllNotificationsAsync()).Where(n => n.OrganizationId == item.OrganizationId).ToList();

        // Act
        await ApplyDirectlyAsync(EventBuilder.Legacy(item.CommentEdited(Guid.CreateVersion7(), creator, "不該蓋掉別人")));

        // Assert
        var after = (await Host.AllNotificationsAsync()).Where(n => n.OrganizationId == item.OrganizationId).ToList();
        Assert.That(after.Select(n => (n.Id, n.Excerpt)), Is.EquivalentTo(before.Select(n => (n.Id, n.Excerpt))));
    }

    private async Task<IReadOnlyCollection<NotifiedRecipient>> ApplyDirectlyAsync(PmTool.Work.WorkEvent workEvent)
    {
        await using var db = await Host.CreateDbContextAsync();
        var applier = new EventApplier(db, new ReaderCache(Host.Access), []);
        await applier.ApplyAsync(workEvent, CancellationToken.None);
        return applier.NotifiedRecipients;
    }
}
