using PmTool.Abstractions;

namespace PmTool.Notifications.Tests;

/// <summary>關注項目：狀態、原因、取消與再關注。</summary>
[TestFixture]
public sealed class SubscriptionTests : ConsumerTestBase
{
    private static Actor As(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);

    [Test]
    public async Task Get_NeverFollowed_IsNotFollowingWithoutAReason()
    {
        // Arrange
        var (item, me, _, _, _) = NewItem();

        // Act
        var subscription = await Host.Subscriptions.GetAsync(As(item, me), item.ProjectId, item.ItemId, CancellationToken.None);

        // Assert
        Assert.That(subscription, Is.EqualTo(new ItemSubscription(false, null, true)));
    }

    [Test]
    public async Task Set_Follow_IsManual()
    {
        // Arrange
        var (item, me, _, _, _) = NewItem();

        // Act
        var subscription = (await Host.Subscriptions.SetAsync(As(item, me), item.ProjectId, item.ItemId, true, CancellationToken.None)).Value;

        // Assert
        Assert.That(subscription, Is.EqualTo(new ItemSubscription(true, SubscriptionReason.Manual, true)));
    }

    [Test]
    public async Task Set_FollowWhileAlreadyFollowingAutomatically_KeepsTheReason()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee));

        // Act
        var subscription = (await Host.Subscriptions.SetAsync(As(item, assignee), item.ProjectId, item.ItemId, true, CancellationToken.None)).Value;

        // Assert
        Assert.That(subscription, Is.EqualTo(new ItemSubscription(true, SubscriptionReason.Assignee, true)));
    }

    [Test]
    public async Task Set_UnfollowThenFollow_BecomesManual()
    {
        // Arrange
        var (item, creator, _, _, _) = NewItem();
        await HandleAsync(item.Created(creator));
        var unfollowed = (await Host.Subscriptions.SetAsync(As(item, creator), item.ProjectId, item.ItemId, false, CancellationToken.None)).Value;

        // Act
        var followed = (await Host.Subscriptions.SetAsync(As(item, creator), item.ProjectId, item.ItemId, true, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unfollowed, Is.EqualTo(new ItemSubscription(false, null, true)));
            Assert.That(followed, Is.EqualTo(new ItemSubscription(true, SubscriptionReason.Manual, true)));
        }
    }

    [Test]
    public async Task Set_TwiceAtTheSameTime_StaysOneRow()
    {
        // Arrange
        var (item, me, _, _, _) = NewItem();

        // Act
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Host.Subscriptions.SetAsync(As(item, me), item.ProjectId, item.ItemId, true, CancellationToken.None)));

        // Assert
        Assert.That((await Host.AllSubscriptionsAsync()).Count(s => s.ItemId == item.ItemId && s.PersonId == me), Is.EqualTo(1));
    }

    [Test]
    public async Task Get_SomeoneElsesSubscription_IsNotMine()
    {
        // Arrange
        var (item, creator, other, _, _) = NewItem();
        await HandleAsync(item.Created(creator));

        // Act
        var subscription = await Host.Subscriptions.GetAsync(As(item, other), item.ProjectId, item.ItemId, CancellationToken.None);

        // Assert
        Assert.That(subscription.Following, Is.False);
    }

    [Test]
    public async Task Set_NotAProjectMember_IsRefusedWithAConflictAndLeavesNoRow()
    {
        // Arrange：組織的管理員看得到專案，但沒有加入（工作引擎不把他算成讀者）。
        var (item, _, _, _, _) = NewItem();
        var outsider = Guid.CreateVersion7();

        // Act
        var result = await Host.Subscriptions.SetAsync(As(item, outsider), item.ProjectId, item.ItemId, true, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure, Is.EqualTo(new Failure(FailureKind.Conflict, "subscription.not_member", "要先加入這個專案，才能關注它的項目。")));
            Assert.That((await Host.AllSubscriptionsAsync()).Any(s => s.PersonId == outsider), Is.False);
        }
    }

    [Test]
    public async Task Set_NotAProjectMemberUnfollowing_IsAlsoRefused()
    {
        // Arrange
        var (item, _, _, _, _) = NewItem();

        // Act
        var result = await Host.Subscriptions.SetAsync(As(item, Guid.CreateVersion7()), item.ProjectId, item.ItemId, false, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(NotificationErrorCodes.NotProjectMember));
    }

    [Test]
    public async Task Get_NotAProjectMember_CannotFollow()
    {
        // Arrange
        var (item, _, _, _, _) = NewItem();

        // Act
        var subscription = await Host.Subscriptions.GetAsync(As(item, Guid.CreateVersion7()), item.ProjectId, item.ItemId, CancellationToken.None);

        // Assert
        Assert.That(subscription, Is.EqualTo(new ItemSubscription(false, null, false)));
    }

    [Test]
    public async Task Get_RemovedFromTheProjectAfterFollowing_IsNoLongerFollowing()
    {
        // Arrange：被指派而自動關注，之後被移出專案。
        var (item, creator, assignee, _, _) = NewItem();
        await HandleAsync(item.Created(creator, assignee));
        Host.Access.Revoke(item.ProjectId, assignee);

        // Act
        var subscription = await Host.Subscriptions.GetAsync(As(item, assignee), item.ProjectId, item.ItemId, CancellationToken.None);

        // Assert
        Assert.That(subscription, Is.EqualTo(new ItemSubscription(false, null, false)));
    }
}
