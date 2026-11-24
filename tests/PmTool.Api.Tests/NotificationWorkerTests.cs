using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Api.Composition;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>背景處理：把事件流的新事件交給每個讀者；一個讀者出錯不擋後面的，也不讓程序掛掉。</summary>
[TestFixture]
public sealed class NotificationWorkerTests
{
    private static WorkEvent EventAt(long sequence) => new(
        Guid.CreateVersion7(),
        new WorkEventPosition(1, sequence),
        WorkEventKind.ItemCreated,
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        DateTimeOffset.UtcNow,
        new ItemEventData(Guid.CreateVersion7(), "WEB", Guid.CreateVersion7(), "WEB-1", "範例", Guid.CreateVersion7(), null, false, null, null, null, null, [], []));

    private static (NotificationWorker Worker, CapturingLoggerProvider Logs, ServiceProvider Services) Create(
        StubStream? stream, params StubConsumer[] consumers) => Create(stream, null, consumers);

    private static (NotificationWorker Worker, CapturingLoggerProvider Logs, ServiceProvider Services) Create(
        StubStream? stream, StubMigrationStatus? migrations, params StubConsumer[] consumers)
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        if (stream is not null)
        {
            services.AddSingleton<IWorkEventStream>(stream);
        }

        foreach (var consumer in consumers)
        {
            services.AddSingleton<IWorkEventConsumer>(consumer);
        }

        if (migrations is not null)
        {
            services.AddSingleton<IMigrationStatus>(migrations);
        }

        var provider = services.BuildServiceProvider();
        var worker = new NotificationWorker(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<ILogger<NotificationWorker>>());
        return (worker, logs, provider);
    }

    [Test]
    public async Task RunOnce_NewEvents_AreReadFromTheConsumersPositionAndHandedOver()
    {
        // Arrange
        var stream = new StubStream([EventAt(1), EventAt(2), EventAt(3)]);
        var consumer = new StubConsumer("a") { Position = new WorkEventPosition(1, 1) };
        var (worker, _, services) = Create(stream, consumer);
        await using var _ = services;

        // Act
        var handed = await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handed, Is.EqualTo(2));
            Assert.That(consumer.Handled.Select(e => e.Position.Sequence), Is.EqualTo(new long[] { 2, 3 }));
            Assert.That(stream.Reads.First(), Is.EqualTo(new WorkEventPosition(1, 1)));
        }
    }

    [Test]
    public async Task RunOnce_EveryConsumerGetsTheEventsFromItsOwnPosition()
    {
        // Arrange：一個落後、一個已經追上。
        var stream = new StubStream([EventAt(1), EventAt(2)]);
        var behind = new StubConsumer("behind");
        var caughtUp = new StubConsumer("caught-up") { Position = new WorkEventPosition(1, 2) };
        var (worker, _, services) = Create(stream, behind, caughtUp);
        await using var _ = services;

        // Act
        await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(behind.Handled, Has.Count.EqualTo(2));
            Assert.That(caughtUp.Handled, Is.Empty);
        }
    }

    [Test]
    public async Task RunOnce_MoreThanOneBatchBehind_CatchesUpInTheSameRun()
    {
        // Arrange
        var total = (NotificationWorker.BatchSize * 2) + 5;
        var stream = new StubStream([.. Enumerable.Range(1, total).Select(i => EventAt(i))]);
        var consumer = new StubConsumer("a");
        var (worker, _, services) = Create(stream, consumer);
        await using var _ = services;

        // Act
        var handed = await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handed, Is.EqualTo(total));
            Assert.That(consumer.HandleCalls, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task RunOnce_NothingNew_DoesNotCallTheConsumer()
    {
        // Arrange
        var consumer = new StubConsumer("a");
        var (worker, _, services) = Create(new StubStream([]), consumer);
        await using var _ = services;

        // Act
        var handed = await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handed, Is.Zero);
            Assert.That(consumer.HandleCalls, Is.Zero);
        }
    }

    [Test]
    public async Task RunOnce_OneConsumerThrows_IsLoggedAsErrorAndTheNextOneStillRuns()
    {
        // Arrange
        var failing = new StubConsumer("failing") { FailWith = new InvalidOperationException("處理失敗") };
        var healthy = new StubConsumer("healthy");
        var (worker, logs, services) = Create(new StubStream([EventAt(1)]), failing, healthy);
        await using var _ = services;

        // Act
        var run = () => worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That(run, Throws.Nothing);
        var error = logs.Entries.Single(entry => entry.Level == LogLevel.Error);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(healthy.Handled, Has.Count.EqualTo(1));
            Assert.That(error.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(error.Properties["Consumer"], Is.EqualTo("failing"));
        }
    }

    [Test]
    public async Task RunOnce_FailedConsumer_TriesAgainFromTheSamePositionNextRun()
    {
        // Arrange
        var consumer = new StubConsumer("a") { FailWith = new InvalidOperationException("暫時失敗") };
        var (worker, _, services) = Create(new StubStream([EventAt(1)]), consumer);
        await using var _ = services;
        await worker.RunOnceAsync(CancellationToken.None);
        consumer.FailWith = null;

        // Act
        await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That(consumer.Handled.Select(e => e.Position.Sequence), Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task RunOnce_StreamThrows_IsLoggedAsError()
    {
        // Arrange
        var stream = new StubStream([]) { FailWith = new InvalidOperationException("讀事件失敗") };
        var consumer = new StubConsumer("a");
        var (worker, logs, services) = Create(stream, consumer);
        await using var _ = services;

        // Act
        var run = () => worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That(run, Throws.Nothing);
        Assert.That(logs.Entries.Count(entry => entry.Level == LogLevel.Error), Is.EqualTo(1));
    }

    [Test]
    public async Task RunOnce_NoEventStreamRegistered_IsLoggedAsErrorWithoutThrowing()
    {
        // Arrange
        var (worker, logs, services) = Create(null, new StubConsumer("a"));
        await using var _ = services;

        // Act
        var run = () => worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That(run, Throws.Nothing);
        Assert.That(logs.Entries.Count(entry => entry.Level == LogLevel.Error), Is.EqualTo(1));
    }

    [Test]
    public async Task RunOnce_Cancelled_PropagatesInsteadOfLoggingAnError()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var (worker, logs, services) = Create(new StubStream([EventAt(1)]), new StubConsumer("a"));
        await using var _ = services;

        // Act
        var run = () => worker.RunOnceAsync(cancellation.Token);

        // Assert
        Assert.That(run, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(logs.Entries.Where(entry => entry.Level == LogLevel.Error), Is.Empty);
    }

    [Test]
    public async Task Start_RunsRightAwayAndStopsCleanly()
    {
        // Arrange
        var consumer = new StubConsumer("a");
        var (worker, _, services) = Create(new StubStream([EventAt(1)]), consumer);
        await using var _ = services;

        // Act
        await worker.StartAsync(CancellationToken.None);
        await consumer.FirstHandled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stop = () => worker.StopAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stop, Throws.Nothing);
            Assert.That(consumer.Handled, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task RunOnce_MigrationsPending_SkipsTheRoundAndLogsOneWarningNoMatterHowManyRounds()
    {
        // Arrange：Database:MigrateOnStartup=false，遷移還沒跑。
        var migrations = new StubMigrationStatus("20261118_AddWorkEvents");
        var consumer = new StubConsumer("a");
        var stream = new StubStream([EventAt(1)]);
        var (worker, logs, services) = Create(stream, migrations, consumer);
        await using var _ = services;

        // Act
        var handed = new[]
        {
            await worker.RunOnceAsync(CancellationToken.None),
            await worker.RunOnceAsync(CancellationToken.None),
            await worker.RunOnceAsync(CancellationToken.None),
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handed, Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(consumer.HandleCalls, Is.Zero);
            Assert.That(stream.Reads, Is.Empty);
            Assert.That(logs.Entries.Count(entry => entry.Level == LogLevel.Warning), Is.EqualTo(1));
            Assert.That(logs.Entries.Where(entry => entry.Level >= LogLevel.Error), Is.Empty);
        }
    }

    [Test]
    public async Task RunOnce_MigrationsFinish_ResumesAndWarnsAgainIfTheyAreNeededLater()
    {
        // Arrange
        var migrations = new StubMigrationStatus("m1");
        var consumer = new StubConsumer("a");
        var (worker, logs, services) = Create(new StubStream([EventAt(1)]), migrations, consumer);
        await using var _ = services;
        await worker.RunOnceAsync(CancellationToken.None);

        // Act
        migrations.Pending = [];
        var resumed = await worker.RunOnceAsync(CancellationToken.None);
        migrations.Pending = ["m2"];
        await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resumed, Is.EqualTo(1));
            Assert.That(logs.Entries.Count(entry => entry.Level == LogLevel.Warning), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task RunOnce_MigrationStatusThrows_IsLoggedAsError()
    {
        // Arrange：連不上資料庫是真的錯誤，不是「還沒遷移」。
        var migrations = new StubMigrationStatus { FailWith = new InvalidOperationException("連不上") };
        var (worker, logs, services) = Create(new StubStream([EventAt(1)]), migrations, new StubConsumer("a"));
        await using var _ = services;

        // Act
        await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That(logs.Entries.Count(entry => entry.Level == LogLevel.Error), Is.EqualTo(1));
    }

    private sealed class StubMigrationStatus(params string[] pending) : IMigrationStatus
    {
        public IReadOnlyList<string> Pending { get; set; } = pending;

        public Exception? FailWith { get; set; }

        public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken) =>
            FailWith is { } failure ? throw failure : Task.FromResult(Pending);
    }

    private sealed class StubStream(IReadOnlyList<WorkEvent> all) : IWorkEventStream
    {
        private readonly List<WorkEventPosition> _reads = [];

        public Exception? FailWith { get; set; }

        public IReadOnlyList<WorkEventPosition> Reads
        {
            get
            {
                lock (_reads)
                {
                    return [.. _reads];
                }
            }
        }

        public Task<WorkEventPosition> ResolvePositionAsync(WorkEventPosition stored, CancellationToken cancellationToken) => Task.FromResult(stored);

        public Task<WorkEventPosition> GetLatestPositionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(all.Count == 0 ? WorkEventPosition.Start : all[^1].Position);

        public Task<IReadOnlyList<WorkEvent>> ReadAfterAsync(WorkEventPosition after, int limit, CancellationToken cancellationToken)
        {
            if (FailWith is { } failure)
            {
                throw failure;
            }

            lock (_reads)
            {
                _reads.Add(after);
            }

            return Task.FromResult<IReadOnlyList<WorkEvent>>([.. all.Where(e => e.Position.CompareTo(after) > 0).OrderBy(e => e.Position).Take(limit)]);
        }
    }

    private sealed class StubConsumer(string name) : IWorkEventConsumer
    {
        private readonly List<WorkEvent> _handled = [];
        private int _handleCalls;

        public TaskCompletionSource FirstHandled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkEventPosition Position { get; set; } = WorkEventPosition.Start;

        public Exception? FailWith { get; set; }

        public string Name => name;

        public int HandleCalls => Volatile.Read(ref _handleCalls);

        public IReadOnlyList<WorkEvent> Handled
        {
            get
            {
                lock (_handled)
                {
                    return [.. _handled];
                }
            }
        }

        public Task<WorkEventPosition> GetPositionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Position);
        }

        public Task HandleAsync(IReadOnlyList<WorkEvent> events, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _handleCalls);
            if (FailWith is { } failure)
            {
                throw failure;
            }

            lock (_handled)
            {
                _handled.AddRange(events);
            }

            Position = events[^1].Position;
            FirstHandled.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
