using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Api.Composition;
using PmTool.Api.Tests.Support;
using PmTool.Notifications;

namespace PmTool.Api.Tests;

/// <summary>寄信的背景工作：註冊的開關、一輪做什麼、出錯時不掛掉。</summary>
[TestFixture]
public sealed class EmailWorkerTests
{
    private static int WorkerCount(ApiFactory factory) =>
        factory.Services.GetServices<IHostedService>().OfType<EmailWorker>().Count();

    private static (EmailWorker Worker, CapturingLoggerProvider Logs, ServiceProvider Services, StubDispatcher Dispatcher) Create(
        StubMigrationStatus? migrations = null)
    {
        var logs = new CapturingLoggerProvider();
        var dispatcher = new StubDispatcher();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddScoped<IEmailDispatcher>(_ => dispatcher);
        if (migrations is not null)
        {
            services.AddSingleton<IMigrationStatus>(migrations);
        }

        var provider = services.BuildServiceProvider();
        var worker = new EmailWorker(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<ILogger<EmailWorker>>());
        return (worker, logs, provider, dispatcher);
    }

    [Test]
    public void Worker_NotConfigured_IsOffInTheTestFactoryBecauseItSetsItToFalse()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act & Assert：ApiFactory 預設關掉，測試用假的模組。
        Assert.That(WorkerCount(factory), Is.Zero);
    }

    [Test]
    public void Worker_SettingTrue_RunsAsAHostedService()
    {
        // Arrange
        var settings = new Dictionary<string, string?> { [EmailWorker.EnabledSetting] = "true" };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act & Assert
        Assert.That(WorkerCount(factory), Is.EqualTo(1));
    }

    [Test]
    public void Worker_SettingNotABoolean_FailsTheStartup()
    {
        // Arrange
        var settings = new Dictionary<string, string?> { [EmailWorker.EnabledSetting] = "maybe" };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act & Assert：設定錯了要在啟動時就知道。
        Assert.That(() => factory.Services, Throws.InvalidOperationException);
    }

    [Test]
    public void Worker_TheNotificationWorkerSettingDoesNotControlIt()
    {
        // Arrange：兩個背景工作各有自己的開關。
        var settings = new Dictionary<string, string?>
        {
            [NotificationWorker.EnabledSetting] = "true",
            [EmailWorker.EnabledSetting] = "false",
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerCount(factory), Is.Zero);
            Assert.That(factory.Services.GetServices<IHostedService>().OfType<NotificationWorker>().Count(), Is.EqualTo(1));
        }
    }

    [Test]
    public void Interval_IsThirtySeconds() => Assert.That(EmailWorker.Interval, Is.EqualTo(TimeSpan.FromSeconds(30)));

    [Test]
    public async Task RunOnce_CallsTheDispatcherAndReturnsHowManyMailsWereSent()
    {
        // Arrange
        var (worker, _, services, dispatcher) = Create();
        await using var _ = services;
        dispatcher.Result = 3;

        // Act
        var sent = await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(3));
            Assert.That(dispatcher.Calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task RunOnce_MigrationsPending_SkipsTheRoundAndWarnsOnlyOnce()
    {
        // Arrange
        var (worker, logs, services, dispatcher) = Create(new StubMigrationStatus("20261121133638_AddEmailNotifications"));
        await using var _ = services;

        // Act
        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Calls, Is.Zero);
            Assert.That(logs.Entries.Count(e => e.Level == LogLevel.Warning), Is.EqualTo(1));
            Assert.That(logs.Lines.Single(line => line.StartsWith("Warning", StringComparison.Ordinal)), Does.Contain("20261121133638_AddEmailNotifications"));
        }
    }

    [Test]
    public async Task RunOnce_MigrationsDoneAfterwards_ResumesAndWarnsAgainIfTheyComeBack()
    {
        // Arrange
        var migrations = new StubMigrationStatus("pending");
        var (worker, logs, services, dispatcher) = Create(migrations);
        await using var _ = services;
        await worker.RunOnceAsync(CancellationToken.None);
        migrations.Pending = [];

        // Act
        await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Calls, Is.EqualTo(1));
            Assert.That(logs.Entries.Count(e => e.Level == LogLevel.Warning), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task RunOnce_DispatcherThrows_LogsAnErrorAndDoesNotThrow()
    {
        // Arrange
        var (worker, logs, services, dispatcher) = Create();
        await using var _ = services;
        dispatcher.FailWith = new InvalidOperationException("資料庫斷了");

        // Act
        var sent = await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.Zero);
            Assert.That(logs.Entries.Single(e => e.Level == LogLevel.Error).Exception?.Message, Is.EqualTo("資料庫斷了"));
        }
    }

    [Test]
    public async Task RunOnce_Cancelled_PropagatesInsteadOfLoggingAnError()
    {
        // Arrange
        var (worker, logs, services, dispatcher) = Create();
        await using var _ = services;
        dispatcher.FailWith = new OperationCanceledException();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act & Assert
        Assert.That(async () => await worker.RunOnceAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(logs.Entries.Where(e => e.Level == LogLevel.Error), Is.Empty);
    }

    private sealed class StubDispatcher : IEmailDispatcher
    {
        private int _calls;

        public int Result { get; set; }

        public Exception? FailWith { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public Task<int> DispatchAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return FailWith is { } failure ? throw failure : Task.FromResult(Result);
        }
    }

    private sealed class StubMigrationStatus(params string[] pending) : IMigrationStatus
    {
        public IReadOnlyList<string> Pending { get; set; } = pending;

        public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken) => Task.FromResult(Pending);
    }
}
