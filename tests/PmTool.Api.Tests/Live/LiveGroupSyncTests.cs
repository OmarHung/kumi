using System.Collections.Immutable;
using PmTool.Api.Live;

namespace PmTool.Api.Tests.Live;

/// <summary>群組同步的順序：離開時先停止被送到、再通知 SignalR；加入時先讓 SignalR 加進去、再開始被送到。</summary>
[TestFixture]
public sealed class LiveGroupSyncTests
{
    private static LiveConnection NewConnection() =>
        new("c", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "token", () => { });

    [Test]
    public async Task Apply_LeavingAGroup_NoLongerRecordsItBeforeSignalRIsTold()
    {
        // Arrange
        var hub = new FakeHubContext();
        var connection = NewConnection();
        connection.Groups = ["project:a", "project:b"];
        bool? stillRecordedWhenSignalRRemoved = null;
        hub.OnRemoveFromGroup = group => stillRecordedWhenSignalRRemoved = connection.Groups.Contains(group);

        // Act
        await new LiveGroupSync(hub).ApplyAsync(connection, ["project:a"], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stillRecordedWhenSignalRRemoved, Is.False);
            Assert.That(connection.Groups, Is.EquivalentTo(new[] { "project:a" }));
        }
    }

    [Test]
    public async Task Apply_JoiningAGroup_IsRecordedOnlyAfterSignalRHasIt()
    {
        // Arrange
        var hub = new FakeHubContext();
        var connection = NewConnection();
        bool? recordedWhenSignalRAdded = null;
        hub.OnAddToGroup = group => recordedWhenSignalRAdded = connection.Groups.Contains(group);

        // Act
        await new LiveGroupSync(hub).ApplyAsync(connection, ImmutableHashSet.Create("project:a"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recordedWhenSignalRAdded, Is.False);
            Assert.That(connection.Groups, Is.EquivalentTo(new[] { "project:a" }));
        }
    }
}
