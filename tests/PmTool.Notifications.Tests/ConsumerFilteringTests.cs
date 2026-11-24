using Microsoft.Extensions.DependencyInjection;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>取消關注、看不到專案的人、重複的事件與同時處理。</summary>
[TestFixture]
public sealed class ConsumerFilteringTests : ConsumerTestBase
{
    [Test]
    public async Task Unsubscribed_NoMoreCommentedOrStateChangedNotifications()
    {
        // Arrange：建立者關注了項目，然後取消。
        var (item, creator, other, _, _) = NewItem();
        await HandleAsync(item.Created(creator));
        await Host.Subscriptions.SetAsync(Actor(item, creator), item.ProjectId, item.ItemId, false, CancellationToken.None);

        // Act
        await HandleAsync(item.Commented(other, "留言"), item.Transitioned(other));

        // Assert
        Assert.That(await Host.KindsForAsync(creator), Is.Empty);
    }

    [Test]
    public async Task Unsubscribed_CommentingAgain_DoesNotFollowAgain()
    {
        // Arrange
        var (item, creator, other, _, _) = NewItem();
        await HandleAsync(item.Created(creator));
        await Host.Subscriptions.SetAsync(Actor(item, creator), item.ProjectId, item.ItemId, false, CancellationToken.None);

        // Act
        await HandleAsync(item.Commented(creator, "我又留言了"), item.Transitioned(other));

        // Assert
        var subscription = await Host.Subscriptions.GetAsync(Actor(item, creator), item.ProjectId, item.ItemId, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subscription.Following, Is.False);
            Assert.That(await Host.KindsForAsync(creator), Is.Empty);
        }
    }

    [Test]
    public async Task Unsubscribed_StillGetsAssignedAndMentioned()
    {
        // Arrange
        var (item, creator, other, _, _) = NewItem();
        await HandleAsync(item.Created(creator));
        await Host.Subscriptions.SetAsync(Actor(item, other), item.ProjectId, item.ItemId, false, CancellationToken.None);

        // Act：被點名不是關注。
        await HandleAsync(item.Updated(creator, other, assigneeChanged: true), item.Commented(creator, "@某人", other));

        // Assert
        Assert.That(await Host.KindsForAsync(other), Is.EqualTo(new[] { NotificationKind.Assigned, NotificationKind.Mentioned }));
    }

    [Test]
    public async Task NeverFollowedButUnsubscribed_IsNotFollowedByLaterAutomaticRules()
    {
        // Arrange：還沒有關注就先按了取消。
        var (item, creator, other, _, _) = NewItem();
        await Host.Subscriptions.SetAsync(Actor(item, other), item.ProjectId, item.ItemId, false, CancellationToken.None);

        // Act
        await HandleAsync(item.Created(creator, other));

        // Assert
        var subscription = await Host.Subscriptions.GetAsync(Actor(item, other), item.ProjectId, item.ItemId, CancellationToken.None);
        Assert.That(subscription.Following, Is.False);
    }

    [Test]
    public async Task Recipient_WhoCanNoLongerSeeTheProject_GetsNothingAndIsNotFollowed()
    {
        // Arrange
        var (item, creator, removed, _, _) = NewItem();
        Host.Access.Revoke(item.ProjectId, removed);

        // Act
        await HandleAsync(item.Created(creator, removed));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(removed), Is.Empty);
            Assert.That((await Host.AllSubscriptionsAsync()).Where(s => s.PersonId == removed), Is.Empty);
        }
    }

    [Test]
    public async Task Handle_SameBatchTwice_ProducesOneNotification()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        var created = item.Created(creator, assignee);

        // Act
        await HandleAsync(created);
        await HandleAsync(created);

        // Assert
        Assert.That(await Host.KindsForAsync(assignee), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Handle_SameEventRedeliveredAtANewPosition_ProducesOneNotification()
    {
        // Arrange：位置重設了（例如事件流重建），同一個事件編號再來一次，靠唯一限制擋下。
        // 位置往前跳會影響同一個資料庫裡的其他測試，所以用自己的資料庫。
        await using var host = await NotificationsTestHost.StartAsync();
        var item = EventBuilder.ForNewItem();
        Guid[] people = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        host.Access.Allow(item.ProjectId, people);
        var created = item.Created(people[0], people[1]);
        await host.Consumer.HandleAsync([created], CancellationToken.None);

        // Act
        await host.Consumer.HandleAsync(
            [created with { Position = new WorkEventPosition(created.Position.TransactionId + 100, 1) }], CancellationToken.None);

        // Assert
        Assert.That(await host.KindsForAsync(people[1]), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Handle_TwoReadersAtTheSameTime_ProduceOneSetOfNotifications()
    {
        // Arrange：兩個程序拿到同一批事件。
        var (item, creator, assignee, mentioned, _) = NewItem();
        WorkEvent[] batch = [item.Created(creator, assignee, mentioned), item.Commented(creator, "留言", mentioned)];
        using var secondScope = Host.CreateScope();
        var second = secondScope.ServiceProvider.GetServices<IWorkEventConsumer>().Single();

        // Act
        await Task.WhenAll(
            Host.Consumer.HandleAsync(batch, CancellationToken.None),
            second.HandleAsync(batch, CancellationToken.None));

        // Assert
        var mine = (await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mine.Where(n => n.RecipientId == assignee).Select(n => n.Kind),
                Is.EquivalentTo(new[] { NotificationKind.Assigned, NotificationKind.Commented }));
            Assert.That(mine.Count(n => n.RecipientId == mentioned && n.Kind == NotificationKind.Mentioned), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Handle_Events_RecordsThePositionOfTheLastOne()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        var created = item.Created(creator, assignee);
        var commented = item.Commented(creator, "留言");

        // Act：事件不照順序給也行，讀者自己排。
        await HandleAsync(commented, created);

        // Assert
        Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(commented.Position));
    }

    [Test]
    public async Task Handle_EventsAtOrBeforeThePosition_AreSkipped()
    {
        // Arrange：位置已經記到比它更後面（先造後面的事件、先處理，再處理前面的）。
        var (item, creator, assignee, _, _) = NewItem();
        var earlier = item.Created(creator, assignee);
        var later = item.Created(creator);
        await HandleAsync(later);

        // Act
        await HandleAsync(earlier);

        // Assert
        Assert.That(await Host.KindsForAsync(assignee), Is.Empty);
    }

    private static PmTool.Abstractions.Actor Actor(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);
}
