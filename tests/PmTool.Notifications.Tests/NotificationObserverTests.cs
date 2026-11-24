using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>通知存好（交易提交）之後，告訴觀察者誰的收件匣有新東西：即時推送用。</summary>
[TestFixture]
public sealed class NotificationObserverTests
{
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

    [Test]
    public async Task HandleAsync_NewNotifications_TellsTheObserverOnceWithDistinctRecipients()
    {
        // Arrange
        var (item, creator, assignee, mentioned) = NewScene();

        // Act：一批裡兩個事件，被提及的人收到兩則。
        await _host.Consumer.HandleAsync(
            [item.Created(creator, assignee, mentioned), item.Commented(creator, "留言", mentioned)], CancellationToken.None);

        // Assert
        Assert.That(_observer.Calls, Has.Count.EqualTo(1));
        var notified = _observer.Calls[0].Select(r => r.PersonId).ToArray();
        Assert.That(notified, Is.EquivalentTo(new[] { assignee, mentioned }));
        Assert.That(_observer.Calls[0].Select(r => r.OrganizationId), Is.All.EqualTo(item.OrganizationId));
    }

    [Test]
    public async Task HandleAsync_TheObserverIsCalledAfterTheNotificationsAreCommitted()
    {
        // Arrange
        var (item, creator, assignee, _) = NewScene();
        var seenWhenCalled = -1;
        _observer.OnNotified = async () => seenWhenCalled = (await _host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId);

        // Act
        await _host.Consumer.HandleAsync([item.Created(creator, assignee)], CancellationToken.None);

        // Assert
        Assert.That(seenWhenCalled, Is.EqualTo(1));
    }

    [Test]
    public async Task HandleAsync_EventsThatCreateNoNotification_DoNotCallTheObserver()
    {
        // Arrange：自己指派給自己、歷史匯入都不通知。
        var (item, creator, _, _) = NewScene();
        var history = EventBuilder.ForNewItem();
        _host.Access.Allow(history.ProjectId, [creator]);

        // Act
        await _host.Consumer.HandleAsync([item.Created(creator, creator)], CancellationToken.None);
        await _host.Consumer.HandleAsync([history.CreatedByHistory(creator, Guid.CreateVersion7(), [])], CancellationToken.None);

        // Assert
        Assert.That(_observer.Calls, Is.Empty);
    }

    [Test]
    public async Task HandleAsync_TheSameBatchAgain_DoesNotNotifyAgain()
    {
        // Arrange
        var (item, creator, assignee, _) = NewScene();
        var created = item.Created(creator, assignee);
        await _host.Consumer.HandleAsync([created], CancellationToken.None);

        // Act：位置已經往前，重送的事件略過。
        await _host.Consumer.HandleAsync([created], CancellationToken.None);

        // Assert
        Assert.That(_observer.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task HandleAsync_AnObserverThatThrows_DoesNotUndoTheNotificationsAndIsLogged()
    {
        // Arrange
        var (item, creator, assignee, _) = NewScene();
        _observer.OnNotified = () => throw new InvalidOperationException("推送壞了");

        // Act
        await _host.Consumer.HandleAsync([item.Created(creator, assignee)], CancellationToken.None);

        // Assert
        Assert.That((await _host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId), Is.EqualTo(1));
        Assert.That(_host.Logs.Entries.Where(e => e.Level == LogLevel.Warning && e.Exception is InvalidOperationException), Is.Not.Empty);
    }

    [Test]
    public async Task HandleAsync_AnObserverThatNeverFinishes_IsGivenUpOnAndTheNextOneStillRuns()
    {
        // Arrange
        var stuck = new RecordingObserver { OnNotified = () => Task.Delay(Timeout.Infinite) };
        var next = new RecordingObserver();
        await using var host = await NotificationsTestHost.StartAsync(
            new DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), observers: [stuck, next], observerTimeout: TimeSpan.FromMilliseconds(200));
        var item = EventBuilder.ForNewItem();
        var (creator, assignee) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        host.Access.Allow(item.ProjectId, [creator, assignee]);

        // Act
        var handling = host.Consumer.HandleAsync([item.Created(creator, assignee)], CancellationToken.None);
        var finished = await Task.WhenAny(handling, Task.Delay(TimeSpan.FromSeconds(10))) == handling;
        if (finished)
        {
            await handling;
        }

        // Assert
        Assert.That(finished, Is.True, "卡住的觀察者不能拖住通知的處理");
        Assert.That(next.Calls, Has.Count.EqualTo(1));
        Assert.That((await host.AllNotificationsAsync()).Count(n => n.ItemId == item.ItemId), Is.EqualTo(1));
        Assert.That(host.Logs.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("超過", StringComparison.Ordinal)), Is.Not.Empty);
    }

    private (EventBuilder Item, Guid Creator, Guid Assignee, Guid Mentioned) NewScene()
    {
        var item = EventBuilder.ForNewItem();
        Guid[] people = [.. Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7())];
        _host.Access.Allow(item.ProjectId, people);
        return (item, people[0], people[1], people[2]);
    }

    private sealed class RecordingObserver : INotificationObserver
    {
        private readonly List<IReadOnlyCollection<NotifiedRecipient>> _calls = [];

        public Func<Task>? OnNotified { get; set; }

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

        public async Task NotifiedAsync(IReadOnlyCollection<NotifiedRecipient> recipients, CancellationToken cancellationToken)
        {
            lock (_calls)
            {
                _calls.Add(recipients);
            }

            if (OnNotified is { } callback)
            {
                await callback();
            }
        }
    }
}
