using Npgsql;

namespace PmTool.Work.Tests.Events;

/// <summary>「從現在起」的起點：即時推送不必從頭讀過所有的歷史。</summary>
[TestFixture]
public sealed class EventStreamLatestPositionTests
{
    private const int Everything = 10_000;

    private WorkTestHost _host = null!;

    [SetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [TearDown]
    public async Task StopAsync()
    {
        await _host.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    [Test]
    public async Task GetLatestPositionAsync_NoEvents_IsStart()
    {
        // Act
        var latest = await _host.Events.GetLatestPositionAsync(CancellationToken.None);

        // Assert
        Assert.That(latest, Is.EqualTo(WorkEventPosition.Start));
    }

    [Test]
    public async Task GetLatestPositionAsync_AfterWrites_IsThePositionOfTheLastReadableEvent()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "第一個");
        await scenario.CreateTaskAsync("WEB", "第二個");

        // Act
        var latest = await _host.Events.GetLatestPositionAsync(CancellationToken.None);
        var all = await _host.Events.ReadAfterAsync(WorkEventPosition.Start, Everything, CancellationToken.None);

        // Assert
        Assert.That(latest, Is.EqualTo(all[^1].Position));
    }

    [Test]
    public async Task ReadAfterAsync_FromTheLatestPosition_OnlyGetsEventsWrittenLater()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "之前的");
        var latest = await _host.Events.GetLatestPositionAsync(CancellationToken.None);
        await scenario.CreateTaskAsync("WEB", "之後的");

        // Act
        var events = await _host.Events.ReadAfterAsync(latest, Everything, CancellationToken.None);

        // Assert
        Assert.That(events.Select(e => e.Item.Title), Is.EqualTo(new[] { "之後的" }));
    }
}
