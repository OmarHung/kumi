using System.Collections.Concurrent;

namespace PmTool.Identity.Sessions;

/// <summary>查一個權杖的結果。</summary>
/// <param name="AccountId">工作階段屬於哪個帳號；查無此權杖時是 null。作廢一個帳號時靠它找出要清掉的項目。</param>
/// <param name="Session">還有效時是工作階段；作廢、過期、帳號停用、查無此權杖都是 null。</param>
internal sealed record SessionLookup(Guid? AccountId, ActiveSession? Session)
{
    /// <summary>存取權杖的查詢結果（和工作階段共用同一個快取，帳號作廢時一起清掉）；有效時才有值。</summary>
    public ActiveAccessToken? Token { get; init; }

    /// <summary>權杖的期限，快取期間跨過它就不能再用；不過期是 null。</summary>
    public DateTimeOffset? TokenExpiresAt { get; init; }

    public static readonly SessionLookup Unknown = new(null, null);
}

/// <summary>快取的兩個數字。獨立成可以替換的物件，測試才不必真的建上萬個工作階段才碰得到上限。</summary>
internal sealed record SessionCacheLimits
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(IdentityRules.SessionCacheSeconds);

    /// <summary>一筆只有一個工作階段的資料，一萬筆也只有幾 MiB。</summary>
    public int MaxEntries { get; init; } = 10_000;
}

/// <summary>
/// 依權杖的雜湊保留查詢結果最多 <see cref="SessionCacheLimits.Duration"/>，每個請求才不必都查資料庫。
/// 無效的結果也保留：拿著作廢或過期的舊 Cookie 一直重送，一樣每段時間只查一次。
/// 同一個權杖同時來的請求共用同一次查詢，頁面一次發好幾個請求時不會變成好幾次查詢。
/// <para>
/// 作廢時（<see cref="Forget"/>）要讓這個帳號在作廢之前開始的查詢全部不算數，包括還在查的：
/// 它可能在作廢寫進資料庫之前就讀到「還有效」。所以每次查詢記下開始時的序號，作廢時把序號往前推並記在帳號上，
/// 序號比帳號的作廢序號舊的結果一律不用；還在查的只有序號是最新的才能共用，因為還不知道它是哪個帳號的。
/// </para>
/// </summary>
internal sealed class SessionCache(TimeProvider time, SessionCacheLimits limits)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Revocation> _revocations = new();
    private long _epoch;

    /// <summary>
    /// 有可用的結果就直接回，沒有就查（或加入正在進行的那一次）。查詢本身不綁在任何一個請求上：
    /// 觸發它的請求斷線了，其他在等的請求仍然拿得到結果；每個請求只等自己的期限。
    /// <paramref name="keepUnknown"/> 為 false 時，「查無此權杖」的結果查完就不保留（在等的請求照樣共用）：
    /// 隨便編一個格式正確的字串就能造出一筆，保留的話靠大量亂數就能把真人的工作階段擠出有上限的快取。
    /// </summary>
    public Task<SessionLookup> GetAsync(
        string key, Func<Task<SessionLookup>> lookup, CancellationToken cancellationToken, bool keepUnknown = true)
    {
        var now = time.GetUtcNow();
        if (!_entries.TryGetValue(key, out var entry) || !IsUsable(entry, now))
        {
            entry = StartOrJoin(key, lookup, now, keepUnknown);
        }

        return entry.Result.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 只看已經有的結果，不查：有可用的（已完成、沒過期、沒被作廢）才回，其他一律 null。
    /// 沒有東西會因為它而被查或被存，所以任意編的權杖也不會佔掉快取。
    /// </summary>
    public SessionLookup? TryPeek(string key)
    {
        if (!_entries.TryGetValue(key, out var entry) || !IsUsable(entry, time.GetUtcNow()) || !entry.Result.Task.IsCompletedSuccessfully)
        {
            return null;
        }

        return entry.Result.Task.Result;
    }

    /// <summary>
    /// 這個帳號的工作階段剛在資料庫裡作廢了：之前的查詢結果都不能再用。要在寫進資料庫（交易提交）之後才呼叫，
    /// 否則在這之後才開始的查詢可能讀到還沒提交的舊狀態，又把「還有效」存回來。
    /// </summary>
    public void Forget(Guid accountId)
    {
        var epoch = Interlocked.Increment(ref _epoch);

        // 時刻在推進序號之後才讀：被這次作廢擋下的查詢，開始的時刻一定不晚於它，下面清掉舊紀錄時才不會早一步清掉。
        var revocation = new Revocation(epoch, time.GetUtcNow());
        _revocations.AddOrUpdate(accountId, revocation, (_, existing) => existing.Epoch > epoch ? existing : revocation);

        foreach (var pair in _entries)
        {
            if (pair.Value.Result.Task.IsCompletedSuccessfully && pair.Value.Result.Task.Result.AccountId == accountId)
            {
                _entries.TryRemove(pair);
            }
        }

        DropOldRevocations(revocation.At);
    }

    private bool IsUsable(Entry entry, DateTimeOffset now)
    {
        var task = entry.Result.Task;
        if (!task.IsCompleted)
        {
            return entry.Epoch == Interlocked.Read(ref _epoch);
        }

        // 時刻比現在還晚表示時鐘被往回調過，不信任它。
        if (!task.IsCompletedSuccessfully || entry.StartedAt > now || now - entry.StartedAt >= limits.Duration)
        {
            return false;
        }

        var result = task.Result;
        var revoked = result.AccountId is { } accountId
            && _revocations.TryGetValue(accountId, out var revocation)
            && revocation.Epoch > entry.Epoch;

        // 絕對期限可能落在快取的期間裡。
        return !revoked
            && (result.Session is null || now < result.Session.ExpiresAt)
            && (result.TokenExpiresAt is null || now < result.TokenExpiresAt);
    }

    private Entry StartOrJoin(string key, Func<Task<SessionLookup>> lookup, DateTimeOffset now, bool keepUnknown)
    {
        MakeRoom(now);
        var candidate = new Entry(now, Interlocked.Read(ref _epoch), new(TaskCreationOptions.RunContinuationsAsynchronously));

        // 同時有好幾個請求走到這裡時只會有一個放得進去，其他的拿到它放的那一份，所以只有放進去的那一個去查。
        var stored = _entries.AddOrUpdate(key, candidate, (_, existing) => IsUsable(existing, now) ? existing : candidate);
        if (ReferenceEquals(stored, candidate))
        {
            _ = RunAsync(key, candidate, lookup, keepUnknown);
        }

        return stored;
    }

    private async Task RunAsync(string key, Entry entry, Func<Task<SessionLookup>> lookup, bool keepUnknown)
    {
        try
        {
            var result = await lookup();
            if (!keepUnknown && result.AccountId is null)
            {
                // 先拿掉再回報結果，在等的請求拿到結果時快取裡已經沒有這一筆。
                _entries.TryRemove(KeyValuePair.Create(key, entry));
            }

            entry.Result.SetResult(result);
        }
        catch (Exception exception)
        {
            // 查不到結果（例如資料庫連不上）不保留，下一個請求重新查；在等的請求照樣看到錯誤。
            _entries.TryRemove(KeyValuePair.Create(key, entry));
            entry.Result.SetException(exception);
        }
    }

    /// <summary>
    /// 滿了先清掉不能用的；全部都還能用就拿掉最舊的一筆。寧可多查幾次，也不讓記憶體跟著工作階段數長。
    /// 同時新增的請求可能讓數量暫時超過上限幾筆，不影響「有上限」這件事。
    /// </summary>
    private void MakeRoom(DateTimeOffset now)
    {
        if (_entries.Count < limits.MaxEntries)
        {
            return;
        }

        foreach (var pair in _entries)
        {
            if (!IsUsable(pair.Value, now))
            {
                _entries.TryRemove(pair);
            }
        }

        if (_entries.Count >= limits.MaxEntries && _entries.MinBy(pair => pair.Value.StartedAt) is { Value: not null } oldest)
        {
            _entries.TryRemove(oldest);
        }
    }

    /// <summary>
    /// 作廢的紀錄只要留到被它擋下的結果都過期為止：那些結果開始的時刻都不晚於作廢的時刻，
    /// 所以作廢超過一個快取期間，它們自己就因為過期而不能用了。
    /// </summary>
    private void DropOldRevocations(DateTimeOffset now)
    {
        foreach (var pair in _revocations)
        {
            if (now - pair.Value.At >= limits.Duration)
            {
                _revocations.TryRemove(pair);
            }
        }
    }

    private sealed record Entry(DateTimeOffset StartedAt, long Epoch, TaskCompletionSource<SessionLookup> Result);

    private sealed record Revocation(long Epoch, DateTimeOffset At);
}
