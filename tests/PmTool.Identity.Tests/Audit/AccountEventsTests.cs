using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PmTool.Identity.Audit;

namespace PmTool.Identity.Tests.Audit;

/// <summary>帳號事件的發送：盡力送，任何接收者的問題都不能影響已經完成的操作。</summary>
[TestFixture]
public sealed class AccountEventsTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(300);

    private static AccountEvent SampleEvent() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), AccountEventKind.AccessTokenCreated, DateTimeOffset.UnixEpoch);

    [Test]
    public async Task PublishAsync_SeveralSinks_AllReceiveTheEvent()
    {
        // Arrange
        var first = new RecordingAccountEventSink();
        var second = new RecordingAccountEventSink();
        var events = new AccountEvents([first, second], new CapturingLogger(), ShortTimeout);
        var sample = SampleEvent();

        // Act
        await events.PublishAsync(sample);

        // Assert
        Assert.That((first.Received, second.Received), Is.EqualTo((new[] { sample }, new[] { sample })));
    }

    [Test]
    public void PublishAsync_NoSinks_DoesNothing()
    {
        // Arrange
        var events = new AccountEvents([], new CapturingLogger(), ShortTimeout);

        // Act & Assert
        Assert.That(async () => await events.PublishAsync(SampleEvent()), Throws.Nothing);
    }

    [Test]
    public async Task PublishAsync_SinkThrows_DoesNotAffectTheCallerOrTheOtherSinks()
    {
        // Arrange：一個同步丟、一個非同步丟
        var good = new RecordingAccountEventSink();
        var logger = new CapturingLogger();
        var events = new AccountEvents([new ThrowingSink(synchronous: true), new ThrowingSink(synchronous: false), good], logger, ShortTimeout);

        // Act
        await events.PublishAsync(SampleEvent());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(good.Received, Has.Count.EqualTo(1));
            Assert.That(logger.Warnings, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task PublishAsync_SinkNeverFinishes_ReturnsAfterTheTimeoutAndLogsAWarning()
    {
        // Arrange：接收者不理會取消權杖
        var good = new RecordingAccountEventSink();
        var logger = new CapturingLogger();
        var events = new AccountEvents([new HangingSink(), good], logger, ShortTimeout);
        var clock = Stopwatch.StartNew();

        // Act
        await events.PublishAsync(SampleEvent());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
            Assert.That(good.Received, Has.Count.EqualTo(1));
            Assert.That(logger.Warnings, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task PublishAsync_TheTokenGivenToSinks_IsCancelledAtTheTimeout()
    {
        // Arrange
        var sink = new TokenWatchingSink();
        var events = new AccountEvents([sink], new CapturingLogger(), ShortTimeout);

        // Act
        await events.PublishAsync(SampleEvent());

        // Assert：發送端放手的同時才取消權杖，接收者要一下下才看得到
        Assert.That(async () => await sink.Cancelled.WaitAsync(TimeSpan.FromSeconds(10)), Throws.Nothing);
    }

    [Test]
    public void DefaultTimeout_IsFiveSeconds() =>
        Assert.That(AccountEvents.DefaultTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));

    private sealed class ThrowingSink(bool synchronous) : IAccountEventSink
    {
        public Task PublishAsync(AccountEvent accountEvent, CancellationToken cancellationToken) =>
            synchronous ? throw new InvalidOperationException("接收者壞了") : Task.FromException(new InvalidOperationException("接收者壞了"));
    }

    private sealed class HangingSink : IAccountEventSink
    {
        public Task PublishAsync(AccountEvent accountEvent, CancellationToken cancellationToken) =>
            new TaskCompletionSource().Task;
    }

    private sealed class TokenWatchingSink : IAccountEventSink
    {
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Cancelled => _cancelled.Task;

        public async Task PublishAsync(AccountEvent accountEvent, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cancelled.TrySetResult();
            }
        }
    }

    private sealed class CapturingLogger : ILogger<AccountEvents>
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

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning)
            {
                return;
            }

            lock (_warnings)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }
}
