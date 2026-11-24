using System.Globalization;
using System.Threading.RateLimiting;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 網頁回報錯誤的全站上限。預設值是 <see cref="ApiLimits"/> 的常數；
/// 獨立成可以替換的物件，測試才不必登入幾十個人才碰得到上限。
/// </summary>
public sealed record ClientErrorLimits
{
    public int PermitsPerWindow { get; init; } = ApiLimits.ClientErrorGlobalPermitsPerWindow;

    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(ApiLimits.RateLimitWindowSeconds);
}

/// <summary>
/// 全站共用的一份額度。限流的政策一次只能套一個分區（每人一份），全站的上限沒辦法和它串在同一個政策裡，
/// 所以端點在通過每人的限制之後，另外來這裡拿一次。
/// </summary>
internal sealed class ClientErrorBudget(ClientErrorLimits limits) : IDisposable
{
    // 拿不到額度時回報多久之後再試；固定視窗的限流器會給剩下的時間，萬一沒給就請用戶端一秒後再試。
    private const string DefaultRetryAfterSeconds = "1";

    private readonly FixedWindowRateLimiter _limiter = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = limits.PermitsPerWindow,
        Window = limits.Window,
        QueueLimit = 0,
        AutoReplenishment = true,
    });

    /// <summary>拿一次額度。拿不到時回傳 false，並給出 Retry-After 的秒數。</summary>
    public bool TryTake(out string retryAfterSeconds)
    {
        using var lease = _limiter.AttemptAcquire();
        retryAfterSeconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture)
            : DefaultRetryAfterSeconds;
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
