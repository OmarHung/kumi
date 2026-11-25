using Microsoft.Extensions.Logging;

namespace PmTool.Identity.Audit;

/// <summary>
/// 把帳號事件交給所有 <see cref="IAccountEventSink"/>。要在交易提交之後呼叫：
/// 回復的操作不該發出通知，而身分與通知是不同的資料庫內容、不同的連線，沒辦法放進同一個交易。
/// 可靠的紀錄是稽核表（和操作同一個交易）；這裡是盡力送，所以任何失敗只記警告，不影響已經完成的操作。
/// </summary>
internal sealed class AccountEvents(IEnumerable<IAccountEventSink> sinks, ILogger<AccountEvents> logger, TimeSpan timeout)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IAccountEventSink> _sinks = [.. sinks];

    /// <summary>不會丟例外。每個接收者各自跑，一個慢或壞了不拖累其他的；全部加起來最多等 <see cref="DefaultTimeout"/>。</summary>
    public async Task PublishAsync(AccountEvent accountEvent)
    {
        ArgumentNullException.ThrowIfNull(accountEvent);
        if (_sinks.Count == 0)
        {
            return;
        }

        // 不綁呼叫端的取消權杖：操作已經提交了，請求斷線不該讓通知跟著掉。
        using var cancellation = new CancellationTokenSource(timeout);
        var deliveries = _sinks.Select(sink => DeliverAsync(sink, accountEvent, cancellation.Token)).ToArray();
        try
        {
            // 接收者不理會取消權杖時，靠這裡的等待上限放手；沒送完的讓它自己結束。
            await Task.WhenAll(deliveries).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            // 等待的上限和取消來源的計時器同時到期：等待先放手時，離開這裡就會處理掉取消來源、計時器的取消再也送不出去，
            // 還在跑的接收者會一直等下去。放手之前自己取消。
            await cancellation.CancelAsync();
            logger.LogWarning("帳號事件 {Kind}（{EventId}）超過 {Seconds} 秒還沒送完，不再等。", accountEvent.Kind, accountEvent.EventId, timeout.TotalSeconds);
        }
    }

    private async Task DeliverAsync(IAccountEventSink sink, AccountEvent accountEvent, CancellationToken cancellationToken)
    {
        try
        {
            await sink.PublishAsync(accountEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "帳號事件 {Kind}（{EventId}）交給 {Sink} 時失敗。", accountEvent.Kind, accountEvent.EventId, sink.GetType().Name);
        }
    }
}
