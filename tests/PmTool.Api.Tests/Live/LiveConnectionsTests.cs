using PmTool.Api.Live;

namespace PmTool.Api.Tests.Live;

/// <summary>連線的登記：一個人最多 20 條，同時進來的也一樣。</summary>
[TestFixture]
public sealed class LiveConnectionsTests
{
    private static LiveConnection Connection(Guid person, string id) => new(id, Guid.CreateVersion7(), person, Guid.CreateVersion7(), "token", () => { });

    [Test]
    public void TryAdd_BeyondTheLimit_IsRefusedForThatPersonOnly()
    {
        // Arrange
        var connections = new LiveConnections();
        var person = Guid.CreateVersion7();
        var accepted = Enumerable.Range(0, LiveConnections.MaxPerPerson).Select(i => connections.TryAdd(Connection(person, $"c{i}"))).ToArray();

        // Act
        var extra = connections.TryAdd(Connection(person, "extra"));
        var someoneElse = connections.TryAdd(Connection(Guid.CreateVersion7(), "other"));

        // Assert
        Assert.That(accepted, Has.All.True);
        Assert.That((extra, someoneElse), Is.EqualTo((false, true)));
        Assert.That(connections.IsFull(person), Is.True);
    }

    [Test]
    public void Remove_FreesASlot_AndRemovingTwiceOrAnUnknownOneDoesNothing()
    {
        // Arrange
        var connections = new LiveConnections();
        var person = Guid.CreateVersion7();
        for (var i = 0; i < LiveConnections.MaxPerPerson; i++)
        {
            connections.TryAdd(Connection(person, $"c{i}"));
        }

        // Act
        connections.Remove("c0");
        connections.Remove("c0");
        connections.Remove("never-existed");

        // Assert
        Assert.That(connections.CountOf(person), Is.EqualTo(LiveConnections.MaxPerPerson - 1));
        Assert.That(connections.TryAdd(Connection(person, "again")), Is.True);
    }

    [Test]
    public void TryAdd_TheSameConnectionTwice_CountsOnce()
    {
        // Arrange
        var connections = new LiveConnections();
        var person = Guid.CreateVersion7();

        // Act
        connections.TryAdd(Connection(person, "same"));
        connections.TryAdd(Connection(person, "same"));

        // Assert
        Assert.That(connections.CountOf(person), Is.EqualTo(1));
    }

    [Test]
    public async Task TryAdd_ManyAtTheSameTime_NeverGoesOverTheLimit()
    {
        // Arrange
        var connections = new LiveConnections();
        var person = Guid.CreateVersion7();

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, 200).Select(i => Task.Run(() => connections.TryAdd(Connection(person, $"c{i}")))));

        // Assert
        Assert.That(results.Count(accepted => accepted), Is.EqualTo(LiveConnections.MaxPerPerson));
        Assert.That(connections.CountOf(person), Is.EqualTo(LiveConnections.MaxPerPerson));
    }

    [Test]
    public void Snapshot_IsACopyThatLaterChangesDoNotTouch()
    {
        // Arrange
        var connections = new LiveConnections();
        connections.TryAdd(Connection(Guid.CreateVersion7(), "a"));
        var snapshot = connections.Snapshot();

        // Act
        connections.Remove("a");

        // Assert
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.That(connections.Snapshot(), Is.Empty);
    }

    [Test]
    public void TryEnqueue_WhileSending_QueuesUpToTheLimitThenSkipsAndAbortsOnlyOnceTheWriteIsOld()
    {
        // Arrange：送出一直沒完成。
        var connection = Connection(Guid.CreateVersion7(), "c");
        var timeout = TimeSpan.FromSeconds(5);
        var start = DateTimeOffset.UtcNow;
        IReadOnlyList<LiveDelivery> batch = [new LiveDelivery(connection, "items", new object())];
        var first = connection.TryEnqueue(
            batch,
            () =>
            {
                connection.TryTakeNext(out _); // 送出的工作先取走第一批，之後卡在寫入。
                return new TaskCompletionSource().Task;
            },
            timeout,
            start,
            out _);

        // Act
        var queued = Enumerable.Range(0, LiveConnection.MaxBacklog)
            .Select(index => connection.TryEnqueue(batch, () => throw new InvalidOperationException(), timeout, start, out Task? ignored)).ToList();
        var full = connection.TryEnqueue(batch, () => throw new InvalidOperationException(), timeout, start.AddSeconds(1), out _);
        var fullAndOld = connection.TryEnqueue(batch, () => throw new InvalidOperationException(), timeout, start.AddSeconds(6), out _);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(SendStart.Started));
            Assert.That(queued, Has.All.EqualTo(SendStart.Queued));
            Assert.That(full, Is.EqualTo(SendStart.Busy));
            Assert.That(fullAndOld, Is.EqualTo(SendStart.TimedOut));
            Assert.That(connection.IsBacklogged, Is.True);
        }
    }

    [Test]
    public void TryTakeNext_WhenTheBacklogIsEmpty_EndsTheSendSoTheNextBatchStartsANewOne()
    {
        // Arrange
        var connection = Connection(Guid.CreateVersion7(), "c");
        IReadOnlyList<LiveDelivery> batch = [new LiveDelivery(connection, "items", new object())];
        connection.TryEnqueue(batch, () => Task.CompletedTask, TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow, out _);

        // Act
        var tookFirst = connection.TryTakeNext(out _);
        var tookSecond = connection.TryTakeNext(out _);
        var next = connection.TryEnqueue(batch, () => Task.CompletedTask, TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow, out _);

        // Assert
        Assert.That((tookFirst, tookSecond, next), Is.EqualTo((true, false, SendStart.Started)));
    }
}
