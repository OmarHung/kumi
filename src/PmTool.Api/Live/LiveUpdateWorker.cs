using System.Diagnostics;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Live;

/// <summary>
/// 把工作引擎的事件定期變成「哪些項目變了」，推給這個程序裡的連線。和通知不同，它不存讀到哪裡：
/// 從啟動之後（有連線之後）的最新位置開始，記在記憶體，程序重啟就從新的最新位置開始，
/// 沒連上的人重連時會把畫面上的資料全部重抓，補回漏掉的。每個程序各讀各的、各推給自己的連線，
/// 所以開多個容器也不需要共用的訊息匯流排。
/// 沒有任何連線時不讀事件內容（多數時間沒人開著網頁），但每一輪都把位置更新到最新：這樣第一條連線登記之後提交的事件，
/// 一定在位置之後、下一輪讀得到，不會有「連線登記了、位置還沒取」的空窗。
/// </summary>
public sealed partial class LiveUpdateWorker(
    IServiceScopeFactory scopes,
    LiveConnections connections,
    LiveSender sender,
    ILogger<LiveUpdateWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>沒有任何連線時，多久才把位置更新一次。位置只是「連線之後的事件一定在它後面」的下限，舊一點只會多推一次。</summary>
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    /// <summary>設定名稱。測試只想自己決定什麼時候讀事件時關掉；沒設定就是開著。</summary>
    public const string EnabledSetting = "Live:Worker:Enabled";

    private const int BatchSize = 500;

    /// <summary>一輪最多讀幾批：匯入歷史一次會寫幾千筆事件，分幾輪追上，不讓一輪佔住太久。</summary>
    private const int MaxBatchesPerRun = 20;

    private WorkEventPosition? _position;
    private bool _waitingForMigrations;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        long? lastRun = null;
        try
        {
            do
            {
                if (lastRun is null || IsDue(connections.Count > 0, Stopwatch.GetElapsedTime(lastRun.Value)))
                {
                    lastRun = Stopwatch.GetTimestamp();
                    await RunOnceAsync(stoppingToken);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 程序要結束了，不是錯誤。
        }
    }

    /// <summary>有連線就每一輪都跑；沒有連線時每 <see cref="IdleInterval"/> 才跑一次（只更新位置），省下沒人在看時的查詢。</summary>
    public static bool IsDue(bool hasConnections, TimeSpan sinceLastRun) => hasConnections || sinceLastRun >= IdleInterval;

    /// <summary>
    /// 跑一輪：還沒有起點，或沒有任何連線，就只把位置更新到最新（這一輪不讀、不推）；有連線的話讀位置之後新增的事件，
    /// 併成每個群組一則訊息推出去。還有沒套用的遷移就跳過；任何例外都記成 Error 後吞掉（位置不動，下一輪從原來的位置重來），
    /// 不讓背景服務停止。
    /// 回傳這一輪處理的事件數。測試直接呼叫它，不必等計時器。
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();

            // 位置已經有了就不必每輪都問遷移：位置還沒取到（啟動後第一輪、或之前一直失敗）才問。
            if (_position is null && await HasPendingMigrationsAsync(scope.ServiceProvider, cancellationToken))
            {
                return 0;
            }

            var stream = scope.ServiceProvider.GetRequiredService<IWorkEventStream>();
            if (_position is null || connections.Count == 0)
            {
                _position = await stream.GetLatestPositionAsync(cancellationToken);
                return 0;
            }

            return await PushNewEventsAsync(stream, _position, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogRunFailed(logger, exception);
            return 0;
        }
    }

    private async Task<int> PushNewEventsAsync(IWorkEventStream stream, WorkEventPosition from, CancellationToken cancellationToken)
    {
        List<WorkEvent> events = [];
        var position = from;
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            var read = await stream.ReadAfterAsync(position, BatchSize, cancellationToken);
            events.AddRange(read);
            if (read.Count > 0)
            {
                position = read[^1].Position;
            }

            if (read.Count < BatchSize)
            {
                break;
            }
        }

        // 全部的訊息併成一批：同一條連線的依順序送、各條連線同時送，一輪最多等一個逾時，
        // 一條不讀資料的連線被斷開，不會讓其他連線的推送一起等。
        List<LiveDelivery> deliveries = [];
        foreach (var push in LiveEventRouter.Route(events))
        {
            var message = new LiveItemsMessage(push.ProjectKey, push.ItemKeys, push.ActorId);
            deliveries.AddRange(connections.InGroup(push.Group).Select(c => new LiveDelivery(c, LiveMessages.Items, message)));
        }

        await sender.SendAsync(deliveries, cancellationToken);

        // 推完才往前走：推的過程出錯時下一輪整批重來（多推一次只是多重抓一次）。
        _position = position;
        return events.Count;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "還有沒套用的資料表遷移（{Pending}），即時推送先暫停，遷移完會自動恢復。")]
    private static partial void LogWaitingForMigrations(ILogger logger, string pending);

    [LoggerMessage(Level = LogLevel.Error, Message = "即時推送這一輪失敗，下一輪重來。")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
