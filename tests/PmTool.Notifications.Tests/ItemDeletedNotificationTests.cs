using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Notifications.Tests;

/// <summary>項目刪除時它的通知一起刪掉（復原不找回）；帳號通知沒有項目，不受影響，也不受去重與「已編輯」影響。</summary>
[TestFixture]
public sealed class ItemDeletedNotificationTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);

    private RecordingObserver _observer = null!;
    private NotificationsTestHost _host = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _observer = new RecordingObserver();
        _host = await NotificationsTestHost.StartAsync(
            new DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), observers: [_observer]);
    }

    [TearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private static Actor As(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);

    private (EventBuilder Item, Guid[] People) NewItem()
    {
        var item = EventBuilder.ForNewItem();
        Guid[] everyone = [.. Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7())];
        _host.Access.Allow(item.ProjectId, everyone);
        return (item, everyone);
    }

    private Task HandleAsync(params PmTool.Work.WorkEvent[] events) => _host.Consumer.HandleAsync(events, CancellationToken.None);

    private Task PublishTokenNoticeAsync(Guid organization, Guid person) =>
        _host.Services.GetServices<IAccountEventSink>().Single().PublishAsync(
            new AccountEvent(Guid.CreateVersion7(), organization, person, AccountEventKind.AccessTokenCreated, At)
            {
                Tokens = [new AccountEventToken("CI", [AccessTokenScope.ItemsRead], At.AddDays(30))],
                SourceAddress = "203.0.113.5",
            },
            CancellationToken.None);

    [Test]
    public async Task ItemDeleted_RemovesItsNotificationsReadOrNotAndUnreadCountDrops()
    {
        // Arrange
        var (item, p) = NewItem();
        var (creator, author, mentioned) = (p[0], p[1], p[2]);
        await HandleAsync(item.Created(creator, mentioned: mentioned));
        await HandleAsync(item.Commented(author, "留言", mentioned));
        await _host.Inbox.MarkAllReadAsync(As(item, creator), CancellationToken.None);
        await HandleAsync(item.Commented(author, "再一則"));

        // Act
        await HandleAsync(item.Deleted(author));

        // Assert
        var page = (await _host.Inbox.ListAsync(As(item, mentioned), new NotificationQuery(false, null, 20), CancellationToken.None)).Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await _host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId), Is.Empty);
            Assert.That(page.Items, Is.Empty);
            Assert.That(await _host.Inbox.CountUnreadAsync(As(item, mentioned), CancellationToken.None), Is.Zero);
            Assert.That(await _host.Inbox.CountUnreadAsync(As(item, creator), CancellationToken.None), Is.Zero);
        }
    }

    [Test]
    public async Task ItemDeleted_TellsTheObserverAboutEveryoneWhoLostNotifications()
    {
        // Arrange
        var (item, p) = NewItem();
        var (creator, author, mentioned) = (p[0], p[1], p[2]);
        await HandleAsync(item.Created(author, creator, mentioned));
        var callsBefore = _observer.Calls.Count;

        // Act
        await HandleAsync(item.Deleted(author));

        // Assert
        var told = _observer.Calls.Skip(callsBefore).SelectMany(call => call).Select(r => r.PersonId).ToHashSet();
        Assert.That(told, Is.EquivalentTo(new[] { creator, mentioned }));
    }

    [Test]
    public async Task ItemDeleted_OnlyRemovesThatItemsNotifications()
    {
        // Arrange
        var (item, p) = NewItem();
        var (creator, author) = (p[0], p[1]);
        var other = new EventBuilder(item.OrganizationId, item.ProjectId, Guid.CreateVersion7());
        await HandleAsync(item.Created(author, assignee: creator));
        await HandleAsync(other.Created(author, assignee: creator));

        // Act
        await HandleAsync(item.Deleted(author));

        // Assert
        var rows = (await _host.AllNotificationsAsync()).Where(n => n.RecipientId == creator).ToList();
        Assert.That(rows.Select(n => n.ItemId), Is.EqualTo(new[] { other.ItemId }));
    }

    [Test]
    public async Task ItemRestored_DoesNotBringTheNotificationsBack()
    {
        // Arrange
        var (item, p) = NewItem();
        var (creator, author) = (p[0], p[1]);
        await HandleAsync(item.Created(author, assignee: creator));
        await HandleAsync(item.Deleted(author));

        // Act
        await HandleAsync(item.Restored(author, creator: author, assignee: creator));

        // Assert
        Assert.That((await _host.AllNotificationsAsync()).Where(n => n.ItemId == item.ItemId), Is.Empty);
    }

    [Test]
    public async Task ItemDeleted_AccountNoticeOfTheSamePerson_StaysAndIsStillUnread()
    {
        // Arrange
        var (item, p) = NewItem();
        var (creator, author) = (p[0], p[1]);
        await HandleAsync(item.Created(author, assignee: creator));
        await PublishTokenNoticeAsync(item.OrganizationId, creator);

        // Act
        await HandleAsync(item.Deleted(author));

        // Assert
        var rows = (await _host.AllNotificationsAsync()).Where(n => n.RecipientId == creator).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Kind, Is.EqualTo(NotificationKind.AccountSecurity));
            Assert.That(await _host.Inbox.CountUnreadAsync(As(item, creator), CancellationToken.None), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task AccountNotice_NotAffectedByMentionDedupOrCommentEdits()
    {
        // Arrange
        var (item, p) = NewItem();
        var (creator, author, mentioned) = (p[0], p[1], p[2]);
        var commentId = Guid.CreateVersion7();
        await PublishTokenNoticeAsync(item.OrganizationId, mentioned);
        await HandleAsync(item.Created(creator));
        await HandleAsync(item.CommentedAs(commentId, author, "留言", mentioned));

        // Act
        await HandleAsync(item.CommentEdited(commentId, author, "改過", mentioned));
        await HandleAsync(item.Updated(creator, mentioned: mentioned));

        // Assert
        var page = (await _host.Inbox.ListAsync(As(item, mentioned), new NotificationQuery(false, null, 20), CancellationToken.None)).Value;
        var account = page.Items.Single(n => n.Kind == NotificationKind.AccountSecurity);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.Edited, Is.False);
            Assert.That(account.CommentId, Is.Null);
            Assert.That(page.Items.Count(n => n.Kind == NotificationKind.Mentioned), Is.EqualTo(2), "留言一則、說明一則");
        }
    }

    private sealed class RecordingObserver : INotificationObserver
    {
        private readonly List<IReadOnlyCollection<NotifiedRecipient>> _calls = [];

        public IReadOnlyList<IReadOnlyCollection<NotifiedRecipient>> Calls
        {
            get
            {
                lock (_calls)
                {
                    return [.. _calls];
                }
            }
        }

        public Task NotifiedAsync(IReadOnlyCollection<NotifiedRecipient> recipients, CancellationToken cancellationToken)
        {
            lock (_calls)
            {
                _calls.Add(recipients);
            }

            return Task.CompletedTask;
        }
    }
}
