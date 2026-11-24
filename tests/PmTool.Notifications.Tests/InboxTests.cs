using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>收件匣：分頁、未讀、看不到的專案、標成已讀。</summary>
[TestFixture]
public sealed class InboxTests : ConsumerTestBase
{
    private static Actor As(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);

    /// <summary>讓 recipient 在項目上收到 count 則「留言」通知（先建立項目讓他關注）。</summary>
    private async Task<Guid[]> FillAsync(EventBuilder item, Guid recipient, Guid other, int count)
    {
        await HandleAsync(item.Created(recipient));
        for (var i = 0; i < count; i++)
        {
            await HandleAsync(item.Commented(other, $"留言 {i}"));
        }

        return [.. (await Host.AllNotificationsAsync()).Where(n => n.RecipientId == recipient && n.ItemId == item.ItemId).Select(n => n.Id)];
    }

    [Test]
    public async Task List_NewestFirstAcrossPages_ReturnsEverythingOnceAndTheLastPageHasNoCursor()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await FillAsync(item, me, other, 5);

        // Act
        var first = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(false, null, 2), CancellationToken.None)).Value;
        var second = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(false, first.NextCursor, 2), CancellationToken.None)).Value;
        var third = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(false, second.NextCursor, 2), CancellationToken.None)).Value;

        // Assert
        var excerpts = first.Items.Concat(second.Items).Concat(third.Items).Select(n => n.Excerpt).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(excerpts, Is.EqualTo(new[] { "留言 4", "留言 3", "留言 2", "留言 1", "留言 0" }));
            Assert.That(first.NextCursor, Is.Not.Null);
            Assert.That(third.NextCursor, Is.Null);
            Assert.That(third.Items, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task List_ExactlyOnePageOfItems_HasNoNextCursor()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await FillAsync(item, me, other, 2);

        // Act
        var page = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(false, null, 2), CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items, Has.Count.EqualTo(2));
            Assert.That(page.NextCursor, Is.Null);
        }
    }

    [Test]
    public async Task List_UnreadOnly_LeavesOutTheReadOnes()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        var ids = await FillAsync(item, me, other, 3);
        await Host.Inbox.MarkReadAsync(As(item, me), [ids[0]], CancellationToken.None);

        // Act
        var unread = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(true, null, 50), CancellationToken.None)).Value;
        var all = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(false, null, 50), CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unread.Items, Has.Count.EqualTo(2));
            Assert.That(all.Items, Has.Count.EqualTo(3));
            Assert.That(all.Items.Count(n => n.ReadAt is not null), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task List_AnItem_CarriesTheSnapshotOfTheEvent()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await HandleAsync(item.Created(me));
        await HandleAsync(item.Transitioned(other, "待辦", "審查中"));

        // Act
        var page = (await Host.Inbox.ListAsync(As(item, me), new NotificationQuery(false, null, 50), CancellationToken.None)).Value;

        // Assert
        var notification = page.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notification.Kind, Is.EqualTo(NotificationKind.StateChanged));
            Assert.That((notification.ProjectKey, notification.ItemKey, notification.ItemTitle), Is.EqualTo(("WEB", "WEB-1", "範例項目")));
            Assert.That((notification.FromStateName, notification.ToStateName), Is.EqualTo(("待辦", "審查中")));
            Assert.That(notification.ActorPersonId, Is.EqualTo(other));
            Assert.That(notification.ReadAt, Is.Null);
        }
    }

    [Test]
    public async Task ListAndCount_ProjectNoLongerReadable_AreHiddenAndNotCounted()
    {
        // Arrange：兩個專案各一則，之後被移出其中一個。
        var (kept, me, other, _, _) = NewItem();
        var lost = new EventBuilder(kept.OrganizationId, Guid.CreateVersion7(), Guid.CreateVersion7());
        Host.Access.Allow(lost.ProjectId, me, other);
        await FillAsync(kept, me, other, 1);
        await FillAsync(lost, me, other, 1);
        Host.Access.Revoke(lost.ProjectId, me);

        // Act
        var page = (await Host.Inbox.ListAsync(As(kept, me), new NotificationQuery(false, null, 50), CancellationToken.None)).Value;
        var count = await Host.Inbox.CountUnreadAsync(As(kept, me), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(n => n.ItemKey).Distinct(), Is.EqualTo(new[] { "WEB-1" }));
            Assert.That(page.Items, Has.Count.EqualTo(1));
            Assert.That(count, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task List_NothingReadable_IsEmptyWithoutTouchingTheTable()
    {
        // Arrange
        var item = EventBuilder.ForNewItem();

        // Act
        var page = (await Host.Inbox.ListAsync(As(item, Guid.CreateVersion7()), new NotificationQuery(false, null, 50), CancellationToken.None)).Value;

        // Assert
        Assert.That(page, Is.EqualTo(new NotificationPage([], null)).Using<NotificationPage>((a, b) => a.Items.Count == b.Items.Count && a.NextCursor == b.NextCursor));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(101)]
    public async Task List_LimitOutOfRange_FailsOnTheLimit(int limit)
    {
        // Arrange
        var item = EventBuilder.ForNewItem();

        // Act
        var result = await Host.Inbox.ListAsync(As(item, Guid.CreateVersion7()), new NotificationQuery(false, null, limit), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(NotificationErrorCodes.QueryInvalid));
            Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "limit" }));
        }
    }

    [TestCase("not a cursor")]
    [TestCase("bjEuMS4x")]
    [TestCase("!!!")]
    public async Task List_UnreadableCursor_FailsOnTheCursor(string cursor)
    {
        // Arrange
        var item = EventBuilder.ForNewItem();

        // Act
        var result = await Host.Inbox.ListAsync(As(item, Guid.CreateVersion7()), new NotificationQuery(false, cursor, 10), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "cursor" }));
    }

    [Test]
    public async Task MarkRead_SomeoneElsesAndUnknownIds_AreSkippedAndTheCountIsWhatChanged()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        var mine = await FillAsync(item, me, other, 2);
        await HandleAsync(item.Commented(me, "給別人"));
        var theirs = (await Host.AllNotificationsAsync()).Single(n => n.RecipientId == other && n.ItemId == item.ItemId);

        // Act
        var updated = await Host.Inbox.MarkReadAsync(As(item, me), [mine[0], theirs.Id, Guid.NewGuid(), mine[0]], CancellationToken.None);
        var again = await Host.Inbox.MarkReadAsync(As(item, me), [mine[0]], CancellationToken.None);

        // Assert
        var theirsAfter = (await Host.AllNotificationsAsync()).Single(n => n.Id == theirs.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.EqualTo(1));
            Assert.That(again, Is.Zero);
            Assert.That(theirsAfter.ReadAt, Is.Null);
            Assert.That(await Host.Inbox.CountUnreadAsync(As(item, me), CancellationToken.None), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task MarkAllRead_MarksOnlyMyReadableOnes()
    {
        // Arrange
        var (kept, me, other, _, _) = NewItem();
        var lost = new EventBuilder(kept.OrganizationId, Guid.CreateVersion7(), Guid.CreateVersion7());
        Host.Access.Allow(lost.ProjectId, me, other);
        await FillAsync(kept, me, other, 2);
        await FillAsync(lost, me, other, 1);
        Host.Access.Revoke(lost.ProjectId, me);
        var otherUnreadBefore = await Host.Inbox.CountUnreadAsync(As(kept, other), CancellationToken.None);

        // Act
        var updated = await Host.Inbox.MarkAllReadAsync(As(kept, me), CancellationToken.None);

        // Assert：看不到的那個專案的通知沒動，重新看得到時還是未讀。
        Host.Access.Allow(lost.ProjectId, me);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.EqualTo(2));
            Assert.That(await Host.Inbox.CountUnreadAsync(As(kept, me), CancellationToken.None), Is.EqualTo(1));
            Assert.That(await Host.Inbox.CountUnreadAsync(As(kept, other), CancellationToken.None), Is.EqualTo(otherUnreadBefore));
        }
    }

    [Test]
    public async Task MarkRead_EmptyList_DoesNothing()
    {
        // Arrange
        var item = EventBuilder.ForNewItem();

        // Act
        var updated = await Host.Inbox.MarkReadAsync(As(item, Guid.CreateVersion7()), [], CancellationToken.None);

        // Assert
        Assert.That(updated, Is.Zero);
    }

    [Test]
    public async Task Count_OtherOrganizationsPeople_AreNotMixedIn()
    {
        // Arrange：同一個人編號在另一個組織看不到這些通知。
        var (item, me, other, _, _) = NewItem();
        await FillAsync(item, me, other, 1);

        // Act
        var elsewhere = await Host.Inbox.CountUnreadAsync(new Actor(Guid.CreateVersion7(), me, false), CancellationToken.None);

        // Assert
        Assert.That(elsewhere, Is.Zero);
    }
}
