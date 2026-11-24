using PmTool.Abstractions;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 就緒檢查的兩個數字。預設值是 <see cref="ApiLimits"/> 的常數；獨立成可以替換的物件，測試才不必真的等。
/// </summary>
public sealed record ReadinessLimits
{
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromSeconds(ApiLimits.ReadinessCacheSeconds);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(ApiLimits.ReadinessTimeoutSeconds);
}

/// <summary>
/// 問所有模組準備好了沒有。結果保留 <see cref="ReadinessLimits.CacheDuration"/>，同時間只有一個請求真的去問：
/// 這個端點不限流也不用登入，每個請求都去問資料庫的話，任何人都能替資料庫加壓。
/// 檢查自己開 scope、用自己的期限，不綁在某一個請求上：那個請求斷線了，其他在等的請求仍然拿得到結果。
/// </summary>
internal sealed partial class ReadinessCheck(
    IServiceScopeFactory scopes, TimeProvider time, ReadinessLimits limits, ILogger<ReadinessCheck> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CheckedReadiness? _last;

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        if (Fresh() is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh() is { } checkedMeanwhile)
            {
                return checkedMeanwhile;
            }

            var ready = await ProbeAllAsync();
            Volatile.Write(ref _last, new CheckedReadiness(ready, time.GetUtcNow()));
            return ready;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private bool? Fresh() =>
        Volatile.Read(ref _last) is { } last && time.GetUtcNow() - last.At < limits.CacheDuration ? last.Ready : null;

    private async Task<bool> ProbeAllAsync()
    {
        using var deadline = new CancellationTokenSource(limits.Timeout);
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            var probes = scope.ServiceProvider.GetServices<IReadinessProbe>();
            var results = await Task.WhenAll(probes.Select(probe => probe.IsReadyAsync(deadline.Token)))
                .WaitAsync(deadline.Token);
            return results.All(ready => ready);
        }
        catch (Exception exception)
        {
            // 連不上資料庫時模組的檢查可能直接丟例外，或一直等不到回應；兩種都是「還沒準備好」，不是伺服器壞了。
            LogProbeFailed(logger, exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "就緒檢查失敗，回報還沒準備好。")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception);

    private sealed record CheckedReadiness(bool Ready, DateTimeOffset At);
}
