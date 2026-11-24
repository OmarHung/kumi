using PmTool.Abstractions;

namespace PmTool.Notifications.Tests;

/// <summary>刪除留言時，收件匣有變的人（被刪掉通知的收件人）要告訴觀察者，網頁的未讀數才會即時變。</summary>
[TestFixture]
public sealed class CommentDeletedObserverTests
{
    private CollectingObserver _observer = null!;
    private NotificationsTestHost _host = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _observer = new CollectingObserver();
        _host = await NotificationsTestHost.StartAsync(
            new DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), observers: [_observer]);
    }

    [TearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CommentDeleted_TellsTheObserverWhoseInboxChanged()
    {
        // Arrange
        var item = EventBuilder.ForNewItem();
        Guid[] people = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        var (creator, author, bystander) = (people[0], people[1], people[2]);
        _host.Access.Allow(item.ProjectId, people);
        var commentId = Guid.CreateVersion7();
        await _host.Consumer.HandleAsync([item.Created(creator), item.CommentedAs(commentId, author, "要刪的")], CancellationToken.None);
        _observer.Clear();

        // Act
        await _host.Consumer.HandleAsync([item.CommentDeleted(commentId, author)], CancellationToken.None);

        // Assert
        var told = _observer.Recipients.Select(r => r.PersonId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(told, Is.EqualTo(new[] { creator }));
            Assert.That(told, Does.Not.Contain(bystander));
        }
    }

    [Test]
    public async Task CommentDeleted_WithoutNotifications_DoesNotCallTheObserver()
    {
        // Arrange
        var item = EventBuilder.ForNewItem();
        var author = Guid.CreateVersion7();
        _host.Access.Allow(item.ProjectId, [author]);
        var commentId = Guid.CreateVersion7();
        await _host.Consumer.HandleAsync([item.Created(author), item.CommentedAs(commentId, author, "沒人收到")], CancellationToken.None);
        _observer.Clear();

        // Act
        await _host.Consumer.HandleAsync([item.CommentDeleted(commentId, author)], CancellationToken.None);

        // Assert
        Assert.That(_observer.Recipients, Is.Empty);
    }

    private sealed class CollectingObserver : INotificationObserver
    {
        private readonly List<NotifiedRecipient> _recipients = [];

        public IReadOnlyList<NotifiedRecipient> Recipients
        {
            get
            {
                lock (_recipients)
                {
                    return [.. _recipients];
                }
            }
        }

        public void Clear()
        {
            lock (_recipients)
            {
                _recipients.Clear();
            }
        }

        public Task NotifiedAsync(IReadOnlyCollection<NotifiedRecipient> recipients, CancellationToken cancellationToken)
        {
            lock (_recipients)
            {
                _recipients.AddRange(recipients);
            }

            return Task.CompletedTask;
        }
    }
}
