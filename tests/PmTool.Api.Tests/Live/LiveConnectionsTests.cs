using PmTool.Api.Live;

namespace PmTool.Api.Tests.Live;

/// <summary>連線的登記：一個人最多 20 條，同時進來的也一樣。</summary>
[TestFixture]
public sealed class LiveConnectionsTests
{
    private static LiveConnection Connection(Guid person, string id) => new(id, Guid.CreateVersion7(), person, "token", () => { });

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
}
