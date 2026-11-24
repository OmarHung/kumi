using Microsoft.Extensions.Logging;

namespace PmTool.Notifications.Tests;

/// <summary>復原刪除的項目（item_restored）：不通知任何人，把建立的人與目前的負責人加回關注，不當成不認得的事件。</summary>
[TestFixture]
public sealed class ItemRestoredTests : ConsumerTestBase
{
    [Test]
    public async Task Restored_AfterDeletion_FollowsCreatorAndAssigneeAgainAndNotifiesNobody()
    {
        // Arrange：建立、刪除（關注清空），再由另一個人復原。
        var (item, creator, assignee, restorer, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee), item.Deleted(restorer));
        var notificationsBefore = (await Host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId);

        // Act
        await HandleAsync(item.Restored(restorer, creator, assignee));

        // Assert
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).ToDictionary(s => s.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subscriptions.Keys, Is.EquivalentTo(new[] { creator, assignee }), "做復原的人不會因此關注");
            Assert.That(subscriptions[creator].Reason, Is.EqualTo(SubscriptionReason.Creator));
            Assert.That(subscriptions[assignee].Reason, Is.EqualTo(SubscriptionReason.Assignee));
            Assert.That((await Host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId), Is.EqualTo(notificationsBefore));
        }
    }

    [Test]
    public async Task Restored_WithoutAnAssignee_FollowsOnlyTheCreator()
    {
        // Arrange
        var (item, creator, restorer, _, _) = NewItem();
        await HandleAsync(item.Created(creator), item.Deleted(restorer));

        // Act
        await HandleAsync(item.Restored(restorer, creator));

        // Assert
        var followers = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId);
        Assert.That(followers, Is.EqualTo(new[] { creator }));
    }

    [Test]
    public async Task Restored_PersonWhoIsNoLongerAProjectMember_IsNotFollowed()
    {
        // Arrange：建立的人已經不是專案成員。
        var (item, _, restorer, _, _) = NewItem();
        var formerMember = Guid.CreateVersion7();
        await HandleAsync(item.Deleted(restorer));

        // Act
        await HandleAsync(item.Restored(restorer, formerMember));

        // Assert
        Assert.That((await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId), Is.Empty);
    }

    [Test]
    public async Task Restored_IsHandledAsAKnownKindWithoutAWarning()
    {
        // Arrange
        var (item, creator, restorer, _, _) = NewItem();
        var restored = item.Restored(restorer, creator);

        // Act
        await HandleAsync(restored);

        // Assert
        Assert.That(Host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning), Is.Empty);
        Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(restored.Position));
    }
}
