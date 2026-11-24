using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Composition;

/// <summary>
/// 把工作引擎的事件定期交給每一個讀者（通知，之後的 webhook、自動化也是）。讀者各自記住讀到哪裡，
/// 所以這裡是無狀態的：一個讀者出錯只影響它自己，下一輪從它記的位置重來；程序重啟、好幾個容器同時跑都不會漏、不會重複
/// （讀者在自己的交易裡處理一批並記下位置，同時只有一個程序做）。
/// </summary>
public sealed partial class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    public const int BatchSize = 100;

    /// <summary>一輪最多處理幾批：落後很多時分幾輪追上，不讓一個讀者佔住程序太久。</summary>
    public const int MaxBatchesPerRun = 20;

    /// <summary>設定名稱。測試與只想關掉背景處理的部署用；沒設定就是開著。</summary>
    public const string EnabledSetting = "Notifications:Worker:Enabled";

    // 資料表還沒遷移的狀況只記一次 Warning，好了之後才重設：每 2 秒記一筆會把記錄洗掉。
    private bool _waitingForMigrations;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 程序要結束了，不是錯誤。
        }
    }

    /// <summary>
    /// 跑一輪：對每個讀者，讀位置、讀事件、交給它處理，直到沒有新的事件（或達到一輪的上限）。
    /// 還有沒套用的遷移（部署成 <c>Database:MigrateOnStartup=false</c>、遷移還沒跑）就跳過這一輪，只記一次 Warning。
    /// 任何讀者的例外都記成 Error 後吞掉，不讓背景服務停止（.NET 預設會讓整個主機跟著結束），也不擋後面的讀者。
    /// 回傳這一輪交出去的事件數。測試直接呼叫它，不必等計時器。
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var handed = 0;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await HasPendingMigrationsAsync(scope.ServiceProvider, cancellationToken))
            {
                return 0;
            }

            var stream = scope.ServiceProvider.GetRequiredService<IWorkEventStream>();
            foreach (var consumer in scope.ServiceProvider.GetServices<IWorkEventConsumer>())
            {
                handed += await DrainAsync(stream, consumer, cancellationToken);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogRunFailed(logger, exception);
        }

        return handed;
    }

    private async Task<bool> HasPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        List<string> pending = [];
        foreach (var status in services.GetServices<IMigrationStatus>())
        {
            pending.AddRange(await status.GetPendingMigrationsAsync(cancellationToken));
        }

        if (pending.Count == 0)
        {
            _waitingForMigrations = false;
            return false;
        }

        if (!_waitingForMigrations)
        {
            _waitingForMigrations = true;
            LogWaitingForMigrations(logger, string.Join("、", pending));
        }

        return true;
    }

    private async Task<int> DrainAsync(IWorkEventStream stream, IWorkEventConsumer consumer, CancellationToken cancellationToken)
    {
        var handed = 0;
        try
        {
            for (var batch = 0; batch < MaxBatchesPerRun; batch++)
            {
                // 每一批都重讀位置：別的程序剛處理過的事件，這裡就不再拿來處理。
                var position = await consumer.GetPositionAsync(cancellationToken);
                var events = await stream.ReadAfterAsync(position, BatchSize, cancellationToken);
                if (events.Count == 0)
                {
                    break;
                }

                await consumer.HandleAsync(events, cancellationToken);
                handed += events.Count;
                if (events.Count < BatchSize)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogConsumerFailed(logger, consumer.Name, exception);
        }

        return handed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "還有沒套用的資料表遷移（{Pending}），事件的背景處理先暫停，遷移完會自動恢復。")]
    private static partial void LogWaitingForMigrations(ILogger logger, string pending);

    [LoggerMessage(Level = LogLevel.Error, Message = "事件的讀者「{Consumer}」處理失敗，下一輪從它記的位置重來。")]
    private static partial void LogConsumerFailed(ILogger logger, string consumer, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "事件的背景處理這一輪失敗，下一輪重來。")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
