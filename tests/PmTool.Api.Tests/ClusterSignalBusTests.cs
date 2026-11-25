using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PmTool.Abstractions;
using PmTool.Api.Cluster;

namespace PmTool.Api.Tests;

/// <summary>匯流排：先本機分送；別的程序送來的只分送；自己發的不重複收；重新接上時叫接收端整個作廢。</summary>
[TestFixture]
public sealed class ClusterSignalBusTests
{
    private sealed class Recorder : IClusterSignalHandler
    {
        private readonly List<ClusterSignal> _received = [];
        private int _resynchronized;

        public IReadOnlyList<ClusterSignal> Received
        {
            get
            {
                lock (_received)
                {
                    return [.. _received];
                }
            }
        }

        public int Resynchronized => Volatile.Read(ref _resynchronized);

        public Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken)
        {
            lock (_received)
            {
                _received.Add(signal);
            }

            return Task.CompletedTask;
        }

        public Task ResynchronizeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _resynchronized);
            return Task.CompletedTask;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private static (ClusterSignalBus Bus, ServiceProvider Provider) Create(params IClusterSignalHandler[] handlers) =>
        Create(null, ClusterSignalBus.QueueCapacity, handlers);

    private static (ClusterSignalBus Bus, ServiceProvider Provider) Create(
        TimeSpan? handlerTimeout, int laneCapacity, params IClusterSignalHandler[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        var provider = services.BuildServiceProvider();
        return (new ClusterSignalBus(
            provider, new ClusterSignalOptions(false), NullLogger<ClusterSignalBus>.Instance, null, handlerTimeout, laneCapacity), provider);
    }

    [Test]
    public async Task Publish_BeforeTheBusIsRunning_IsDeliveredOnceItStarts()
    {
        // Arrange
        var recorder = new Recorder();
        var (bus, provider) = Create(recorder);
        await using var _ = provider;

        // Act
        bus.Publish(new ClusterSignal(ClusterSignalKind.AccessChanged) { AllPeople = true });
        await bus.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => recorder.Received.Count > 0);
        await bus.StopAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recorder.Received, Has.Count.EqualTo(1));
            Assert.That(recorder.Received[0].Origin, Is.EqualTo(bus.Origin));
        }
    }

    [Test]
    public async Task Receive_FromAnotherProcess_IsDeliveredButTheOwnOriginIsIgnored()
    {
        // Arrange
        var recorder = new Recorder();
        var (bus, provider) = Create(recorder);
        await using var _ = provider;
        await bus.StartAsync(CancellationToken.None);
        var person = Guid.NewGuid();

        // Act
        bus.Receive(new ClusterSignal(ClusterSignalKind.InboxChanged) { PersonIds = [person], Origin = bus.Origin });
        bus.Receive(new ClusterSignal(ClusterSignalKind.InboxChanged) { PersonIds = [person], Origin = Guid.NewGuid() });
        await WaitUntilAsync(() => recorder.Received.Count > 0);
        await Task.Delay(50);
        await bus.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(recorder.Received.Select(s => s.PersonIds.Single()), Is.EqualTo(new[] { person }));
    }

    [Test]
    public async Task Publish_WhenOneHandlerThrows_TheOthersStillReceiveAndLaterSignalsStillFlow()
    {
        // Arrange
        var recorder = new Recorder();
        var (bus, provider) = Create(new ThrowingHandler(), recorder);
        await using var _ = provider;
        await bus.StartAsync(CancellationToken.None);

        // Act
        bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        await WaitUntilAsync(() => recorder.Received.Count == 2);
        await bus.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(recorder.Received, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Resynchronize_CallsEveryHandler()
    {
        // Arrange
        var first = new Recorder();
        var second = new Recorder();
        var (bus, provider) = Create(first, second);
        await using var _ = provider;

        // Act
        await bus.ResynchronizeAsync(CancellationToken.None);

        // Assert
        Assert.That((first.Resynchronized, second.Resynchronized), Is.EqualTo((1, 1)));
    }

    [Test]
    public async Task Publish_MoreThanTheQueueHolds_NeverThrowsAndDeliversTheNewest()
    {
        // Arrange：沒有啟動，佇列只會一直滿。
        var recorder = new Recorder();
        var (bus, provider) = Create(recorder);
        await using var _ = provider;
        var last = Guid.NewGuid();

        // Act
        var publishing = () =>
        {
            for (var i = 0; i < ClusterSignalBus.QueueCapacity + 10; i++)
            {
                bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged) { PersonIds = [i == ClusterSignalBus.QueueCapacity + 9 ? last : Guid.NewGuid()] });
            }
        };
        Assert.That(publishing, Throws.Nothing);
        await bus.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => recorder.Received.Count >= ClusterSignalBus.QueueCapacity);
        await bus.StopAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recorder.Received, Has.Count.EqualTo(ClusterSignalBus.QueueCapacity));
            Assert.That(recorder.Received[^1].PersonIds, Is.EqualTo(new[] { last }));
        }
    }

    /// <summary>第一則會卡住直到放行；記下拿到的權杖。</summary>
    private sealed class BlockingHandler(bool honorsCancellation) : IClusterSignalHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _resynchronized;
        private int _handled;

        public CancellationToken FirstToken { get; private set; }

        public int Resynchronized => Volatile.Read(ref _resynchronized);

        public int Handled => Volatile.Read(ref _handled);

        public void Release() => _release.TrySetResult();

        public async Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _handled) == 1)
            {
                FirstToken = cancellationToken;
                await (honorsCancellation ? _release.Task.WaitAsync(cancellationToken) : _release.Task);
            }
        }

        public Task ResynchronizeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _resynchronized);
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task Publish_WhenOneHandlerIsStuck_AnotherHandlerStillReceivesLaterSignalsPromptly()
    {
        // Arrange：卡住的接收端不理會取消，期限設得很長，證明別的接收端不是靠它逾時才輪到。
        var stuck = new BlockingHandler(honorsCancellation: false);
        var fast = new Recorder();
        var (bus, provider) = Create(TimeSpan.FromSeconds(30), ClusterSignalBus.QueueCapacity, stuck, fast);
        await using var _ = provider;
        await bus.StartAsync(CancellationToken.None);

        // Act
        for (var i = 0; i < 3; i++)
        {
            bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        }

        await WaitUntilAsync(() => fast.Received.Count == 3);
        var received = fast.Received.Count;
        stuck.Release();
        await bus.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(received, Is.EqualTo(3));
    }

    [Test]
    public async Task Publish_WhenAHandlerTimesOut_ItsTokenIsCancelledAndItsNextSignalStillArrives()
    {
        // Arrange
        var slow = new BlockingHandler(honorsCancellation: true);
        var (bus, provider) = Create(TimeSpan.FromMilliseconds(100), ClusterSignalBus.QueueCapacity, slow);
        await using var _ = provider;
        await bus.StartAsync(CancellationToken.None);

        // Act
        bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        await WaitUntilAsync(() => slow.Handled == 2);
        await bus.StopAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(slow.FirstToken.IsCancellationRequested, Is.True);
            Assert.That(slow.Handled, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Publish_WhenAHandlersQueueOverflows_ThatHandlerIsResynchronizedOnce()
    {
        // Arrange：佇列只有 4 格，第一則卡住時後面的塞滿。
        var slow = new BlockingHandler(honorsCancellation: false);
        var (bus, provider) = Create(TimeSpan.FromSeconds(30), 4, slow);
        await using var _ = provider;
        await bus.StartAsync(CancellationToken.None);
        bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        await WaitUntilAsync(() => slow.Handled == 1);

        // Act
        for (var i = 0; i < 20; i++)
        {
            bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        }

        await Task.Delay(300); // 讓分派的 pump 把 20 則都派完再放行，免得放行後又有新的塞進去。
        slow.Release();
        await WaitUntilAsync(() => slow.Resynchronized > 0);
        await Task.Delay(100);
        await bus.StopAsync(CancellationToken.None);

        // Assert：重新同步涵蓋了被清掉的訊號，所以處理的則數比發出的少。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(slow.Resynchronized, Is.EqualTo(1));
            Assert.That(slow.Handled, Is.LessThan(21));
        }
    }

    [Test]
    public async Task Stop_WhileAHandlerIsStuck_FinishesAndLeavesNoBackgroundWork()
    {
        // Arrange
        var stuck = new BlockingHandler(honorsCancellation: false);
        var (bus, provider) = Create(TimeSpan.FromSeconds(30), ClusterSignalBus.QueueCapacity, stuck);
        await using var _ = provider;
        await bus.StartAsync(CancellationToken.None);
        bus.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged));
        await WaitUntilAsync(() => stuck.Handled == 1);

        // Act
        var stopping = bus.StopAsync(CancellationToken.None);
        var finished = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(5))) == stopping;

        // Assert
        Assert.That(finished, Is.True);
    }

    private sealed class ThrowingHandler : IClusterSignalHandler
    {
        public Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken) => throw new InvalidOperationException("測試用的失敗");

        public Task ResynchronizeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
