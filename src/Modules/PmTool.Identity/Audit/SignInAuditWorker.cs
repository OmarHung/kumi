using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PmTool.Identity.Audit;

/// <summary>
/// 背景把登入的稽核從佇列寫進資料庫。主機關閉時停下來，並把還在佇列裡的盡量寫完（有時間上限）。
/// </summary>
internal sealed class SignInAuditWorker(SignInAuditQueue queue, SignInAuditWriter writer, ILogger<SignInAuditWorker> logger)
    : BackgroundService
{
    /// <summary>關閉時最多花這麼久把剩下的寫完。</summary>
    public static readonly TimeSpan FlushLimit = TimeSpan.FromSeconds(5);

    /// <summary>單筆寫入的上限，資料庫卡住不能讓整個消費者停擺。</summary>
    private static readonly TimeSpan WriteLimit = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (!stoppingToken.IsCancellationRequested && queue.Reader.TryRead(out var attempt))
                {
                    await WriteOneAsync(attempt, WriteLimit);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常的停止：接著把剩下的寫完。
        }

        await FlushAsync();
    }

    private async Task FlushAsync()
    {
        using var limit = new CancellationTokenSource(FlushLimit);
        try
        {
            while (queue.Reader.TryRead(out var attempt))
            {
                await writer.WriteAsync(attempt, limit.Token);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("關閉時登入稽核的佇列沒有寫完，剩下的丟掉。");
        }
    }

    private async Task WriteOneAsync(SignInAttempt attempt, TimeSpan limit)
    {
        using var cts = new CancellationTokenSource(limit);
        await writer.WriteAsync(attempt, cts.Token);
    }
}
