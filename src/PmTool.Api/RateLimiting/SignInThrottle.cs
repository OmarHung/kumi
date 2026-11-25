using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PmTool.Api.RateLimiting;

/// <summary>
/// 登入失敗的節流（放在 API 層：要回 429 與 Retry-After，限流本來也在這裡）。
/// 這個檔案目前只有合約與註冊；節流的邏輯之後填進來。型別是 public 的，API 的測試專案要換掉它們。
/// </summary>
public sealed record SignInThrottleLimits
{
    /// <summary>測試用：完全不節流。</summary>
    public static SignInThrottleLimits Disabled { get; } = new() { IsEnabled = false };

    public bool IsEnabled { get; init; } = true;
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

internal static class SignInThrottleRegistration
{
    public static IServiceCollection AddSignInThrottle(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new SignInThrottleLimits());
        services.TryAddSingleton<ISignInDelay, TimeProviderSignInDelay>();
        return services;
    }
}
