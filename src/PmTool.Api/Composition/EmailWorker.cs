using PmTool.Abstractions;
using PmTool.Notifications;

namespace PmTool.Api.Composition;

/// <summary>
/// 定期叫通知模組把該寄的 Email 寄出去。無狀態：哪些通知還在等存在資料庫裡，
/// 好幾個容器同時跑時由通知模組的鎖保證同時只有一個在寄（拿不到鎖的那一輪什麼都不做）。
/// </summary>
public sealed partial class EmailWorker(IServiceScopeFactory scopes, ILogger<EmailWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>設定名稱。測試與只想關掉寄信的部署用；沒設定就是開著。</summary>
    public const string EnabledSetting = "Notifications:Email:Worker:Enabled";

    // 資料表還沒遷移的狀況只記一次 Warning，好了之後才重設。
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
    /// 跑一輪。還有沒套用的遷移就跳過這一輪（只記一次 Warning）；任何例外都記成 Error 後吞掉，
    /// 不讓背景服務停止（.NET 預設會讓整個主機跟著結束）。回傳這一輪寄出的信的封數。
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await HasPendingMigrationsAsync(scope.ServiceProvider, cancellationToken))
            {
                return 0;
            }

            return await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogRunFailed(logger, exception);
            return 0;
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "還有沒套用的資料表遷移（{Pending}），寄 Email 先暫停，遷移完會自動恢復。")]
    private static partial void LogWaitingForMigrations(ILogger logger, string pending);

    [LoggerMessage(Level = LogLevel.Error, Message = "寄 Email 的背景處理這一輪失敗，下一輪重來。")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
