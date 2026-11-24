namespace PmTool.Notifications.Tests;

/// <summary>從別的系統搬過來的歷史事件：一則通知都不發；只有建立項目會建立關注（負責人與指定的人，不含建立的人）。</summary>
[TestFixture]
public sealed class HistoryEventTests : ConsumerTestBase
{
    [Test]
    public async Task CreatedByHistory_SendsNoNotificationEvenToTheAssigneeAndMentionedPeople()
    {
        // Arrange
        var (item, creator, assignee, mentioned, _) = NewItem();
        var created = item.CreatedByHistory(creator, assignee, [], mentioned);

        // Act
        await HandleAsync(created);

        // Assert
        Assert.That((await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId), Is.Empty);
    }

    [Test]
    public async Task CreatedByHistory_FollowsTheAssigneeAndTheListedFollowersButNotTheCreatorOrMentionedPeople()
    {
        // Arrange
        var (item, creator, assignee, follower, mentioned) = NewItem();
        var created = item.CreatedByHistory(creator, assignee, [follower], mentioned);

        // Act
        await HandleAsync(created);

        // Assert
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId);
        Assert.That(subscriptions, Is.EquivalentTo(new[] { assignee, follower }));
    }

    [Test]
    public async Task CreatedByHistory_FollowerWhoCannotReadTheProject_IsNotFollowed()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        var outsider = Guid.CreateVersion7();

        // Act
        await HandleAsync(item.CreatedByHistory(creator, assignee, [outsider]));

        // Assert
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId);
        Assert.That(subscriptions, Is.EquivalentTo(new[] { assignee }));
    }

    [Test]
    public async Task CommentedByHistory_SendsNothingAndDoesNotFollowTheAuthorOrMentionedPeople()
    {
        // Arrange
        var (item, creator, assignee, author, mentioned) = NewItem();
        await HandleAsync(item.CreatedByHistory(creator, assignee, []));
        var follower = Guid.CreateVersion7();
        Host.Access.Allow(item.ProjectId, [follower]);

        // Act
        await HandleAsync(item.CommentedByHistory(author, "舊留言", null, mentioned));
        await HandleAsync(item.CommentedByHistory(author, "舊回覆", assignee, mentioned));

        // Assert
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId), Is.Empty);
            Assert.That(subscriptions, Is.EquivalentTo(new[] { assignee }));
        }
    }

    [Test]
    public async Task AttachmentAddedByHistory_SendsNothing()
    {
        // Arrange
        var (item, creator, assignee, uploader, _) = NewItem();
        await HandleAsync(item.CreatedByHistory(creator, assignee, []));

        // Act
        await HandleAsync(item.AttachmentAddedByHistory(uploader, creator, assignee));

        // Assert
        Assert.That((await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId), Is.Empty);
    }

    [Test]
    public async Task CreatedByImportWithoutHistory_StillBehavesLikeTheSpreadsheetImport()
    {
        // Arrange
        var (item, creator, assignee, mentioned, _) = NewItem();

        // Act
        await HandleAsync(item.CreatedByImport(creator, assignee, mentioned));

        // Assert
        var kinds = (await Host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId).Select(n => (n.RecipientId, n.Kind));
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).Select(s => s.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(kinds, Is.EquivalentTo(new[] { (mentioned, NotificationKind.Mentioned) }), "試算表匯入照舊：提及的人收通知，指派不通知");
            Assert.That(subscriptions, Is.EquivalentTo(new[] { creator, assignee, mentioned }));
        }
    }
}
