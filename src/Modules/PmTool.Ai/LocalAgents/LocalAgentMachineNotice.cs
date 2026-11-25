using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 同一個人第一次出現沒見過的電腦（機器編號）時，把帳號事件交給所有 <see cref="IAccountEventSink"/>（通知模組變成帳號通知與信）。
/// 偷到本機代理權杖的人可以登記一台假電腦，本人要有機會發現；但同一個人 10 分鐘內最多一則，不讓它洗收件匣。要在登記的交易提交之後呼叫：
/// 回復的登記不該發通知。盡力送：失敗或逾時只記警告，不影響已經完成的登記（做法同身分模組的 <c>AccountEvents</c>）。
/// </summary>
internal sealed partial class LocalAgentMachineNotice(
    IEnumerable<IAccountEventSink> sinks, IRequestSource requestSource, ILogger<LocalAgentMachineNotice> logger)
{
    /// <summary>同一個人最多每隔這麼久發一則（<c>LocalAgentService</c> 用電腦的建立時間判斷）。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IAccountEventSink> _sinks = [.. sinks];

    /// <summary>不會丟例外。</summary>
    public async Task PublishAsync(Actor actor, string machineName, DateTimeOffset now)
    {
        if (_sinks.Count == 0)
        {
            return;
        }

        var accountEvent = new AccountEvent(Guid.CreateVersion7(), actor.OrganizationId, actor.PersonId, AccountEventKind.LocalAgentMachineAdded, now)
        {
            MachineName = machineName,
            SourceAddress = RequestSourceText.Clean(requestSource.Current?.Address, RequestSourceText.MaxAddressLength),
        };

        // 不綁呼叫端的取消權杖：登記已經提交了，請求斷線不該讓通知跟著掉。
        using var cancellation = new CancellationTokenSource(Timeout);
        var deliveries = _sinks.Select(sink => DeliverAsync(sink, accountEvent, cancellation.Token)).ToArray();
        try
        {
            await Task.WhenAll(deliveries).WaitAsync(Timeout);
        }
        catch (TimeoutException)
        {
            LogTimedOut(logger, accountEvent.EventId);
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
            LogFailed(logger, accountEvent.EventId, sink.GetType().Name, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "新電腦的帳號事件 {EventId} 超過時間還沒送完，不再等。")]
    private static partial void LogTimedOut(ILogger logger, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "新電腦的帳號事件 {EventId} 交給 {Sink} 時失敗。")]
    private static partial void LogFailed(ILogger logger, Guid eventId, string sink, Exception exception);
}
