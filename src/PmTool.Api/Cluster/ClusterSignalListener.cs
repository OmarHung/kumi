using Npgsql;
using PmTool.Abstractions;

namespace PmTool.Api.Cluster;

/// <summary>
/// 專用的一條連線 <c>LISTEN</c>，把別的程序送來的訊號交給 <see cref="ClusterSignalBus"/>。
/// 連線斷了就退避重連；每一次重新接上（第一次不算）對每個接收端做一次整個作廢，補回斷線期間漏掉的訊號。
/// 一條連線不走連線池：被伺服器砍掉的連線不會被池子再借出去。偵測連線死掉靠 TCP 的保活探測，
/// 因為等通知的連線同時不能下別的指令。
/// </summary>
public sealed partial class ClusterSignalListener(
    ClusterSignalConnection connection, ClusterSignalBus bus, ILogger<ClusterSignalListener> logger) : BackgroundService
{
    public const string ApplicationName = "pmtool-cluster-listener";

    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        var needsResynchronize = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(needsResynchronize, () => failures = 0, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogDisconnected(exception);
            }

            // 只要斷過，下一次接上就要整個作廢；第一次連線成功之前沒有快取可以漏。
            needsResynchronize = true;
            var delay = Backoff[Math.Min(failures, Backoff.Length - 1)];
            failures++;
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ListenAsync(bool resynchronize, Action connected, CancellationToken cancellationToken)
    {
        await using var listener = new NpgsqlConnection(connection.ConnectionString);
        listener.Notification += (_, args) => OnNotification(args.Payload);
        await listener.OpenAsync(cancellationToken);
        await using (var command = new NpgsqlCommand($"LISTEN {ClusterSignalText.Channel}", listener))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        connected();

        // 先 LISTEN 再整個作廢：LISTEN 之後的訊號一定收得到，之前漏的靠這一次補。
        if (resynchronize)
        {
            await bus.ResynchronizeAsync(cancellationToken);
        }

        while (true)
        {
            await listener.WaitAsync(cancellationToken);
        }
    }

    private void OnNotification(string payload)
    {
        if (ClusterSignalText.TryParse(payload, out var signal))
        {
            bus.Receive(signal);
        }
        else
        {
            LogIgnored();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "接收跨程序訊號的連線斷了，稍後重連。")]
    private partial void LogDisconnected(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "略過看不懂的跨程序訊號。")]
    private partial void LogIgnored();
}

/// <summary>接收訊號的專用連線字串：執行用的角色，加上辨認用的應用程式名稱與保活設定。</summary>
public sealed record ClusterSignalConnection(string ConnectionString)
{
    public static ClusterSignalConnection From(string runtimeConnectionString) => new(
        new NpgsqlConnectionStringBuilder(runtimeConnectionString)
        {
            ApplicationName = ClusterSignalListener.ApplicationName,
            Pooling = false,
            KeepAlive = 30,
            TcpKeepAlive = true,
            TcpKeepAliveTime = 30,
            TcpKeepAliveInterval = 10,
        }.ConnectionString);
}
