using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PmTool.Identity.Audit;

/// <summary>
/// 登入稽核的佇列：請求只把結果放進來就回去，不碰資料庫；背景的消費者再慢慢寫。
/// 有上限，滿了丟掉最新的、每分鐘最多記一筆 Warning，不讓寫入的速度反過來拖慢登入或撐大記憶體。
/// </summary>
internal sealed class SignInAuditQueue(TimeProvider time, ILogger<SignInAuditQueue> logger)
{
    public const int Capacity = 2_000;

    private static readonly long WarnIntervalTicks = TimeSpan.FromMinutes(1).Ticks;

    private readonly Channel<SignInAttempt> _channel = Channel.CreateBounded<SignInAttempt>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

    private readonly Lock _warnGate = new();
    private long _lastWarnTicks = long.MinValue;
    private int _droppedSinceWarn;

    public ChannelReader<SignInAttempt> Reader => _channel.Reader;

    /// <summary>放得進去回 true；滿了丟掉這一筆。不碰資料庫、不丟例外。</summary>
    public bool TryEnqueue(SignInAttempt attempt)
    {
        if (_channel.Writer.TryWrite(attempt))
        {
            return true;
        }

        WarnDropped();
        return false;
    }

    private void WarnDropped()
    {
        int dropped;
        lock (_warnGate)
        {
            _droppedSinceWarn++;
            var now = time.GetUtcNow().UtcTicks;
            if (_lastWarnTicks != long.MinValue && now - _lastWarnTicks < WarnIntervalTicks && now >= _lastWarnTicks)
            {
                return;
            }

            _lastWarnTicks = now;
            dropped = _droppedSinceWarn;
            _droppedSinceWarn = 0;
        }

        logger.LogWarning("登入稽核的佇列滿了（上限 {Capacity}），丟掉了 {Dropped} 筆。", Capacity, dropped);
    }
}
