using Microsoft.EntityFrameworkCore;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 還原到新的資料庫之後，讀者存的位置是舊資料庫的交易編號，比所有新事件都大。
/// 讀者要先請事件流換算，用換算後的位置比較，並把換算後的存回去。每個測試用自己的資料庫（讀者位置只有一筆）。
/// </summary>
[TestFixture]
public sealed class RestoredPositionTests
{
    private const long OldTransaction = 5160;
    private const long NewTransaction = 776;

    private static WorkEventPosition RestoreRule(WorkEventPosition stored) =>
        stored.TransactionId >= OldTransaction ? stored with { TransactionId = 1 } : stored;

    private static async Task StorePositionAsync(NotificationsTestHost host, WorkEventPosition position)
    {
        await using var db = await host.CreateDbContextAsync();
        db.ReaderPositions.Add(new ReaderPositionRow
        {
            Name = "notifications",
            TransactionId = position.TransactionId,
            Sequence = position.Sequence,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(long TransactionId, long Sequence)> StoredAsync(NotificationsTestHost host)
    {
        await using var db = await host.CreateDbContextAsync();
        var row = await db.ReaderPositions.AsNoTracking().SingleAsync(p => p.Name == "notifications");
        return (row.TransactionId, row.Sequence);
    }

    [Test]
    public async Task HandleAsync_StoredPositionFromTheOldDatabase_AppliesEventsWithSmallerTransactionIdsAndSavesTheNewPosition()
    {
        // Arrange：讀者存的是（5160、100），事件流換算成（1、100）；還原後的新事件是（776、101）。
        await using var host = await NotificationsTestHost.StartAsync(
            new PmTool.Abstractions.DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), null, null, new FakeWorkEventStream(RestoreRule));
        await StorePositionAsync(host, new WorkEventPosition(OldTransaction, 100));
        var item = EventBuilder.ForNewItem();
        Guid actor = Guid.CreateVersion7(), assignee = Guid.CreateVersion7();
        host.Access.Allow(item.ProjectId, [actor, assignee]);
        var created = item.Created(actor, assignee) with { Position = new WorkEventPosition(NewTransaction, 101) };

        // Act
        await host.Consumer.HandleAsync([created], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await StoredAsync(host), Is.EqualTo((NewTransaction, 101L)));
        }
    }

    [Test]
    public async Task GetPositionAsync_StoredPositionFromTheOldDatabase_ReturnsTheResolvedPositionAndStoresIt()
    {
        // Arrange
        await using var host = await NotificationsTestHost.StartAsync(
            new PmTool.Abstractions.DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), null, null, new FakeWorkEventStream(RestoreRule));
        await StorePositionAsync(host, new WorkEventPosition(OldTransaction, 100));

        // Act
        var position = await host.Consumer.GetPositionAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(position, Is.EqualTo(new WorkEventPosition(1, 100)));
            Assert.That(await StoredAsync(host), Is.EqualTo((1L, 100L)));
        }
    }

    [Test]
    public async Task HandleAsync_WithoutRestore_StillDropsEventsAtOrBeforeTheStoredPosition()
    {
        // Arrange：位置不需要換算；比它小的事件照舊當成已處理。
        await using var host = await NotificationsTestHost.StartAsync();
        var item = EventBuilder.ForNewItem();
        Guid actor = Guid.CreateVersion7(), assignee = Guid.CreateVersion7();
        host.Access.Allow(item.ProjectId, [actor, assignee]);
        var created = item.Created(actor, assignee);
        await StorePositionAsync(host, created.Position);

        // Act
        await host.Consumer.HandleAsync([created], CancellationToken.None);

        // Assert
        Assert.That(await host.KindsForAsync(assignee), Is.Empty);
        Assert.That(await StoredAsync(host), Is.EqualTo((created.Position.TransactionId, created.Position.Sequence)));
    }
}
