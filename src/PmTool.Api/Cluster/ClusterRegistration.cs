using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Abstractions;
using PmTool.Api.Composition;

namespace PmTool.Api.Cluster;

/// <summary>跨程序訊號的設定。<paramref name="Enabled"/> 是「要不要經過資料庫送給別的程序」；關掉時只有這個程序裡的接收端收得到。</summary>
public sealed record ClusterSignalOptions(bool Enabled);

/// <summary>
/// 跨程序訊號的接線。現在只有本機分送：<see cref="IClusterSignals.Publish"/> 立刻交給這個程序裡的每個接收端。
/// 經過資料庫送給其他程序的部分接在這裡之後，不需要改發送的人與接收端。
/// </summary>
public static class ClusterRegistration
{
    /// <summary>設定名稱。沒設定就是開著；單一程序的部署與測試可以關掉。</summary>
    public const string EnabledSetting = "Cluster:Signals:Enabled";

    public static IServiceCollection AddClusterSignals(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enabled = SettingValues.ReadFlag(configuration, EnabledSetting, defaultValue: true);
        services.TryAddSingleton(new ClusterSignalOptions(enabled));

        // 身分模組先放了什麼都不做的版本，這裡換成會分送的。
        services.Replace(ServiceDescriptor.Singleton<IClusterSignals, LocalClusterSignals>());
        return services;
    }
}

/// <summary>
/// 本機分送：訊號發出時，同步叫起這個程序裡所有的接收端，不等它們做完，接收端的例外只記下來。
/// 沒有背景工作、計時器或連線，主機關閉時沒有東西要停。
/// </summary>
internal sealed partial class LocalClusterSignals(
    IEnumerable<IClusterSignalHandler> handlers, ILogger<LocalClusterSignals> logger) : IClusterSignals
{
    private readonly Guid _origin = Guid.NewGuid();

    public void Publish(ClusterSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var stamped = signal.Origin == Guid.Empty ? signal with { Origin = _origin } : signal;
        foreach (var handler in handlers)
        {
            Deliver(handler, stamped);
        }
    }

    private void Deliver(IClusterSignalHandler handler, ClusterSignal signal)
    {
        try
        {
            var running = handler.HandleAsync(signal, CancellationToken.None);
            if (!running.IsCompleted)
            {
                _ = running.ContinueWith(
                    task => LogHandlerFailed(task.Exception!.GetBaseException(), handler.GetType().Name),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else if (running.IsFaulted)
            {
                LogHandlerFailed(running.Exception!.GetBaseException(), handler.GetType().Name);
            }
        }
        catch (Exception exception)
        {
            LogHandlerFailed(exception, handler.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "跨程序訊號的接收端 {Handler} 處理失敗。")]
    private partial void LogHandlerFailed(Exception exception, string handler);
}
