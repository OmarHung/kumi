using Microsoft.Extensions.Logging;
using PmTool.Api.Live;

namespace PmTool.Api.Tests.Live;

/// <summary>每一次推送都有自己的期限：一條不讀資料的連線不能把後面的推送都拖住。</summary>
[TestFixture]
public sealed class LiveSenderTests
{
    private static readonly LiveOptions Short = new() { SendTimeout = TimeSpan.FromMilliseconds(150) };

    private static (LiveSender Sender, CapturedLogProvider Logs) Create()
    {
        var logs = new CapturedLogProvider();
        return (new LiveSender(Short, LoggerFactory.Create(b => b.AddProvider(logs)).CreateLogger<LiveSender>()), logs);
    }

    [Test]
    public async Task SendAsync_ASendThatNeverFinishes_GivesUpAfterTheTimeoutAndLogsAWarning()
    {
        // Arrange：連取消都不理會的送出。
        var (sender, logs) = Create();
        var never = new TaskCompletionSource();

        // Act
        var sending = sender.SendAsync("慢的", _ => never.Task, CancellationToken.None);
        var finished = await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(5))) == sending;

        // Assert
        Assert.That(finished, Is.True);
        Assert.That(logs.Entries.Select(e => e.Level), Does.Contain(LogLevel.Warning));
    }

    [Test]
    public async Task SendAsync_AfterOneTimesOut_TheNextSendStillGoesOut()
    {
        // Arrange
        var (sender, _) = Create();
        var delivered = false;

        // Act
        await sender.SendAsync("卡住的", _ => new TaskCompletionSource().Task, CancellationToken.None);
        await sender.SendAsync("正常的", _ =>
        {
            delivered = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert
        Assert.That(delivered, Is.True);
    }

    [Test]
    public async Task SendAsync_ASendThatThrows_IsLoggedAndSwallowed()
    {
        // Arrange
        var (sender, logs) = Create();

        // Act
        await sender.SendAsync("壞的", _ => throw new InvalidOperationException("連線已關閉"), CancellationToken.None);

        // Assert
        Assert.That(logs.Entries.Select(e => e.Level), Does.Contain(LogLevel.Warning));
    }

    [Test]
    public void SendAsync_TheCallerCancels_PropagatesInsteadOfCountingAsATimeout()
    {
        // Arrange
        var (sender, _) = Create();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        // Act & Assert
        Assert.CatchAsync<OperationCanceledException>(() => sender.SendAsync("關機", _ => new TaskCompletionSource().Task, cancel.Token));
    }
}
