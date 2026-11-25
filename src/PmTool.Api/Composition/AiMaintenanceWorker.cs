using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Composition;

/// <summary>每小時叫 AI 模組清一次過期的對話與留著的工作內容（<see cref="IAiMaintenance"/>）。無狀態，好幾個容器同時跑也安全。</summary>
public sealed partial class AiMaintenanceWorker(IServiceScopeFactory scopes, ILogger<AiMaintenanceWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>設定名稱。測試用；沒設定就是開著。</summary>
    public const string EnabledSetting = "Ai:Maintenance:Worker:Enabled";

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

    /// <summary>跑一輪。還有沒套用的遷移就跳過；任何例外都記成 Error 後吞掉，不讓背景服務停止。</summary>
    public async Task<AiMaintenanceResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            foreach (var status in scope.ServiceProvider.GetServices<IMigrationStatus>())
            {
                if ((await status.GetPendingMigrationsAsync(cancellationToken)).Count > 0)
                {
                    return new AiMaintenanceResult(0, 0);
                }
            }

            var result = await scope.ServiceProvider.GetRequiredService<IAiMaintenance>().SweepAsync(cancellationToken);
            if (result.Conversations > 0 || result.JobPayloads > 0)
            {
                LogSwept(logger, result.Conversations, result.JobPayloads);
            }

            return result;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogRunFailed(logger, exception);
            return new AiMaintenanceResult(0, 0);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI 的定期清理：刪掉 {Conversations} 則過期的對話、清掉 {Payloads} 件工作的內容。")]
    private static partial void LogSwept(ILogger logger, int conversations, int payloads);

    [LoggerMessage(Level = LogLevel.Error, Message = "AI 的定期清理這一輪失敗，下一輪重來。")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
