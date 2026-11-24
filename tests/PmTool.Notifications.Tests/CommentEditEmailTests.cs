namespace PmTool.Notifications.Tests;

/// <summary>留言編輯、刪除與寄信：信是寄的時候才讀通知，所以還沒寄的信跟著留言走。</summary>
[TestFixture]
public sealed class CommentEditEmailTests : EmailTestBase
{
    [Test]
    public async Task Dispatch_CommentEditedBeforeTheDelay_MailsTheNewContent()
    {
        // Arrange
        var (item, me, author, _, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await EnableEmailAsync();
        await HandleAsync(item.Created(me));
        await HandleAsync(item.CommentedAs(commentId, author, "舊內容"));
        Wait(TimeSpan.FromMinutes(1));
        await HandleAsync(item.CommentEdited(commentId, author, "新內容"));
        PassTheDelay();

        // Act
        await DispatchAsync();

        // Assert
        var mail = Transport.Sent.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.TextBody, Does.Contain("新內容"));
            Assert.That(mail.TextBody, Does.Not.Contain("舊內容"));
        }
    }

    [Test]
    public async Task Dispatch_CommentDeletedBeforeTheDelay_SendsNothing()
    {
        // Arrange
        var (item, me, author, _, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await EnableEmailAsync();
        await HandleAsync(item.Created(me));
        await HandleAsync(item.CommentedAs(commentId, author, "要刪的"));
        // 項目建立時沒有通知（自己建立），所以刪掉留言之後沒有東西可寄。
        Wait(TimeSpan.FromMinutes(1));
        await HandleAsync(item.CommentDeleted(commentId, author));
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.Zero);
            Assert.That(Transport.Sent, Is.Empty);
        }
    }

    [Test]
    public async Task Dispatch_CommentDeletedThenRestored_StillSendsNothing()
    {
        // Arrange
        var (item, me, author, _, _) = NewItem();
        var commentId = Guid.CreateVersion7();
        await EnableEmailAsync();
        await HandleAsync(item.Created(me));
        await HandleAsync(item.CommentedAs(commentId, author, "要刪的"));
        Wait(TimeSpan.FromMinutes(1));
        await HandleAsync(item.CommentDeleted(commentId, author));
        await HandleAsync(item.CommentRestored(commentId, author));
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.Zero);
            Assert.That(Transport.Sent, Is.Empty);
        }
    }
}
