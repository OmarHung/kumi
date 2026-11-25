using System.Collections.Concurrent;

namespace PmTool.Api.RateLimiting;

/// <summary>
/// 依來源記下 Bearer 權杖驗證失敗的次數。驗證排在限流之前（要先知道是誰才能依人限流），
/// 沒有這一道的話，任何人每個請求送一個新的假權杖，就能讓每個請求都查一次資料庫。
/// 同一個來源一個視窗內失敗到上限之後，後續的 Bearer 請求在查資料庫之前就擋下（快取裡有效的權杖照樣放行）；有效的權杖不計次。
/// 來源的算法和 <see cref="ApiRateLimiting"/> 一致，所以 IPv6 以 /64 計。
/// </summary>
public sealed class BearerFailureLimiter(TimeProvider time)
{
    // 記憶體的上限（ApiLimits.BearerFailureMaxTrackedSources）：來源（尤其是 IPv6 的 /64）是用戶端可以任意換的。
    // 滿了之後沒記過的來源不記也不擋：共用一份額度的話，用滿它就能讓所有新來源一起被擋。

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(ApiLimits.RateLimitWindowSeconds);

    // 滿了之後清理最多每秒做一次：每個新來源都掃整張表的話，失敗灌進來就是在放大成本。
    private static readonly TimeSpan MinCleanupInterval = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly Lock _cleanupGate = new();
    private DateTimeOffset? _lastCleanupAt;
    private int _cleanupScanCount;

    /// <summary>到目前為止掃過整張表的次數（測試用，確認滿了之後不會每個新來源都掃）。</summary>
    public int CleanupScanCount => Volatile.Read(ref _cleanupScanCount);

    /// <summary>這個來源是不是已經用完額度；是的話 <paramref name="retryAfter"/> 是視窗還剩多久。</summary>
    public bool IsBlocked(string source, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_counters.TryGetValue(source, out var counter))
        {
            return false;
        }

        var (count, startedAt) = counter.Read();
        var now = time.GetUtcNow();
        if (count < ApiLimits.BearerFailuresPerWindow || IsOver(startedAt, now))
        {
            return false;
        }

        retryAfter = startedAt + Window - now;
        return true;
    }

    public void RecordFailure(string source)
    {
        var now = time.GetUtcNow();
        if (!_counters.TryGetValue(source, out var counter))
        {
            MakeRoom(now);
            if (_counters.Count >= ApiLimits.BearerFailureMaxTrackedSources)
            {
                return;
            }

            counter = _counters.GetOrAdd(source, _ => new Counter(now));
        }

        counter.Add(now);
    }

    private void MakeRoom(DateTimeOffset now)
    {
        if (_counters.Count < ApiLimits.BearerFailureMaxTrackedSources || !TryStartCleanup(now))
        {
            return;
        }

        Interlocked.Increment(ref _cleanupScanCount);
        foreach (var pair in _counters)
        {
            if (IsOver(pair.Value.Read().StartedAt, now))
            {
                _counters.TryRemove(pair);
            }
        }
    }

    // 時鐘被往回調過時也放行，才不會一直不清。
    private bool TryStartCleanup(DateTimeOffset now)
    {
        lock (_cleanupGate)
        {
            if (_lastCleanupAt is { } last && now >= last && now - last < MinCleanupInterval)
            {
                return false;
            }

            _lastCleanupAt = now;
            return true;
        }
    }

    // 時刻比現在還晚表示時鐘被往回調過，這一個視窗重新算。
    private static bool IsOver(DateTimeOffset startedAt, DateTimeOffset now) => now - startedAt >= Window || startedAt > now;

    private sealed class Counter(DateTimeOffset startedAt)
    {
        private readonly object _gate = new();
        private DateTimeOffset _startedAt = startedAt;
        private int _count;

        public (int Count, DateTimeOffset StartedAt) Read()
        {
            lock (_gate)
            {
                return (_count, _startedAt);
            }
        }

        public void Add(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (IsOver(_startedAt, now))
                {
                    _startedAt = now;
                    _count = 0;
                }

                _count++;
            }
        }
    }
}
