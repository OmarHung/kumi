using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>留言的編輯、刪除與復原（item_comment_edited、item_comment_deleted、item_comment_restored）：通知跟著留言走。</summary>
[TestFixture]
public sealed class CommentEditTests : ConsumerTestBase
{
    private static Actor As(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);

    [Test]
    public async Task CommentEdited_ReplacesTheExcerptOfTheExistingNotifications()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "舊內容"));

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "新內容"));

        // Assert
        var mine = (await Host.AllNotificationsAsync()).Where(n => n.CommentId == commentId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mine, Has.Count.EqualTo(1));
            Assert.That(mine[0].RecipientId, Is.EqualTo(creator));
            Assert.That(mine[0].Excerpt, Is.EqualTo("新內容"));
        }
    }

    [Test]
    public async Task CommentEdited_AlreadyReadNotification_AlsoGetsTheNewExcerpt()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "舊內容"));
        await Host.Inbox.MarkAllReadAsync(As(item, creator), CancellationToken.None);

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "新內容"));

        // Assert
        var row = (await Host.AllNotificationsAsync()).Single(n => n.CommentId == commentId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Excerpt, Is.EqualTo("新內容"));
            Assert.That(row.ReadAt, Is.Not.Null, "編輯不把已讀的改回未讀");
        }
    }

    [Test]
    public async Task CommentEdited_OnlyTouchesNotificationsOfThatComment()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        var edited = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(edited, author, "要改的"));
        await HandleAsync(item.CommentedAs(other, author, "不相干的"));

        // Act
        await HandleAsync(item.CommentEdited(edited, author, "改好了"));

        // Assert
        var excerpts = (await Host.AllNotificationsAsync()).Where(n => n.RecipientId == creator).ToDictionary(n => n.CommentId!.Value, n => n.Excerpt);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(excerpts[edited], Is.EqualTo("改好了"));
            Assert.That(excerpts[other], Is.EqualTo("不相干的"));
        }
    }

    [Test]
    public async Task CommentEdited_NewMention_NotifiesOnlyTheMentionedAndCarriesTheCommentId()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "舊內容"));
        var commentedBefore = await Host.KindsForAsync(creator);

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "新內容 @某人", mentioned));

        // Assert
        var mentionNotice = (await Host.AllNotificationsAsync()).Single(n => n.RecipientId == mentioned);
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mentionNotice.Kind, Is.EqualTo(NotificationKind.Mentioned));
            Assert.That(mentionNotice.CommentId, Is.EqualTo(commentId));
            Assert.That(mentionNotice.Excerpt, Is.EqualTo("新內容 @某人"));
            Assert.That(await Host.KindsForAsync(creator), Is.EqualTo(commentedBefore), "不再發留言通知給關注的人");
            Assert.That(subscriptions, Does.Not.Contain(mentioned), "編輯不因提及加關注");
        }
    }

    [Test]
    public async Task CommentEdited_WithoutNewMentions_CreatesNoNotification()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "舊內容"));
        var before = (await Host.AllNotificationsAsync()).Count;

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "新內容"));

        // Assert
        Assert.That((await Host.AllNotificationsAsync()).Count, Is.EqualTo(before));
    }

    [Test]
    public async Task CommentEdited_ByHistoryImport_NotifiesNobody()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        var edited = item.CommentEdited(commentId, author, "新內容", mentioned);
        var history = edited with { Item = edited.Item with { History = true } };

        // Act
        await HandleAsync(history);

        // Assert
        Assert.That((await Host.AllNotificationsAsync()).Where(n => n.RecipientId == mentioned), Is.Empty);
    }

    [Test]
    public async Task CommentDeleted_RemovesAllNotificationsOfThatCommentAndUnreadCountDrops()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        var kept = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(kept, author, "留著", mentioned));
        await HandleAsync(item.CommentedAs(commentId, author, "要刪的", mentioned));
        var unreadBefore = await Host.Inbox.CountUnreadAsync(As(item, creator), CancellationToken.None);

        // Act
        await HandleAsync(item.CommentDeleted(commentId, author));

        // Assert
        var page = (await Host.Inbox.ListAsync(As(item, mentioned), new NotificationQuery(false, null, 20), CancellationToken.None)).Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Host.AllNotificationsAsync()).Where(n => n.CommentId == commentId), Is.Empty);
            Assert.That(await Host.Inbox.CountUnreadAsync(As(item, creator), CancellationToken.None), Is.EqualTo(unreadBefore - 1));
            Assert.That(page.Items.Select(n => n.Excerpt), Is.EqualTo(new[] { "留著" }));
            Assert.That((await Host.AllNotificationsAsync()).Count(n => n.CommentId == kept), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task CommentDeleted_AlreadyReadNotification_IsRemovedToo()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "要刪的"));
        await Host.Inbox.MarkAllReadAsync(As(item, creator), CancellationToken.None);

        // Act
        await HandleAsync(item.CommentDeleted(commentId, author));

        // Assert
        Assert.That((await Host.AllNotificationsAsync()).Where(n => n.CommentId == commentId), Is.Empty);
    }

    [Test]
    public async Task CommentRestored_DoesNotBringBackNotificationsNorNotifyAgain()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "留言", mentioned));
        await HandleAsync(item.CommentDeleted(commentId, author));
        var restored = item.CommentRestored(commentId, author);

        // Act
        await HandleAsync(restored);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Host.AllNotificationsAsync()).Where(n => n.CommentId == commentId), Is.Empty);
            Assert.That(Host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning && e.Message.Contains(restored.EventId.ToString())), Is.Empty);
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(restored.Position));
        }
    }

    [Test]
    public async Task CommentEventsWithoutActivityId_AreSkippedWithAWarning()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "舊內容"));
        var before = (await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId).ToList();
        var last = item.CommentDeleted(commentId, author);

        // Act
        await HandleAsync(
            EventBuilder.Legacy(item.CommentEdited(commentId, author, "新內容", mentioned)),
            EventBuilder.Legacy(item.CommentDeleted(commentId, author)),
            EventBuilder.Legacy(last));

        // Assert
        var after = (await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.Select(n => (n.Id, n.Excerpt)), Is.EquivalentTo(before.Select(n => (n.Id, n.Excerpt))));
            Assert.That(Host.Logs.Entries.Count(e => e.Level == LogLevel.Warning), Is.GreaterThanOrEqualTo(3));
        }
    }
}
