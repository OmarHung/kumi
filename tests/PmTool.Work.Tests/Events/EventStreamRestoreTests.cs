using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PmTool.Work.Tests.Events;

/// <summary>
/// 把備份還原到新的資料庫之後，新資料庫的交易編號從頭算起，比事件裡的、也比讀的人存的位置小。
/// 模擬的方法：把既有事件的交易編號調成遠大於現在的（像從另一個資料庫來的）。每個測試用自己的資料庫，不影響別人。
/// </summary>
[TestFixture]
public sealed class EventStreamRestoreTests
{
    private const long ForeignOffset = 5_000_000_000_000;
    private const string Titles = "一二三四五";

    private WorkTestHost _host = null!;
    private CapturedLogProvider _logs = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _logs = new CapturedLogProvider();
        _host = await WorkTestHost.StartAsync(services => services.AddLogging(b => b.AddProvider(_logs)));
    }

    [TearDown]
    public async Task StopAsync()
    {
        await _host.DisposeAsync();
        _logs.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    [Test]
    public async Task ReadAfterAsync_AfterRestoreFromTheReadersOldPosition_ReturnsOnlyEventsWrittenAfterTheRestore()
    {
        // Arrange：讀的人讀到歷史的最後一筆，存的是舊資料庫的位置；然後還原、再寫一筆新的。
        var scenario = await ScenarioWithHistoryAsync(3);
        var oldPosition = Foreign((await EventFeed.OfAsync(_host, scenario))[^1].Position);
        await SimulateRestoreAsync();
        await scenario.CreateTaskAsync("WEB", "還原後的項目");

        // Act
        var events = await _host.Events.ReadAfterAsync(oldPosition, 100, CancellationToken.None);

        // Assert
        Assert.That(events.Select(e => e.Item.Title), Is.EqualTo(new[] { "還原後的項目" }));
    }

    [Test]
    public async Task ReadAfterAsync_AfterRestoreFromTheStart_ReturnsHistoryInSequenceOrderThenNewEvents()
    {
        // Arrange
        var scenario = await ScenarioWithHistoryAsync(3);
        var before = (await EventFeed.OfAsync(_host, scenario)).Select(e => e.EventId).ToList();
        await SimulateRestoreAsync();
        await scenario.CreateTaskAsync("WEB", "還原後的項目");

        // Act
        var events = await EventFeed.OfAsync(_host, scenario);

        // Assert
        Assert.That(events.Select(e => e.EventId).Take(before.Count), Is.EqualTo(before));
        Assert.That(events[^1].Item.Title, Is.EqualTo("還原後的項目"));
        Assert.That(events.Select(e => e.Position), Is.Ordered);
        Assert.That(events.Select(e => e.Position.Sequence), Is.Ordered);
    }

    [Test]
    public async Task ReadAfterAsync_AfterRestoreWithTheReaderInTheMiddleOfHistory_ReturnsTheRestOfHistoryAndNewEvents()
    {
        // Arrange：歷史有 N 筆，讀的人讀到第 2 筆。
        var scenario = await ScenarioWithHistoryAsync(4);
        var history = await EventFeed.OfAsync(_host, scenario);
        var oldPosition = Foreign(history[1].Position);
        await SimulateRestoreAsync();
        await scenario.CreateTaskAsync("WEB", "還原後的項目");

        // Act
        var events = await _host.Events.ReadAfterAsync(oldPosition, 100, CancellationToken.None);

        // Assert
        Assert.That(
            events.Select(e => e.EventId),
            Is.EqualTo(history.Skip(2).Select(e => e.EventId).Concat(new[] { events[^1].EventId })));
        Assert.That(events[^1].Item.Title, Is.EqualTo("還原後的項目"));
    }

    [Test]
    public async Task ReadAfterAsync_AfterRestoreAndANormalizedPosition_ContinuesWithoutRereadingAnything()
    {
        // Arrange：還原後第一次讀拿到歷史，用它回傳的位置接著讀。
        var scenario = await ScenarioWithHistoryAsync(2);
        await SimulateRestoreAsync();
        var first = await EventFeed.OfAsync(_host, scenario);
        await scenario.CreateTaskAsync("WEB", "還原後的項目");

        // Act
        var next = await _host.Events.ReadAfterAsync(first[^1].Position, 100, CancellationToken.None);

        // Assert
        Assert.That(next.Select(e => e.Item.Title), Is.EqualTo(new[] { "還原後的項目" }));
    }

    [Test]
    public async Task ReadAfterAsync_WithoutRestore_LeavesStoredTransactionIdsAlone()
    {
        // Arrange
        var scenario = await ScenarioWithHistoryAsync(2);
        var before = await EventFeed.OfAsync(_host, scenario);

        // Act
        var after = await EventFeed.OfAsync(_host, scenario);

        // Assert
        Assert.That(after.Select(e => e.Position), Is.EqualTo(before.Select(e => e.Position)));
        Assert.That(after.Select(e => e.Position.TransactionId), Has.All.GreaterThan(1));
        Assert.That(_logs.Warnings, Is.Empty);
    }

    [Test]
    public async Task ReadAfterAsync_TwoReadersAtTheSameTimeAfterRestore_BothSucceedWithTheSameResult()
    {
        // Arrange
        var scenario = await ScenarioWithHistoryAsync(4);
        await SimulateRestoreAsync();
        await scenario.CreateTaskAsync("WEB", "還原後的項目");

        // Act
        var reads = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Task.Run(() => _host.Events.ReadAfterAsync(WorkEventPosition.Start, 10_000, CancellationToken.None))));

        // Assert
        var expected = reads[0].Select(e => e.EventId).ToList();
        Assert.That(reads.Select(r => r.Select(e => e.EventId)), Has.All.EqualTo(expected));
        Assert.That(_logs.Warnings, Has.Count.EqualTo(1), "只有搶到鎖的那一個做了歸一");
    }

    [Test]
    public async Task ResolvePositionAsync_PositionFromTheOldDatabase_ReturnsTransactionOneWithTheSameSequence()
    {
        // Arrange
        var scenario = await ScenarioWithHistoryAsync(1);
        var stored = Foreign((await EventFeed.OfAsync(_host, scenario))[^1].Position);

        // Act
        var resolved = await _host.Events.ResolvePositionAsync(stored, CancellationToken.None);

        // Assert
        Assert.That(resolved, Is.EqualTo(new WorkEventPosition(1, stored.Sequence)));
    }

    [Test]
    public async Task ResolvePositionAsync_OrdinaryPositionAndStart_AreReturnedUnchanged()
    {
        // Arrange
        var scenario = await ScenarioWithHistoryAsync(1);
        var ordinary = (await EventFeed.OfAsync(_host, scenario))[^1].Position;

        // Act
        var resolvedOrdinary = await _host.Events.ResolvePositionAsync(ordinary, CancellationToken.None);
        var resolvedStart = await _host.Events.ResolvePositionAsync(WorkEventPosition.Start, CancellationToken.None);

        // Assert
        Assert.That(resolvedOrdinary, Is.EqualTo(ordinary));
        Assert.That(resolvedStart, Is.EqualTo(WorkEventPosition.Start));
    }

    private static WorkEventPosition Foreign(WorkEventPosition position) => position with { TransactionId = position.TransactionId + ForeignOffset };

    private async Task<Scenario> ScenarioWithHistoryAsync(int tasks)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        for (var i = 0; i < tasks; i++)
        {
            await scenario.CreateTaskAsync("WEB", Titles[i].ToString());
        }

        return scenario;
    }

    private async Task SimulateRestoreAsync()
    {
        await using var connection = new NpgsqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"UPDATE work.events SET transaction_id = ((transaction_id::text::bigint) + {ForeignOffset})::text::xid8", connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class CapturedLogProvider : ILoggerProvider
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return [.. _warnings];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose()
        {
        }

        private sealed class Capture(CapturedLogProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel != LogLevel.Warning)
                {
                    return;
                }

                lock (owner._warnings)
                {
                    owner._warnings.Add(formatter(state, exception));
                }
            }
        }
    }
}
