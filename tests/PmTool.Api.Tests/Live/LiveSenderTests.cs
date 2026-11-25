using Microsoft.Extensions.Logging;
using PmTool.Api.Live;

namespace PmTool.Api.Tests.Live;

/// <summary>一條連線一條連線送：卡住的連線被斷開，不拖住別人，一輪只等一個逾時。</summary>
[TestFixture]
public sealed class LiveSenderTests
{
    private static readonly LiveOptions Short = new() { SendTimeout = TimeSpan.FromMilliseconds(150) };

    private sealed record Setup(LiveSender Sender, FakeHubContext Hub, CapturedLogProvider Logs);

    private static Setup Create()
    {
        var logs = new CapturedLogProvider();
        var hub = new FakeHubContext();
        return new Setup(new LiveSender(hub, Short, LoggerFactory.Create(b => b.AddProvider(logs)).CreateLogger<LiveSender>()), hub, logs);
    }

    private static (LiveConnection Connection, Func<int> Aborts) NewConnection(string id)
    {
        var aborts = 0;
        var connection = new LiveConnection(id, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "token", () => Interlocked.Increment(ref aborts));
        return (connection, () => Volatile.Read(ref aborts));
    }

    [Test]
    public async Task SendTo_AConnectionThatNeverFinishes_IsAbortedAfterTheTimeoutAndTheOthersStillReceive()
    {
        // Arrange
        var setup = Create();
        var (stuck, stuckAborts) = NewConnection("stuck");
        var (healthy, healthyAborts) = NewConnection("healthy");
        setup.Hub.HangingConnections.Add("stuck");

        // Act
        var sending = setup.Sender.SendToAsync([stuck, healthy], "items", new object(), CancellationToken.None);
        var finished = await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(5))) == sending;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finished, Is.True);
            Assert.That(stuckAborts(), Is.EqualTo(1));
            Assert.That(healthyAborts(), Is.Zero);
            Assert.That(setup.Hub.Sent.Select(m => m.Connection), Is.EqualTo(new[] { "healthy" }));
            Assert.That(setup.Logs.Entries.Select(e => e.Level), Does.Contain(LogLevel.Warning));
        }
    }

    [Test]
    public async Task SendTo_SeveralConnectionsStuckAtOnce_TheRoundWaitsOnlyOneTimeout()
    {
        // Arrange
        var setup = Create();
        var stuck = Enumerable.Range(0, 5).Select(i => NewConnection($"stuck-{i}")).ToList();
        foreach (var (connection, _) in stuck)
        {
            setup.Hub.HangingConnections.Add(connection.ConnectionId);
        }

        // Act
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await setup.Sender.SendToAsync([.. stuck.Select(s => s.Connection)], "items", new object(), CancellationToken.None);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(elapsed, Is.LessThan(Short.SendTimeout * 3));
            Assert.That(stuck.Select(s => s.Aborts()), Has.All.EqualTo(1));
        }
    }

    [Test]
    public async Task SendTo_APreviousSendStillWriting_TheLaterMessageIsSentAfterItInsteadOfBeingDropped()
    {
        // Arrange：第一則寫得慢（但不是卡死），第二則在這段時間裡來了。
        var logs = new CapturedLogProvider();
        var hub = new FakeHubContext();
        var sender = new LiveSender(
            hub, new LiveOptions { SendTimeout = TimeSpan.FromSeconds(10) }, LoggerFactory.Create(b => b.AddProvider(logs)).CreateLogger<LiveSender>());
        var (connection, aborts) = NewConnection("slow");
        var gate = new TaskCompletionSource();
        hub.Gates["slow"] = gate;
        var first = sender.SendToAsync([connection], "first", new object(), CancellationToken.None);

        // Act
        await sender.SendToAsync([connection], "second", new object(), CancellationToken.None);
        gate.SetResult();
        await first;
        await Task.WhenAny(connection.PendingSend ?? Task.CompletedTask, Task.Delay(TimeSpan.FromSeconds(5)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hub.Sent.Select(m => m.Method), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(aborts(), Is.Zero);
        }
    }

    [Test]
    public async Task SendTo_AStuckConnectionWithLaterMessagesQueued_IsStillAbortedOnlyOnce()
    {
        // Arrange
        var setup = Create();
        var (stuck, aborts) = NewConnection("stuck");
        setup.Hub.HangingConnections.Add("stuck");
        var first = setup.Sender.SendToAsync([stuck], "items", new object(), CancellationToken.None);

        // Act
        await setup.Sender.SendToAsync([stuck], "items", new object(), CancellationToken.None);
        await first;

        // Assert
        Assert.That(aborts(), Is.EqualTo(1));
    }

    [Test]
    public async Task SendTo_AfterAConnectionFinishedItsLastSend_TheNextMessageGoesOut()
    {
        // Arrange
        var setup = Create();
        var (connection, _) = NewConnection("c");

        // Act
        await setup.Sender.SendToAsync([connection], "items", new object(), CancellationToken.None);
        await setup.Sender.SendToAsync([connection], "inbox", new object(), CancellationToken.None);

        // Assert
        Assert.That(setup.Hub.Sent.Select(m => m.Method), Is.EqualTo(new[] { "items", "inbox" }));
    }

    [Test]
    public async Task Send_SeveralMessagesForOneConnection_ArriveInOrder()
    {
        // Arrange
        var setup = Create();
        var (connection, _) = NewConnection("c");

        // Act
        await setup.Sender.SendAsync(
            [new LiveDelivery(connection, "a", new object()), new LiveDelivery(connection, "b", new object()), new LiveDelivery(connection, "c", new object())],
            CancellationToken.None);

        // Assert
        Assert.That(setup.Hub.Sent.Select(m => m.Method), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void SendTo_TheCallerCancels_PropagatesInsteadOfCountingAsATimeout()
    {
        // Arrange
        var setup = Create();
        var (stuck, aborts) = NewConnection("stuck");
        setup.Hub.HangingConnections.Add("stuck");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        // Act & Assert
        Assert.CatchAsync<OperationCanceledException>(() => setup.Sender.SendToAsync([stuck], "items", new object(), cancel.Token));
        Assert.That(aborts(), Is.Zero);
    }
}
