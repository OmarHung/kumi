using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>規劃書第 2 節第 3 點的表：每一種事件通知誰、讓誰自動關注。</summary>
[TestFixture]
public sealed class ConsumerRulesTests : ConsumerTestBase
{
    [Test]
    public async Task Created_WithAnAssignee_NotifiesTheAssigneeAndFollowsCreatorAndAssignee()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();

        // Act
        await HandleAsync(item.Created(creator, assignee));

        // Assert
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).ToDictionary(s => s.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.KindsForAsync(creator), Is.Empty);
            Assert.That(subscriptions[creator].Reason, Is.EqualTo(SubscriptionReason.Creator));
            Assert.That(subscriptions[assignee].Reason, Is.EqualTo(SubscriptionReason.Assignee));
        }
    }

    [Test]
    public async Task Created_ByImportWithAnAssignee_DoesNotNotifyButStillFollowsCreatorAndAssignee()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();

        // Act
        await HandleAsync(item.CreatedByImport(creator, assignee));

        // Assert
        var subscriptions = (await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId).ToDictionary(s => s.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(assignee), Is.Empty, "匯入的項目不發「指派給你」");
            Assert.That(subscriptions[creator].Reason, Is.EqualTo(SubscriptionReason.Creator));
            Assert.That(subscriptions[assignee].Reason, Is.EqualTo(SubscriptionReason.Assignee));
        }
    }

    [Test]
    public async Task Created_ByImportWithAMention_StillNotifiesTheMentionedPerson()
    {
        // Arrange
        var (item, creator, mentioned, _, _) = NewItem();

        // Act
        await HandleAsync(item.CreatedByImport(creator, null, mentioned));

        // Assert
        Assert.That(await Host.KindsForAsync(mentioned), Is.EqualTo(new[] { NotificationKind.Mentioned }));
    }

    [Test]
    public async Task Created_AssignedToTheCreator_NotifiesNobody()
    {
        // Arrange
        var (item, creator, _, _, _) = NewItem();

        // Act
        await HandleAsync(item.Created(creator, creator));

        // Assert
        Assert.That(await Host.KindsForAsync(creator), Is.Empty);
    }

    [Test]
    public async Task Created_WithAMention_NotifiesAndFollowsTheMentionedPerson()
    {
        // Arrange
        var (item, creator, mentioned, _, _) = NewItem();

        // Act
        await HandleAsync(item.Created(creator, null, mentioned));

        // Assert
        var subscription = (await Host.AllSubscriptionsAsync()).Single(s => s.ItemId == item.ItemId && s.PersonId == mentioned);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(mentioned), Is.EqualTo(new[] { NotificationKind.Mentioned }));
            Assert.That(subscription.Reason, Is.EqualTo(SubscriptionReason.Mentioned));
        }
    }

    [Test]
    public async Task Created_AssigneeIsAlsoMentioned_GetsOnlyTheAssignment()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();

        // Act
        await HandleAsync(item.Created(creator, assignee, assignee));

        // Assert
        Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
    }

    [Test]
    public async Task Updated_AssigneeChanged_NotifiesTheNewAssigneeAndFollowsThem()
    {
        // Arrange
        var (item, editor, newAssignee, _, _) = NewItem();

        // Act
        await HandleAsync(item.Updated(editor, newAssignee, assigneeChanged: true));

        // Assert
        var subscription = (await Host.AllSubscriptionsAsync()).Single(s => s.ItemId == item.ItemId && s.PersonId == newAssignee);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(newAssignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(subscription.Reason, Is.EqualTo(SubscriptionReason.Assignee));
            Assert.That(await Host.KindsForAsync(editor), Is.Empty);
        }
    }

    [Test]
    public async Task Updated_AssigneeNotChanged_DoesNotNotifyTheCurrentAssignee()
    {
        // Arrange：改了標題，負責人還是同一個人。
        var (item, editor, assignee, _, _) = NewItem();

        // Act
        await HandleAsync(item.Updated(editor, assignee, assigneeChanged: false));

        // Assert
        Assert.That(await Host.KindsForAsync(assignee), Is.Empty);
    }

    [Test]
    public async Task Updated_AssigneeCleared_NotifiesNobody()
    {
        // Arrange
        var (item, editor, _, _, _) = NewItem();

        // Act
        await HandleAsync(item.Updated(editor, null, assigneeChanged: true));

        // Assert
        Assert.That(await Host.AllNotificationsAsync(), Has.None.Matches<Persistence.NotificationRow>(n => n.ItemId == item.ItemId));
    }

    [Test]
    public async Task Updated_NewMention_NotifiesTheMentionedPerson()
    {
        // Arrange
        var (item, editor, mentioned, _, _) = NewItem();

        // Act
        await HandleAsync(item.Updated(editor, mentioned: mentioned));

        // Assert
        Assert.That(await Host.KindsForAsync(mentioned), Is.EqualTo(new[] { NotificationKind.Mentioned }));
    }

    [Test]
    public async Task Transitioned_NotifiesFollowersExceptTheActor()
    {
        // Arrange：建立者（關注）、負責人（關注）、做事的人是負責人。
        var (item, creator, assignee, outsider, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee));

        // Act
        await HandleAsync(item.Transitioned(assignee, "待辦", "審查中"));

        // Assert
        var notification = (await Host.AllNotificationsAsync()).Single(n => n.RecipientId == creator && n.Kind == NotificationKind.StateChanged);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notification.FromStateName, Is.EqualTo("待辦"));
            Assert.That(notification.ToStateName, Is.EqualTo("審查中"));
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.KindsForAsync(outsider), Is.Empty);
        }
    }

    [Test]
    public async Task Commented_FollowerAndMentionedPerson_BothGetTheCommentId()
    {
        // Arrange
        var (item, commenter, mentioned, follower, _) = NewItem();
        await HandleAsync(item.Created(follower));
        var commented = item.Commented(commenter, "請看一下 @王小明", mentioned);

        // Act
        await HandleAsync(commented);

        // Assert
        var all = await Host.AllNotificationsAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(all.Single(n => n.RecipientId == mentioned).CommentId, Is.EqualTo(commented.Item.ActivityId));
            Assert.That(all.Single(n => n.RecipientId == follower).CommentId, Is.EqualTo(commented.Item.ActivityId));
        }
    }

    [Test]
    public async Task Commented_EventWrittenBeforeActivityIdExisted_LeavesTheCommentIdEmpty()
    {
        // Arrange
        var (item, commenter, mentioned, _, _) = NewItem();

        // Act
        await HandleAsync(EventBuilder.Legacy(item.Commented(commenter, "請看一下 @王小明", mentioned)));

        // Assert
        Assert.That((await Host.AllNotificationsAsync()).Single(n => n.RecipientId == mentioned).CommentId, Is.Null);
    }

    [Test]
    public async Task AssignedStateChangedAndDescriptionMention_HaveNoCommentIdEvenThoughTheEventsCarryAnActivityId()
    {
        // Arrange
        var (item, actor, assignee, mentioned, _) = NewItem();

        // Act
        await HandleAsync(item.Created(actor, assignee, mentioned));
        await HandleAsync(item.Transitioned(actor));

        // Assert
        var all = await Host.AllNotificationsAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(all.Select(n => n.Kind), Is.SupersetOf(new[] { NotificationKind.Assigned, NotificationKind.Mentioned, NotificationKind.StateChanged }));
            Assert.That(all.Select(n => n.CommentId), Is.All.Null);
        }
    }

    [Test]
    public async Task Commented_MentionedPerson_GetsMentionedWithTheExcerpt()
    {
        // Arrange
        var (item, commenter, mentioned, _, _) = NewItem();

        // Act
        await HandleAsync(item.Commented(commenter, "請看一下 @王小明", mentioned));

        // Assert
        var notification = (await Host.AllNotificationsAsync()).Single(n => n.RecipientId == mentioned);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notification.Kind, Is.EqualTo(NotificationKind.Mentioned));
            Assert.That(notification.Excerpt, Is.EqualTo("請看一下 @王小明"));
            Assert.That(notification.ActorPersonId, Is.EqualTo(commenter));
            Assert.That(notification.ItemKey, Is.EqualTo("WEB-1"));
        }
    }

    [Test]
    public async Task Commented_FollowerWhoIsAlsoMentioned_GetsOnlyOneNotification()
    {
        // Arrange：建立者關注了項目，之後留言又提及了他。
        var (item, creator, commenter, _, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        await HandleAsync(item.Commented(commenter, "@建立者 你看", creator));

        // Assert
        Assert.That(await Host.KindsForAsync(creator), Is.EqualTo(new[] { NotificationKind.Mentioned }));
    }

    [Test]
    public async Task Replied_CarriesTheExcerpt_SoTheInboxShowsWhatWasSaid()
    {
        // Arrange
        var (item, author, replier, _, _) = NewItem();

        // Act
        await HandleAsync(item.Replied(replier, author, "我來處理［圖片］"));

        // Assert
        var notification = (await Host.AllNotificationsAsync()).Single(n => n.RecipientId == author);
        Assert.That(notification.Excerpt, Is.EqualTo("我來處理［圖片］"));
    }

    [Test]
    public async Task Replied_AuthorOfTheComment_GetsRepliedEvenWhenNotFollowing()
    {
        // Arrange
        var (item, author, replier, _, _) = NewItem();

        // Act
        await HandleAsync(item.Replied(replier, author, "我來處理"));

        // Assert
        Assert.That(await Host.KindsForAsync(author), Is.EqualTo(new[] { NotificationKind.Replied }));
    }

    [Test]
    public async Task Replied_FollowerWhoIsNotTheAuthor_StillGetsCommented()
    {
        // Arrange
        var (item, creator, replier, author, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        await HandleAsync(item.Replied(replier, author, "我來處理"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(author), Is.EqualTo(new[] { NotificationKind.Replied }));
            Assert.That(await Host.KindsForAsync(creator), Is.EqualTo(new[] { NotificationKind.Commented }));
        }
    }

    [Test]
    public async Task Replied_AuthorWhoIsAlsoAFollower_GetsOnlyReplied()
    {
        // Arrange
        var (item, author, replier, _, _) = NewItem();
        await HandleAsync(item.Created(author));

        // Act
        await HandleAsync(item.Replied(replier, author, "我來處理"));

        // Assert
        Assert.That(await Host.KindsForAsync(author), Is.EqualTo(new[] { NotificationKind.Replied }));
    }

    [Test]
    public async Task Replied_AuthorWhoIsAlsoMentioned_GetsOnlyMentioned()
    {
        // Arrange
        var (item, author, replier, _, _) = NewItem();

        // Act
        await HandleAsync(item.Replied(replier, author, "@作者 你看", author));

        // Assert
        Assert.That(await Host.KindsForAsync(author), Is.EqualTo(new[] { NotificationKind.Mentioned }));
    }

    [Test]
    public async Task Replied_ToOwnComment_NotifiesNobodyExtra()
    {
        // Arrange
        var (item, creator, author, _, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        await HandleAsync(item.Replied(author, author, "補充"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(author), Is.Empty);
            Assert.That(await Host.KindsForAsync(creator), Is.EqualTo(new[] { NotificationKind.Commented }));
        }
    }

    [Test]
    public async Task Commented_OtherFollowers_GetCommentedAndTheCommenterStartsFollowing()
    {
        // Arrange
        var (item, creator, commenter, _, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        await HandleAsync(item.Commented(commenter, "做好了"));

        // Assert
        var subscription = (await Host.AllSubscriptionsAsync()).Single(s => s.ItemId == item.ItemId && s.PersonId == commenter);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(creator), Is.EqualTo(new[] { NotificationKind.Commented }));
            Assert.That(await Host.KindsForAsync(commenter), Is.Empty);
            Assert.That(subscription.Reason, Is.EqualTo(SubscriptionReason.Commenter));
        }
    }

    [Test]
    public async Task Commented_LongExcerpt_IsCutToTheLimit()
    {
        // Arrange
        var (item, creator, commenter, _, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        await HandleAsync(item.Commented(commenter, new string('字', WorkEventRules.MaxExcerptLength + 50)));

        // Assert
        var notification = (await Host.AllNotificationsAsync()).Single(n => n.RecipientId == creator && n.Kind == NotificationKind.Commented);
        Assert.That(notification.Excerpt, Has.Length.EqualTo(WorkEventRules.MaxExcerptLength));
    }

    [Test]
    public async Task Deleted_RemovesTheItemsSubscriptionsAndNotifications()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee));
        Assert.That((await Host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId), Is.EqualTo(1), "刪除之前有一則指派的通知");

        // Act
        await HandleAsync(item.Deleted(creator));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Host.AllSubscriptionsAsync()).Where(s => s.ItemId == item.ItemId), Is.Empty);
            Assert.That((await Host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId), Is.Zero);
        }
    }

    [Test]
    public async Task Commented_OrganizationAdminWhoIsNotFollowing_GetsNothing()
    {
        // Arrange：管理員看得到每個專案，但沒有關注、也沒有被點名。
        var (item, creator, commenter, admin, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        await HandleAsync(item.Commented(commenter, "留言"));

        // Assert
        Assert.That(await Host.KindsForAsync(admin), Is.Empty);
    }
}
