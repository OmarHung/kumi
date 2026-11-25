using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Api.Cluster;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>跨程序訊號的本機分送：發出的訊號立刻交給這個程序裡的每個接收端，接收端出錯不影響發送的人與其他接收端。</summary>
[TestFixture]
public sealed class ClusterSignalRegistrationTests
{
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private sealed class RecordingHandler : IClusterSignalHandler
    {
        public List<ClusterSignal> Received { get; } = [];

        public Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken)
        {
            lock (Received)
            {
                Received.Add(signal);
            }

            return Task.CompletedTask;
        }

        public Task ResynchronizeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingHandler(bool isAsync) : IClusterSignalHandler
    {
        public Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken) =>
            isAsync ? Task.FromException(new InvalidOperationException("測試用的失敗")) : throw new InvalidOperationException("測試用的失敗");

        public Task ResynchronizeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Test]
    public async Task Publish_WithHandlers_DeliversTheSignalToEachOfThemWithAnOrigin()
    {
        // Arrange
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions
            {
                ConfigureServices = services =>
                {
                    services.AddSingleton<IClusterSignalHandler>(first);
                    services.AddSingleton<IClusterSignalHandler>(second);
                },
            });
        var signals = factory.Services.GetRequiredService<IClusterSignals>();
        var account = Guid.NewGuid();

        // Act
        signals.Publish(new ClusterSignal(ClusterSignalKind.CredentialsRevoked) { AccountId = account });
        await WaitUntilAsync(() => first.Received.Count > 0 && second.Received.Count > 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Received.Select(s => s.AccountId), Is.EqualTo(new Guid?[] { account }));
            Assert.That(second.Received, Has.Count.EqualTo(1));
            Assert.That(first.Received[0].Origin, Is.Not.EqualTo(Guid.Empty));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Publish_WhenAHandlerFails_DoesNotThrowAndTheOtherHandlersStillReceiveIt(bool isAsync)
    {
        // Arrange
        var recording = new RecordingHandler();
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions
            {
                ConfigureServices = services =>
                {
                    services.AddSingleton<IClusterSignalHandler>(new ThrowingHandler(isAsync));
                    services.AddSingleton<IClusterSignalHandler>(recording);
                },
            });
        var signals = factory.Services.GetRequiredService<IClusterSignals>();

        // Act
        var publish = () => signals.Publish(new ClusterSignal(ClusterSignalKind.AccessChanged) { AllPeople = true });

        // Assert
        Assert.That(publish, Throws.Nothing);
        await WaitUntilAsync(() => recording.Received.Count > 0);
        Assert.That(recording.Received, Has.Count.EqualTo(1));
    }

    [Test]
    public void Publish_WithoutExtraHandlers_ReachesTheIdentityModulesSessionCacheHandlerWithoutFailing()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        var publish = () => factory.Services.GetRequiredService<IClusterSignals>()
            .Publish(new ClusterSignal(ClusterSignalKind.InboxChanged) { PersonIds = [Guid.NewGuid()] });

        // Assert
        Assert.That(publish, Throws.Nothing);
    }

    [Test]
    public void AddClusterSignals_NothingConfigured_IsEnabled()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        using var provider = services.AddClusterSignals(configuration).BuildServiceProvider();

        // Assert
        Assert.That(provider.GetRequiredService<ClusterSignalOptions>().Enabled, Is.True);
    }

    [Test]
    public async Task AddClusterSignals_Disabled_IsDisabledAndStillDeliversLocally()
    {
        // Arrange
        var recording = new RecordingHandler();
        var services = new ServiceCollection().AddLogging().AddSingleton<IClusterSignalHandler>(recording);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([KeyValuePair.Create<string, string?>(ClusterRegistration.EnabledSetting, "false")])
            .Build();

        // Act
        await using var provider = services.AddClusterSignals(configuration).BuildServiceProvider();
        var bus = provider.GetRequiredService<ClusterSignalBus>();
        await bus.StartAsync(CancellationToken.None);
        provider.GetRequiredService<IClusterSignals>().Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        await WaitUntilAsync(() => recording.Received.Count > 0);
        await bus.StopAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetRequiredService<ClusterSignalOptions>().Enabled, Is.False);
            Assert.That(recording.Received, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void TestFactory_ByDefault_KeepsTheClusterSignalsOffTheDatabase()
    {
        // Arrange、Act
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Assert
        Assert.That(factory.Services.GetRequiredService<ClusterSignalOptions>().Enabled, Is.False);
    }
}
