using Microsoft.AspNetCore.SignalR;

namespace PmTool.Api.Live;

/// <summary>即時推送的時間上限。測試換成很短的數字，才不必真的等。</summary>
public sealed record LiveOptions
{
    /// <summary>一輪送出最多等多久；超過還沒送完的連線會被斷開。</summary>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>一則要送給一條連線的訊息。</summary>
public readonly record struct LiveDelivery(LiveConnection Connection, string Method, object Message);

/// <summary>
/// 送出推送的唯一入口。一條連線不讀資料（網路卡住、故意不讀），寫入就會一直卡住；
/// SignalR 對群組送出時要等每一條都寫完，一條卡住會拖住所有人，而且看不出是哪一條。
/// 所以一條連線一條連線送：所有連線同時送，一整輪最多等一個 <see cref="LiveOptions.SendTimeout"/>；
/// 超過還沒送完的連線直接斷開（SignalR 的心跳看的是用戶端有沒有送東西，不是有沒有在讀，不能指望它斷開卡住的連線）。
/// 上一次還沒送完的連線，後到的訊息接在後面送（健康的連線一次寫只要幾毫秒，短暫重疊時不能丟掉後到的，網頁不會自己補）；
/// 排滿了才略過，並標成送不完，正在寫的那一則超過期限就斷開，讓網頁重連重抓。
/// </summary>
public sealed partial class LiveSender(IHubContext<LiveHub> hub, LiveOptions options, ILogger<LiveSender> logger)
{
    /// <summary>同一則訊息送給這些連線。</summary>
    public Task SendToAsync(IReadOnlyList<LiveConnection> targets, string method, object message, CancellationToken cancellationToken) =>
        SendAsync([.. targets.Select(c => new LiveDelivery(c, method, message))], cancellationToken);

    /// <summary>
    /// 一批訊息，同一條連線的依順序送；各條連線同時送，整批只有一個期限。
    /// 呼叫端自己的取消（程序關閉）照常往外丟，不當成逾時。
    /// </summary>
    public async Task SendAsync(IReadOnlyList<LiveDelivery> deliveries, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var started = new List<(LiveConnection Connection, Task Pending)>();
        foreach (var perConnection in deliveries.GroupBy(d => d.Connection))
        {
            IReadOnlyList<LiveDelivery> batch = [.. perConnection];
            var connection = perConnection.Key;
            switch (connection.TryEnqueue(batch, () => SendInOrderAsync(connection), options.SendTimeout, now, out var pending))
            {
                case SendStart.Started:
                    started.Add((connection, pending!));
                    break;
                case SendStart.TimedOut:
                    AbortStuck(connection);
                    break;
            }
        }

        if (started.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(started.Select(s => s.Pending)).WaitAsync(options.SendTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            foreach (var (connection, pending) in started.Where(s => !s.Pending.IsCompleted))
            {
                AbortStuck(connection);
            }
        }
    }

    /// <summary>逐批、逐則依順序寫，直到排著的都送完。這一個工作不會失敗：送不出去（連線已經關了）只記錄，不影響別的連線。</summary>
    private async Task SendInOrderAsync(LiveConnection connection)
    {
        try
        {
            while (connection.TryTakeNext(out var batch))
            {
                foreach (var delivery in batch)
                {
                    connection.MarkWriting(DateTimeOffset.UtcNow);
                    await hub.Clients.Client(connection.ConnectionId).SendAsync(delivery.Method, delivery.Message, CancellationToken.None);
                }
            }
        }
        catch (Exception exception)
        {
            connection.AbandonBacklog();
            LogFailed(exception, connection.ConnectionId);
        }
    }

    private void AbortStuck(LiveConnection connection)
    {
        if (connection.TryMarkAborted())
        {
            LogAborted(connection.ConnectionId, connection.PersonId, options.SendTimeout.TotalSeconds);
            connection.Abort();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "即時連線 {ConnectionId}（人員 {PersonId}）超過 {Seconds} 秒還沒收完推送，可能沒有在讀資料，斷開它讓網頁重連。")]
    private partial void LogAborted(string connectionId, Guid personId, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "推送給即時連線 {ConnectionId} 失敗，網頁要等下一次重抓才會更新。")]
    private partial void LogFailed(Exception exception, string connectionId);
}
