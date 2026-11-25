using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PmTool.Api.RateLimiting;

/// <summary>
/// 登入失敗的節流（放在 API 層：要回 429 與 Retry-After，限流本來也在這裡）。
/// 型別是 public 的，API 的測試專案要換掉它們。
/// </summary>
public sealed record SignInThrottleLimits
{
    /// <summary>測試用：完全不節流。</summary>
    public static SignInThrottleLimits Disabled { get; } = new() { IsEnabled = false };

    public bool IsEnabled { get; init; } = true;

    /// <summary>同一個帳號在這段時間內失敗幾次之後，下一次開始延遲。</summary>
    public int DelayStartsAfterFailures { get; init; } = 4;

    /// <summary>超過門檻之後，每多失敗一次多等的時間。</summary>
    public TimeSpan DelayStep { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>最後一次失敗之後隔這麼久都沒有再失敗，就歸零。</summary>
    public TimeSpan ResetAfter { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>記憶體最多記幾個帳號；滿了先清掉過期的，再擠掉最久沒失敗的。</summary>
    public int MaxTrackedKeys { get; init; } = 50_000;

    /// <summary>同一個帳號同時進行的嘗試（所有來源加起來）最多幾個；只保護記憶體與連線，不是主要的防線。</summary>
    public int MaxPendingPerAccount { get; init; } = 20;

    /// <summary>全部帳號同時進行的嘗試最多幾個。</summary>
    public int MaxPendingTotal { get; init; } = 50_000;
}

/// <summary>等一段時間。抽成介面，測試才不必真的等。</summary>
public interface ISignInDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>正式環境：依 <see cref="TimeProvider"/> 等。</summary>
public sealed class TimeProviderSignInDelay(TimeProvider time) : ISignInDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, time, cancellationToken);
}

/// <summary>
/// 一次登入嘗試進來時的結果：可以進行（先等 <see cref="Delay"/> 再驗），或被拒絕並告訴用戶端 <see cref="RetryAfter"/> 之後再試。
/// 放行的結果帶一個只有節流認得的位子，結束時要把它交回 <see cref="SignInThrottle"/>；別人的位子交不了。
/// </summary>
public readonly record struct SignInAdmission(bool IsAllowed, TimeSpan Delay, TimeSpan RetryAfter)
{
    internal object? Slot { get; init; }
}

/// <summary>
/// 依帳號（Email 正規化後）記失敗次數：失敗加上正在進行的嘗試到一定數量之後，每次先等一會兒再驗。
/// 「進行中只能有一個」依（帳號、來源）算，所以別人只知道某個 Email 也佔不住本人的位子；
/// 同一個帳號同時進行的嘗試另有上限，只保護記憶體與連線。
/// 帳號存不存在 API 不知道，所以兩種情況完全一樣。
/// 只放在記憶體、各程序各算各的；時間用 <see cref="TimeProvider"/> 比對，沒有計時器要在關閉時停掉。
/// </summary>
public sealed class SignInThrottle(SignInThrottleLimits limits, TimeProvider time)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _byKey = new(StringComparer.Ordinal);

    // 依最後一次失敗的時間由舊到新排，過期與擠掉都從最前面處理，不必掃整張表。
    private readonly LinkedList<Entry> _byAge = new();

    // 進行中的嘗試：每個（帳號、來源）一個；每個帳號的數量另外記，用來算延遲與上限。嘗試結束時一定移除。
    private readonly Dictionary<(string Account, string Source), Slot> _pending = [];
    private readonly Dictionary<string, int> _pendingPerAccount = new(StringComparer.Ordinal);

    /// <param name="source">來源的鍵（<see cref="ApiRateLimiting.AddressKeyOf"/>）。</param>
    public SignInAdmission Begin(string email, string source)
    {
        if (!limits.IsEnabled)
        {
            return new SignInAdmission(true, TimeSpan.Zero, TimeSpan.Zero);
        }

        var key = KeyOf(email);
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_pending.TryGetValue((key, source), out var holder))
            {
                return Rejected(holder.BusyUntil - now);
            }

            var pendingForAccount = _pendingPerAccount.GetValueOrDefault(key);
            if (pendingForAccount >= limits.MaxPendingPerAccount || _pending.Count >= limits.MaxPendingTotal)
            {
                return Rejected(TimeSpan.Zero);
            }

            var failures = TryGetLive(key, now, out var entry) ? entry.Failures : 0;
            var delay = DelayFor(failures + pendingForAccount);
            var slot = new Slot(key, source, now + delay);
            _pending[(key, source)] = slot;
            _pendingPerAccount[key] = pendingForAccount + 1;
            return new SignInAdmission(true, delay, TimeSpan.Zero) { Slot = slot };
        }
    }

    public void Succeeded(SignInAdmission admission)
    {
        lock (_gate)
        {
            if (!Release(admission))
            {
                return;
            }

            if (_byKey.Remove(((Slot)admission.Slot!).Account, out var node))
            {
                _byAge.Remove(node);
            }
        }
    }

    public void Failed(SignInAdmission admission)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (admission.Slot is not Slot slot || !Release(admission))
            {
                return;
            }

            var key = slot.Account;
            if (TryGetLive(key, now, out var entry))
            {
                _byAge.Remove(_byKey[key]);
                _byKey[key] = _byAge.AddLast(entry);
                entry.Failures++;
                entry.LastFailureAt = now;
                return;
            }

            MakeRoom(now);
            _byKey[key] = _byAge.AddLast(new Entry(key) { Failures = 1, LastFailureAt = now });
        }
    }

    /// <summary>嘗試沒有驗完（用戶端斷線、程式出錯）：只放掉自己佔的位子，不算一次失敗。</summary>
    public void Abandoned(SignInAdmission admission)
    {
        lock (_gate)
        {
            Release(admission);
        }
    }

    private static SignInAdmission Rejected(TimeSpan remaining) =>
        new(false, TimeSpan.Zero, remaining < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining);

    private TimeSpan DelayFor(int attemptsSoFar)
    {
        if (attemptsSoFar < limits.DelayStartsAfterFailures)
        {
            return TimeSpan.Zero;
        }

        var steps = attemptsSoFar - limits.DelayStartsAfterFailures + 1;
        return TimeSpan.FromTicks(Math.Min(limits.DelayStep.Ticks * steps, limits.MaxDelay.Ticks));
    }

    /// <summary>只放掉交進來的那一個位子；不是這個節流發的、或已經放掉的，什麼都不動。呼叫端要先拿著鎖。</summary>
    private bool Release(SignInAdmission admission)
    {
        if (admission.Slot is not Slot slot
            || !_pending.TryGetValue((slot.Account, slot.Source), out var current)
            || !ReferenceEquals(current, slot))
        {
            return false;
        }

        _pending.Remove((slot.Account, slot.Source));
        if (_pendingPerAccount[slot.Account] <= 1)
        {
            _pendingPerAccount.Remove(slot.Account);
        }
        else
        {
            _pendingPerAccount[slot.Account]--;
        }

        return true;
    }

    private bool TryGetLive(string key, DateTimeOffset now, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Entry? entry)
    {
        if (_byKey.TryGetValue(key, out var node))
        {
            if (now - node.Value.LastFailureAt < limits.ResetAfter)
            {
                entry = node.Value;
                return true;
            }

            _byKey.Remove(key);
            _byAge.Remove(node);
        }

        entry = null;
        return false;
    }

    private void MakeRoom(DateTimeOffset now)
    {
        while (_byAge.First is { } oldest && now - oldest.Value.LastFailureAt >= limits.ResetAfter)
        {
            _byKey.Remove(oldest.Value.Key);
            _byAge.RemoveFirst();
        }

        while (_byKey.Count >= limits.MaxTrackedKeys && _byAge.First is { } stale)
        {
            _byKey.Remove(stale.Value.Key);
            _byAge.RemoveFirst();
        }
    }

    /// <summary>
    /// 和模組的正規化（組合字、大小寫、頭尾空白）一致：模組那邊只差在寫法的 Email 登不進去，分到別的鍵也猜不到真的帳號。
    /// 落單的代理字元會讓正規化丟例外，改用原字串。
    /// </summary>
    private static string KeyOf(string email)
    {
        var trimmed = (email ?? string.Empty).Trim();
        try
        {
            return trimmed.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
    }

    private sealed class Entry(string key)
    {
        public string Key { get; } = key;

        public int Failures { get; set; }

        public DateTimeOffset LastFailureAt { get; set; }
    }

    private sealed record Slot(string Account, string Source, DateTimeOffset BusyUntil);
}

internal static class SignInThrottleRegistration
{
    public static IServiceCollection AddSignInThrottle(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new SignInThrottleLimits());
        services.TryAddSingleton<ISignInDelay, TimeProviderSignInDelay>();
        services.TryAddSingleton<SignInThrottle>();
        return services;
    }
}
