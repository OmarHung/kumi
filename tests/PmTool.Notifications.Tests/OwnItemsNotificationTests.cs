using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>只看自己的項目：通知、自動關注、收件匣與手動關注都只給看得到那個項目的人。</summary>
[TestFixture]
public sealed class OwnItemsNotificationTests : ConsumerTestBase
{
    private static Actor As(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);

    [Test]
    public async Task Mention_OwnOnlyMemberWhoIsNotOwnerOfTheItem_GetsNothingAndDoesNotFollow()
    {
        // Arrange：outsider 只看自己的，項目是 author 建的、沒有負責人；full 看得到全部。
        var (item, author, outsider, full, _) = NewItem();
        Host.Access.AllowOwnOnly(item.ProjectId, outsider);

        // Act
        await HandleAsync(item.Commented(author, "請看", outsider, full));

        // Assert
        var notified = (await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId).Select(n => n.RecipientId).ToList();
        var followers = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notified, Is.EqualTo(new[] { full }));
            Assert.That(followers, Does.Not.Contain(outsider));
        }
    }

    [Test]
    public async Task Assigned_OwnOnlyMember_GetsTheNotificationBecauseTheItemIsTheirs()
    {
        // Arrange
        var (item, author, outsider, _, _) = NewItem();
        Host.Access.AllowOwnOnly(item.ProjectId, outsider);

        // Act
        await HandleAsync(item.Created(author, outsider));

        // Assert
        var notifications = (await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId).ToList();
        Assert.That(notifications.Select(n => (n.RecipientId, n.Kind)), Is.EqualTo(new[] { (outsider, NotificationKind.Assigned) }));
    }

    [Test]
    public async Task StateChange_FollowerWhoLostTheItem_IsNotNotifiedButOtherFollowersAre()
    {
        // Arrange：outsider 先是負責人（自動關注），之後項目換給別人，他只看自己的、已經看不到。
        var (item, author, outsider, full, _) = NewItem();
        Host.Access.AllowOwnOnly(item.ProjectId, outsider);
        await HandleAsync(item.Created(author, outsider));
        await Host.Subscriptions.SetAsync(As(item, full), item.ProjectId, item.ItemId, true, CancellationToken.None);

        // Act：改狀態的事件裡，項目的建立者是 author、沒有負責人。
        await HandleAsync(item.Transitioned(author));

        // Assert
        var stateChanged = (await Host.AllNotificationsAsync())
            .Where(n => n.ItemId == item.ItemId && n.Kind == NotificationKind.StateChanged)
            .Select(n => n.RecipientId);
        Assert.That(stateChanged, Is.EqualTo(new[] { full }));
    }

    [Test]
    public async Task Inbox_ItemTheyCanNoLongerSee_IsLeftOutOfTheListAndTheUnreadCount()
    {
        // Arrange：同一個人在兩個項目上都有通知，其中一個項目之後看不到了。
        var (visible, me, other, _, _) = NewItem();
        var hiddenItem = new EventBuilder(visible.OrganizationId, visible.ProjectId, Guid.CreateVersion7());
        Host.Access.AllowOwnOnly(visible.ProjectId, me);
        await HandleAsync(visible.Created(other, me), hiddenItem.Created(other, me));
        var before = (await Host.Inbox.ListAsync(As(visible, me), new NotificationQuery(false, null, 10), CancellationToken.None)).Value;
        var unreadBefore = await Host.Inbox.CountUnreadAsync(As(visible, me), CancellationToken.None);
        Host.Access.HideItem(me, hiddenItem.ItemId);

        // Act
        var page = (await Host.Inbox.ListAsync(As(visible, me), new NotificationQuery(false, null, 10), CancellationToken.None)).Value;
        var unread = await Host.Inbox.CountUnreadAsync(As(visible, me), CancellationToken.None);
        var marked = await Host.Inbox.MarkAllReadAsync(As(visible, me), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Items, Has.Count.EqualTo(2));
            Assert.That(unreadBefore, Is.EqualTo(2));
            Assert.That(page.Items, Has.Count.EqualTo(1));
            Assert.That(unread, Is.EqualTo(1));
            Assert.That(marked, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Inbox_MemberWhoReadsEverything_KeepsNotificationsOfItemsTheEngineNoLongerResolves()
    {
        // Arrange：一般成員（不是只看自己的）的通知，項目之後被刪除；工作引擎對已刪除的項目回「看不到」，收件匣照舊留著。
        var (item, me, other, _, _) = NewItem();
        await HandleAsync(item.Created(other, me));
        Host.Access.HideItem(me, item.ItemId);

        // Act
        var page = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(false, null, 10), CancellationToken.None)).Value;
        var unread = await Host.Inbox.CountUnreadAsync(As(item, me), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items, Has.Count.EqualTo(1));
            Assert.That(unread, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Inbox_OwnOnlyMember_DoesNotSeeNotificationsOfOthersItemsAndCountsAgree()
    {
        // Arrange：只看自己的人有一則通知指到別人的項目（例如先前被提及、後來看不到了）。
        var (item, me, other, _, _) = NewItem();
        Host.Access.AllowOwnOnly(item.ProjectId, me);
        await HandleAsync(item.Created(other, me));
        Host.Access.HideItem(me, item.ItemId);

        // Act
        var page = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(false, null, 10), CancellationToken.None)).Value;
        var unread = await Host.Inbox.CountUnreadAsync(As(item, me), CancellationToken.None);
        var unreadOnly = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(true, null, 10), CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items, Is.Empty);
            Assert.That(unreadOnly.Items, Is.Empty);
            Assert.That(unread, Is.Zero);
        }
    }

    [Test]
    public async Task Subscription_ItemTheyCannotSee_CannotBeFollowed()
    {
        // Arrange
        var (item, me, _, _, _) = NewItem();
        Host.Access.HideItem(me, item.ItemId);

        // Act
        var get = await Host.Subscriptions.GetAsync(As(item, me), item.ProjectId, item.ItemId, CancellationToken.None);
        var set = await Host.Subscriptions.SetAsync(As(item, me), item.ProjectId, item.ItemId, true, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(get, Is.EqualTo(new ItemSubscription(false, null, false)));
            Assert.That(set.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(set.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
        }
    }

    [Test]
    public async Task Subscription_FollowedItemTheyCanNoLongerSee_IsReportedAsNotFollowed()
    {
        // Arrange：先關注，之後項目換了負責人、他只看自己的就看不到了。
        var (item, me, _, _, _) = NewItem();
        await Host.Subscriptions.SetAsync(As(item, me), item.ProjectId, item.ItemId, true, CancellationToken.None);
        Host.Access.HideItem(me, item.ItemId);

        // Act
        var get = await Host.Subscriptions.GetAsync(As(item, me), item.ProjectId, item.ItemId, CancellationToken.None);

        // Assert
        Assert.That(get, Is.EqualTo(new ItemSubscription(false, null, false)));
    }
}
