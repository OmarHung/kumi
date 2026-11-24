using System.Collections.Concurrent;

namespace PmTool.Api.RateLimiting;

/// <summary>
/// 依來源記下 Bearer 權杖驗證失敗的次數。驗證排在限流之前（要先知道是誰才能依人限流），
/// 沒有這一道的話，任何人每個請求送一個新的假權杖，就能讓每個請求都查一次資料庫。
/// 同一個來源一個視窗內失敗到上限之後，後續的 Bearer 請求在查資料庫之前就擋下；有效的權杖不計次。
/// 來源的算法和 <see cref="ApiRateLimiting"/> 一致，所以 IPv6 以 /64 計。
/// </summary>
internal sealed class BearerFailureLimiter(TimeProvider time)
{
    /// <summary>記憶體的上限：來源（尤其是 IPv6 的 /64）是攻擊者可以任意換的。滿了之後新的來源共用同一份額度。</summary>
    private const int MaxTrackedSources = 10_000;

    private const string OverflowKey = "overflow";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(ApiLimits.RateLimitWindowSeconds);

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    /// <summary>這個來源是不是已經用完額度；是的話 <paramref name="retryAfter"/> 是視窗還剩多久。</summary>
    public bool IsBlocked(string source, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_counters.TryGetValue(KeyOf(source), out var counter))
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
        MakeRoom(now);
        _counters.GetOrAdd(KeyOf(source), _ => new Counter(now)).Add(now);
    }

    private string KeyOf(string source) =>
        _counters.ContainsKey(source) || _counters.Count < MaxTrackedSources ? source : OverflowKey;

    private void MakeRoom(DateTimeOffset now)
    {
        if (_counters.Count < MaxTrackedSources)
        {
            return;
        }

        foreach (var pair in _counters)
        {
            if (IsOver(pair.Value.Read().StartedAt, now))
            {
                _counters.TryRemove(pair);
            }
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
