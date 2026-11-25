using PmTool.Abstractions;

namespace PmTool.Notifications.Tests;

/// <summary>留言編輯之後，收件匣裡那則通知的摘要換成新的並標成「已編輯」。</summary>
[TestFixture]
public sealed class CommentEditedFlagTests : ConsumerTestBase
{
    private static Actor As(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);

    [Test]
    public async Task Inbox_BeforeAnyEdit_NotificationIsNotMarkedEdited()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        await HandleAsync(item.CommentedAs(Guid.CreateVersion7(), author, "原文"));

        // Assert
        var page = (await Host.Inbox.ListAsync(As(item, creator), new NotificationQuery(false, null, 20), CancellationToken.None)).Value;
        Assert.That(page.Items.Single(n => n.Excerpt == "原文").Edited, Is.False);
    }

    [Test]
    public async Task Inbox_AfterTheCommentIsEdited_NotificationShowsNewExcerptAndIsMarkedEdited()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "原文"));
        var edit = item.CommentEdited(commentId, author, "改過的");

        // Act
        await HandleAsync(edit);

        // Assert
        var page = (await Host.Inbox.ListAsync(As(item, creator), new NotificationQuery(false, null, 20), CancellationToken.None)).Value;
        var notice = page.Items.Single(n => n.CommentId == commentId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notice.Excerpt, Is.EqualTo("改過的"));
            Assert.That(notice.Edited, Is.True);
        }
    }

    [Test]
    public async Task Inbox_EditingOneComment_DoesNotMarkTheOthersEdited()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        var edited = Guid.CreateVersion7();
        var untouched = Guid.CreateVersion7();
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(edited, author, "要改的"));
        await HandleAsync(item.CommentedAs(untouched, author, "不改的"));

        // Act
        await HandleAsync(item.CommentEdited(edited, author, "改好了"));

        // Assert
        var page = (await Host.Inbox.ListAsync(As(item, creator), new NotificationQuery(false, null, 20), CancellationToken.None)).Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Single(n => n.CommentId == edited).Edited, Is.True);
            Assert.That(page.Items.Single(n => n.CommentId == untouched).Edited, Is.False);
        }
    }
}
