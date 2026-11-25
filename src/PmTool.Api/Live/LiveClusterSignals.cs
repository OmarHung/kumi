using PmTool.Abstractions;

namespace PmTool.Api.Live;

/// <summary>
/// 即時連線這一端的跨程序訊號接收端（只有即時更新開著時註冊）：憑證作廢與權限變動立刻重算這個程序裡的連線，
/// 收件匣有變推給這些人的連線。連線的接收端斷過又接上，中間的訊號可能漏了，整個重新檢查一輪。
/// </summary>
internal sealed class LiveClusterSignals(LiveConnectionRefresher refresher, LiveConnections connections, LiveSender sender)
    : IClusterSignalHandler
{
    public Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken) => signal.Kind switch
    {
        ClusterSignalKind.CredentialsRevoked when signal.AccountId is { } accountId =>
            refresher.RefreshAccountAsync(accountId, cancellationToken),
        ClusterSignalKind.AccessChanged =>
            refresher.RefreshAsync(signal.AllPeople ? null : signal.PersonIds.ToHashSet(), cancellationToken),
        ClusterSignalKind.InboxChanged => PushInboxAsync(signal.PersonIds, cancellationToken),
        _ => Task.CompletedTask,
    };

    public Task ResynchronizeAsync(CancellationToken cancellationToken) => refresher.RunOnceAsync(cancellationToken);

    private Task PushInboxAsync(IReadOnlyList<Guid> people, CancellationToken cancellationToken)
    {
        var targets = people.Distinct().SelectMany(person => connections.InGroup(LiveGroups.Person(person))).ToList();
        return targets.Count == 0
            ? Task.CompletedTask
            : sender.SendToAsync(targets, LiveMessages.Inbox, new LiveInboxMessage(), cancellationToken);
    }
}
