using PmTool.Work.Attachments;

namespace PmTool.Work.Tests.Attachments;

/// <summary>下載接成串流：成功要讀到完整內容，失敗要讓讀的人拿到例外，不能看起來像正常結束。</summary>
[TestFixture]
public sealed class PipedDownloadTests
{
    [Test]
    public async Task Start_ADownloadThatSucceeds_IsReadInFull()
    {
        // Arrange
        var bytes = Enumerable.Range(0, 500_000).Select(i => (byte)(i % 249)).ToArray();
        await using var stream = PipedDownload.Start(async (destination, token) => await destination.WriteAsync(bytes, token));

        // Act
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        // Assert
        Assert.That(copy.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public async Task Start_ADownloadThatFailsHalfway_MakesTheReaderThrowInsteadOfEndingNormally()
    {
        // Arrange
        await using var stream = PipedDownload.Start(async (destination, token) =>
        {
            await destination.WriteAsync(new byte[1000], token);
            throw new IOException("測試：下載中斷。");
        });

        // Act / Assert
        Assert.That(async () => await stream.CopyToAsync(Stream.Null), Throws.TypeOf<IOException>().With.Message.Contains("下載中斷"));
    }

    [Test]
    public async Task Start_DisposingTheStreamEarly_CancelsTheDownload()
    {
        // Arrange
        var cancelled = new TaskCompletionSource();
        var stream = PipedDownload.Start(async (destination, token) =>
        {
            await destination.WriteAsync(new byte[10], token);
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        });
        _ = await stream.ReadAsync(new byte[4]);

        // Act
        await stream.DisposeAsync();

        // Assert
        Assert.That(await Task.WhenAny(cancelled.Task, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(cancelled.Task));
    }
}
