using PmTool.Abstractions;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>同一則留言、同一個項目的說明，對同一個人反覆提及時，收件匣裡只留一則「提及了你」。</summary>
[TestFixture]
public sealed class MentionDedupTests : ConsumerTestBase
{
    private static Actor As(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);

    private async Task<IReadOnlyList<NotificationRow>> MentionsOfAsync(Guid person) =>
        [.. (await Host.AllNotificationsAsync()).Where(n => n.RecipientId == person && n.Kind == NotificationKind.Mentioned)];

    [Test]
    public async Task CommentEdited_MentionAddedRemovedAndAddedAgain_LeavesOneMentionWithTheLatestExcerptMarkedEdited()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "第一版", mentioned));

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "第二版（拿掉提及）"));
        await HandleAsync(item.CommentEdited(commentId, author, "第三版（再提及）", mentioned));

        // Assert
        var mentions = await MentionsOfAsync(mentioned);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mentions, Has.Count.EqualTo(1));
            Assert.That(mentions[0].Excerpt, Is.EqualTo("第三版（再提及）"));
            Assert.That(mentions[0].ExcerptEditedAt, Is.Not.Null);
            Assert.That(await Host.Inbox.CountUnreadAsync(As(item, mentioned), CancellationToken.None), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task CommentEdited_MentionAlreadyReadThenMentionedAgain_DoesNotBecomeUnread()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "第一版", mentioned));
        await Host.Inbox.MarkAllReadAsync(As(item, mentioned), CancellationToken.None);

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "第二版", mentioned));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await MentionsOfAsync(mentioned), Has.Count.EqualTo(1));
            Assert.That(await Host.Inbox.CountUnreadAsync(As(item, mentioned), CancellationToken.None), Is.Zero);
        }
    }

    [Test]
    public async Task CommentEdited_MentionsAnotherPersonForTheFirstTime_StillNotifiesThem()
    {
        // Arrange
        var (item, creator, author, first, second) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "第一版", first));

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "第二版", first, second));

        // Assert
        var mentionOfSecond = (await MentionsOfAsync(second)).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await MentionsOfAsync(first), Has.Count.EqualTo(1));
            Assert.That(mentionOfSecond.CommentId, Is.EqualTo(commentId));
            Assert.That(mentionOfSecond.ExcerptEditedAt, Is.Null, "這次編輯新增的提及，內容本來就是新的");
        }
    }

    [Test]
    public async Task CommentMentions_TwoDifferentComments_EachGetsItsOwnMention()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        await HandleAsync(item.CommentedAs(Guid.CreateVersion7(), author, "一", mentioned));
        await HandleAsync(item.CommentedAs(Guid.CreateVersion7(), author, "二", mentioned));

        // Assert
        Assert.That(await MentionsOfAsync(mentioned), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CommentMention_DeletedThenRestoredThenMentionedByEdit_NotifiesAgain()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "留言", mentioned));
        await HandleAsync(item.CommentDeleted(commentId, author));
        await HandleAsync(item.CommentRestored(commentId, author));

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "留言（改）", mentioned));

        // Assert
        Assert.That(await MentionsOfAsync(mentioned), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DescriptionMention_UnreadMentionAlreadyThere_DoesNotAddAnother()
    {
        // Arrange
        var (item, creator, _, mentioned, _) = NewItem();
        await HandleAsync(item.Created(creator, mentioned: mentioned));

        // Act
        await HandleAsync(item.Updated(creator, mentioned: mentioned));
        await HandleAsync(item.Updated(creator, mentioned: mentioned));

        // Assert
        Assert.That(await MentionsOfAsync(mentioned), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DescriptionMention_AfterTheFirstOneWasRead_NotifiesAgain()
    {
        // Arrange
        var (item, creator, _, mentioned, _) = NewItem();
        await HandleAsync(item.Created(creator, mentioned: mentioned));
        await Host.Inbox.MarkAllReadAsync(As(item, mentioned), CancellationToken.None);

        // Act
        await HandleAsync(item.Updated(creator, mentioned: mentioned));

        // Assert
        var mentions = await MentionsOfAsync(mentioned);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mentions, Has.Count.EqualTo(2));
            Assert.That(mentions.Count(m => m.ReadAt is null), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DescriptionMention_OtherItemOrOtherPerson_IsNotAffected()
    {
        // Arrange
        var (item, creator, _, mentioned, other) = NewItem();
        await HandleAsync(item.Created(creator, mentioned: mentioned));
        var another = new EventBuilder(item.OrganizationId, item.ProjectId, Guid.CreateVersion7());

        // Act
        await HandleAsync(item.Updated(creator, mentioned: other));
        await HandleAsync(another.Created(creator, mentioned: mentioned));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await MentionsOfAsync(mentioned), Has.Count.EqualTo(2));
            Assert.That(await MentionsOfAsync(other), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task DescriptionMention_UnreadCommentMentionOnTheSameItem_DoesNotSuppressIt()
    {
        // Arrange
        var (item, creator, author, mentioned, _) = NewItem();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(Guid.CreateVersion7(), author, "留言提及", mentioned));

        // Act
        await HandleAsync(item.Updated(creator, mentioned: mentioned));

        // Assert
        Assert.That(await MentionsOfAsync(mentioned), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task DescriptionMention_AnotherPersonMentionsTheSamePersonWhileTheFirstIsUnread_NotifiesAgain()
    {
        // Arrange
        var (item, creator, other, mentioned, _) = NewItem();
        await HandleAsync(item.Created(creator, mentioned: mentioned));

        // Act
        await HandleAsync(item.Updated(other, mentioned: mentioned));
        await HandleAsync(item.Updated(creator, mentioned: mentioned));
        await HandleAsync(item.Updated(other, mentioned: mentioned));

        // Assert：各人只算自己的那一則，同一個人反覆提及不洗收件匣。
        var mentions = await MentionsOfAsync(mentioned);
        Assert.That(mentions.Select(m => m.ActorPersonId), Is.EquivalentTo(new Guid?[] { creator, other }));
    }

    [Test]
    public async Task DescriptionMention_TheFirstPersonMentionsAgainAfterReadingOnlyThatOne_NotifiesAThirdTime()
    {
        // Arrange
        var (item, creator, other, mentioned, _) = NewItem();
        await HandleAsync(item.Created(creator, mentioned: mentioned));
        await HandleAsync(item.Updated(other, mentioned: mentioned));
        await Host.Inbox.MarkAllReadAsync(As(item, mentioned), CancellationToken.None);

        // Act
        await HandleAsync(item.Updated(creator, mentioned: mentioned));

        // Assert
        Assert.That(await MentionsOfAsync(mentioned), Has.Count.EqualTo(3));
    }
}
