using System.Threading.Channels;
using Npgsql;
using PmTool.Abstractions;

namespace PmTool.Api.Cluster;

/// <summary>
/// 跨程序訊號的匯流排。<see cref="Publish"/> 只把訊號放進有界的佇列（立刻回來、不丟例外）；
/// 一個分派的 pump 把訊號派進「每個接收端各自的佇列」與「送給別的程序的佇列」，派送本身不等任何人。
/// 每個接收端有自己的 pump：同一個接收端裡的訊號照順序，接收端之間互不等待，一個很慢的接收端（例如為每條連線查資料庫）
/// 不會讓即時的接收端（清快取）晚收到。送給別的程序（<c>pg_notify</c>）也是獨立的一條，不排在本機分送的路徑上。
/// 別的程序送來的訊號（<see cref="Receive"/>）只分送、不再送回去。
/// 每個接收端有期限，到期就真的取消它拿到的權杖（不只是不等它）。
/// 接收端的佇列滿了不能靜靜丟掉：記下「需要整個重新同步」，等它清出空位時清掉排著的（重新同步已經涵蓋它們）、呼叫一次
/// <see cref="IClusterSignalHandler.ResynchronizeAsync"/>。送給別的程序的佇列滿了丟最舊的，別的程序最慢退回快取的期限。
/// 接收端在第一次用到時才向容器要：身分模組的快取發訊號、同時又是接收端，建構時互相要會繞成圈。
/// </summary>
public sealed partial class ClusterSignalBus : BackgroundService, IClusterSignals
{
    public const int QueueCapacity = 1_024;

    /// <summary>每個接收端、每次送出各自的期限：一個卡住的接收端不能拖住後面的訊號。</summary>
    public static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(10);

    /// <summary>同一種警告最多隔這麼久記一筆：佇列一滿就是連續很多則，每一則都記只會淹掉記錄。</summary>
    private const long WarningIntervalMilliseconds = 30_000;

    private readonly IServiceProvider _services;
    private readonly ClusterSignalOptions _options;
    private readonly NpgsqlDataSource? _source;
    private readonly ILogger<ClusterSignalBus> _logger;
    private readonly TimeSpan _handlerTimeout;
    private readonly int _laneCapacity;
    private readonly Channel<Item> _inbound;
    private readonly Channel<ClusterSignal>? _outbound;
    private readonly Throttle _outboundWarning = new();
    private IReadOnlyList<IClusterSignalHandler>? _handlers;

    public ClusterSignalBus(
        IServiceProvider services,
        ClusterSignalOptions options,
        ILogger<ClusterSignalBus> logger,
        NpgsqlDataSource? source = null,
        TimeSpan? handlerTimeout = null,
        int laneCapacity = QueueCapacity)
    {
        _services = services;
        _options = options;
        _source = source;
        _logger = logger;
        _handlerTimeout = handlerTimeout ?? HandlerTimeout;
        _laneCapacity = laneCapacity;
        _inbound = Channel.CreateBounded<Item>(
            new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => LogInboundDropped());
        if (options.Enabled && source is not null)
        {
            _outbound = Channel.CreateBounded<ClusterSignal>(
                new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
                _ =>
                {
                    if (_outboundWarning.ShouldLog())
                    {
                        LogOutboundDropped();
                    }
                });
        }
    }

    /// <summary>這個程序的編號，發出去的訊號都帶它；收到自己發的訊號時略過。</summary>
    public Guid Origin { get; } = Guid.NewGuid();

    public void Publish(ClusterSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var stamped = signal.Origin == Guid.Empty ? signal with { Origin = Origin } : signal;
        _inbound.Writer.TryWrite(new Item(stamped, ShouldNotify: true));
    }

    /// <summary>別的程序送來的訊號：只交給這個程序裡的接收端。</summary>
    public void Receive(ClusterSignal signal)
    {
        if (signal.Origin != Origin)
        {
            _inbound.Writer.TryWrite(new Item(signal, ShouldNotify: false));
        }
    }

    /// <summary>接收的連線斷過又接上：每個接收端把自己快取的整個作廢（各自進行、各有期限）。</summary>
    public Task ResynchronizeAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(Handlers().Select(handler => RunHandlerAsync(handler, (h, token) => h.ResynchronizeAsync(token), cancellationToken)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lanes = Handlers().Select(handler => new Lane(handler, _laneCapacity)).ToList();
        List<Task> pumps = [RouteAsync(lanes, stoppingToken), .. lanes.Select(lane => PumpAsync(lane, stoppingToken))];
        if (_outbound is not null)
        {
            pumps.Add(NotifyPumpAsync(stoppingToken));
        }

        await Task.WhenAll(pumps);
    }

    /// <summary>只派送、不等任何接收端，所以不會被慢的接收端拖住。</summary>
    private async Task RouteAsync(IReadOnlyList<Lane> lanes, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _inbound.Reader.ReadAllAsync(stoppingToken))
            {
                foreach (var lane in lanes)
                {
                    if (!lane.TryEnqueue(item.Signal) && lane.Warning.ShouldLog())
                    {
                        LogLaneFull(lane.Handler.GetType().Name);
                    }
                }

                if (item.ShouldNotify)
                {
                    _outbound?.Writer.TryWrite(item.Signal);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 程序要結束了，不是錯誤。
        }
    }

    private async Task PumpAsync(Lane lane, CancellationToken stoppingToken)
    {
        try
        {
            while (await lane.Queue.Reader.WaitToReadAsync(stoppingToken))
            {
                if (lane.TakeResynchronize())
                {
                    // 重新同步已經涵蓋排著的訊號，不必一則一則補。
                    while (lane.Queue.Reader.TryRead(out _))
                    {
                    }

                    await RunHandlerAsync(lane.Handler, (h, token) => h.ResynchronizeAsync(token), stoppingToken);
                }
                else if (lane.Queue.Reader.TryRead(out var signal))
                {
                    await RunHandlerAsync(lane.Handler, (h, token) => h.HandleAsync(signal, token), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 程序要結束了，不是錯誤。
        }
    }

    private async Task NotifyPumpAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var signal in _outbound!.Reader.ReadAllAsync(stoppingToken))
            {
                await NotifyAsync(signal, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 程序要結束了，不是錯誤。
        }
    }

    private async Task RunHandlerAsync(
        IClusterSignalHandler handler, Func<IClusterSignalHandler, CancellationToken, Task> run, CancellationToken cancellationToken)
    {
        // 到期就取消它拿到的權杖：不然一個不回來的接收端會留在背景繼續跑，越疊越多。
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_handlerTimeout);
        try
        {
            await run(handler, deadline.Token).WaitAsync(deadline.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogHandlerFailed(exception, handler.GetType().Name);
        }
    }

    private async Task NotifyAsync(ClusterSignal signal, CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_handlerTimeout);
            foreach (var payload in ClusterSignalText.SerializeAll(signal))
            {
                await using var command = _source!.CreateCommand("SELECT pg_notify(@channel, @payload)");
                command.Parameters.AddWithValue("channel", ClusterSignalText.Channel);
                command.Parameters.AddWithValue("payload", payload);
                await command.ExecuteNonQueryAsync(deadline.Token);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 送不出去不影響這個程序；別的程序最慢等快取的期限與定期檢查。
            LogNotifyFailed(exception);
        }
    }

    private IReadOnlyList<IClusterSignalHandler> Handlers() =>
        _handlers ??= [.. _services.GetServices<IClusterSignalHandler>()];

    private sealed record Item(ClusterSignal Signal, bool ShouldNotify);

    /// <summary>警告的節流：同一個來源在間隔內只記一次。</summary>
    private sealed class Throttle
    {
        private long _last = long.MinValue;

        public bool ShouldLog()
        {
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _last);
            if (last != long.MinValue && now - last < WarningIntervalMilliseconds)
            {
                return false;
            }

            return Interlocked.CompareExchange(ref _last, now, last) == last;
        }
    }

    /// <summary>一個接收端的佇列。滿了就記下「要整個重新同步」，不丟訊號當作沒事。</summary>
    private sealed class Lane(IClusterSignalHandler handler, int capacity)
    {
        private int _needsResynchronize;

        public IClusterSignalHandler Handler { get; } = handler;

        public Throttle Warning { get; } = new();

        public Channel<ClusterSignal> Queue { get; } = Channel.CreateBounded<ClusterSignal>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

        public bool TryEnqueue(ClusterSignal signal)
        {
            if (Queue.Writer.TryWrite(signal))
            {
                return true;
            }

            Volatile.Write(ref _needsResynchronize, 1);
            return false;
        }

        public bool TakeResynchronize() => Interlocked.Exchange(ref _needsResynchronize, 0) == 1;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "跨程序訊號的佇列滿了（程序剛啟動、還沒開始分送），丟掉最舊的一則；接收端靠定期檢查補回。")]
    private partial void LogInboundDropped();

    [LoggerMessage(Level = LogLevel.Warning, Message = "跨程序訊號送給別的程序的佇列滿了，丟掉最舊的；別的程序最慢等快取到期。")]
    private partial void LogOutboundDropped();

    [LoggerMessage(Level = LogLevel.Warning, Message = "跨程序訊號的接收端 {Handler} 來不及處理、佇列滿了；清出空位之後會整個重新同步一次。")]
    private partial void LogLaneFull(string handler);

    [LoggerMessage(Level = LogLevel.Warning, Message = "跨程序訊號的接收端 {Handler} 處理失敗或逾時。")]
    private partial void LogHandlerFailed(Exception exception, string handler);

    [LoggerMessage(Level = LogLevel.Warning, Message = "跨程序訊號沒有送給別的程序；那邊要等快取到期或定期檢查才會更新。")]
    private partial void LogNotifyFailed(Exception exception);
}
