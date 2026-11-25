using System.Collections.Immutable;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Api.Live;

/// <summary>
/// 重新檢查連線：工作階段失效（登出、被停用、密碼被重設）就斷線；專案被加入或移出、角色改變就調整群組。
/// 連線是長時間的，Cookie 只在連線建立的那一刻驗過，沒有這一步，登出之後連線還會一直收到推送。
/// 兩種時機：每隔 <see cref="Interval"/> 檢查所有連線（保險，訊號漏掉時靠它），
/// 以及收到跨程序訊號（含這個程序自己發的）時立刻重算受影響的人或帳號。
/// 確認工作階段用唯讀的 <see cref="ISessionService.IsActiveAsync"/>，不用 FindAsync：後者每次都記下使用時刻、延長閒置期限，
/// 開著不動的分頁會因此永遠不會閒置失效。
/// 查詢出錯時（資料庫暫時連不上）那一條連線不動、下一輪再來：不能因為一次查詢失敗就把所有人斷線。
/// 被移出專案之後，最多一個間隔的時間還會收到那個專案的「有變動」（只有編號，重抓時 API 會擋下）。
/// </summary>
public sealed partial class LiveConnectionRefresher(
    IServiceScopeFactory scopes, LiveConnections connections, LiveGroupSync sync, ILogger<LiveConnectionRefresher> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private bool _waitingForMigrations;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 程序要結束了，不是錯誤。
        }
    }

    /// <summary>檢查所有連線一輪，回傳被斷開的連線數。測試直接呼叫它，不必等計時器。</summary>
    public Task<int> RunOnceAsync(CancellationToken cancellationToken) => RefreshAsync(null, cancellationToken);

    /// <summary>
    /// 只檢查這些人的連線（null 是全部）。還有沒套用的遷移就跳過；任何例外都記成 Error 後吞掉，
    /// 不讓背景服務停止（.NET 預設會讓整個主機跟著結束）。回傳被斷開的連線數。
    /// </summary>
    public Task<int> RefreshAsync(IReadOnlyCollection<Guid>? personIds, CancellationToken cancellationToken) =>
        RefreshWhereAsync(c => personIds is null || personIds.Contains(c.PersonId), cancellationToken);

    /// <summary>只檢查這個帳號的連線（憑證作廢的訊號只帶帳號）。工作階段直接查資料庫，不經過快取，所以不必管誰先誰後。</summary>
    public Task<int> RefreshAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        RefreshWhereAsync(c => c.AccountId == accountId, cancellationToken);

    private async Task<int> RefreshWhereAsync(Func<LiveConnection, bool> filter, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await HasPendingMigrationsAsync(scope.ServiceProvider, cancellationToken)
                ? 0
                : await CheckAsync(scope.ServiceProvider, filter, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogRunFailed(exception);
            return 0;
        }
    }

    private async Task<int> CheckAsync(IServiceProvider services, Func<LiveConnection, bool> filter, CancellationToken cancellationToken)
    {
        var sessions = services.GetRequiredService<ISessionService>();
        var resolver = services.GetRequiredService<LiveGroupResolver>();
        var people = services.GetRequiredService<IPeopleDirectory>();

        // 同一個人的連線共用一次群組的計算；同一個工作階段（同一個瀏覽器的好幾個分頁）共用一次確認。
        Dictionary<string, bool> activeSessions = new(StringComparer.Ordinal);
        Dictionary<Guid, ImmutableHashSet<string>?> groupsByPerson = [];
        var aborted = 0;
        foreach (var connection in connections.Snapshot().Where(filter))
        {
            try
            {
                if (!await IsActiveAsync(sessions, activeSessions, connection, cancellationToken))
                {
                    connection.Abort();
                    aborted++;
                    continue;
                }

                if (await GroupsOfAsync(people, resolver, groupsByPerson, connection, cancellationToken) is { } groups)
                {
                    await sync.ApplyAsync(connection, groups, cancellationToken);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogRefreshFailed(exception);
            }
        }

        return aborted;
    }

    private static async Task<bool> IsActiveAsync(
        ISessionService sessions, Dictionary<string, bool> known, LiveConnection connection, CancellationToken cancellationToken)
    {
        if (!known.TryGetValue(connection.SessionToken, out var isActive))
        {
            known[connection.SessionToken] = isActive = await sessions.IsActiveAsync(connection.SessionToken, cancellationToken);
        }

        return isActive;
    }

    /// <summary>
    /// 角色可能變了（升成管理員就看得到所有專案），所以每次都重新問人員目錄；找不到這個人就是 null，連線維持原樣。
    /// </summary>
    private static async Task<ImmutableHashSet<string>?> GroupsOfAsync(
        IPeopleDirectory people,
        LiveGroupResolver resolver,
        Dictionary<Guid, ImmutableHashSet<string>?> known,
        LiveConnection connection,
        CancellationToken cancellationToken)
    {
        if (known.TryGetValue(connection.PersonId, out var groups))
        {
            return groups;
        }

        var found = await people.FindManyAsync(connection.OrganizationId, [connection.PersonId], cancellationToken);
        groups = found.TryGetValue(connection.PersonId, out var person)
            ? await resolver.ResolveAsync(
                new Actor(connection.OrganizationId, connection.PersonId, person.Role is OrganizationRole.Owner or OrganizationRole.Admin),
                cancellationToken)
            : null;
        known[connection.PersonId] = groups;
        return groups;
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
            LogWaitingForMigrations(string.Join("、", pending));
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "還有沒套用的資料表遷移（{Pending}），即時連線的重新檢查先暫停，遷移完會自動恢復。")]
    private partial void LogWaitingForMigrations(string pending);

    [LoggerMessage(Level = LogLevel.Error, Message = "重新檢查即時連線這一輪失敗，下一輪重來。")]
    private partial void LogRunFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "重新檢查即時連線失敗，這一條維持原樣，下一輪再檢查。")]
    private partial void LogRefreshFailed(Exception exception);
}
