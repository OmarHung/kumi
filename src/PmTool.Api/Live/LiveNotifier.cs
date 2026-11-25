using PmTool.Abstractions;
using PmTool.Notifications;

namespace PmTool.Api.Live;

/// <summary>
/// 端點與通知模組告訴即時連線「有東西變了」的入口。即時推送關掉時換成什麼都不做的實作，呼叫的人不必判斷。
/// 兩個方法都不會讓呼叫端等或失敗：推送出錯、逾時只是畫面晚一點更新。
/// </summary>
public interface ILiveNotifier
{
    /// <summary>叫某個人所有的連線去重抓收件匣。送完（或逾時放棄）才完成；端點不要等它。</summary>
    Task InboxChangedAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>叫這些人所有的連線去重抓收件匣；一批人只發一則訊號（別的程序那邊每 150 人拆成一則）。</summary>
    Task InboxChangedAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken);

    /// <summary>
    /// 這些人的專案權限剛變了（專案建立、成員設定或移除、角色改變、停用），立刻重算他們的連線，不等每兩分鐘的檢查。
    /// null 是所有人（新專案：管理員看得到全部）。立刻回來，重算在背景做，失敗只記錄。
    /// </summary>
    void AccessChanged(IReadOnlyCollection<Guid>? personIds);
}

public sealed class NullLiveNotifier : ILiveNotifier
{
    public Task InboxChangedAsync(Guid personId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InboxChangedAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public void AccessChanged(IReadOnlyCollection<Guid>? personIds)
    {
    }
}

/// <summary>
/// 只負責把「有東西變了」交給跨程序訊號：這個程序與別的程序裡的連線都由接收端（<see cref="LiveClusterSignals"/>）處理，
/// 這裡不自己重算或推送，免得本機算兩次。
/// </summary>
public sealed class HubLiveNotifier(IClusterSignals signals) : ILiveNotifier
{
    public Task InboxChangedAsync(Guid personId, CancellationToken cancellationToken) =>
        InboxChangedAsync([personId], cancellationToken);

    public Task InboxChangedAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var distinct = personIds.Distinct().ToList();
        if (distinct.Count > 0)
        {
            signals.Publish(new ClusterSignal(ClusterSignalKind.InboxChanged) { PersonIds = distinct });
        }

        return Task.CompletedTask;
    }

    public void AccessChanged(IReadOnlyCollection<Guid>? personIds) =>
        signals.Publish(new ClusterSignal(ClusterSignalKind.AccessChanged)
        {
            AllPeople = personIds is null,
            PersonIds = personIds is null ? [] : [.. personIds],
        });
}

/// <summary>
/// 通知模組存好新通知之後，叫收件人的連線去重抓收件匣。一次通知很多人時合併成一則訊號；送出時各條連線同時送，一個人的連線卡住不會拖住其他人，
/// 也不會拖住通知的處理（送出在訊號的接收端，逐條連線、各有期限，見 <see cref="LiveSender"/>）。
/// </summary>
public sealed class LiveNotificationObserver(ILiveNotifier notifier) : INotificationObserver
{
    public Task NotifiedAsync(IReadOnlyCollection<NotifiedRecipient> recipients, CancellationToken cancellationToken) =>
        notifier.InboxChangedAsync([.. recipients.Select(r => r.PersonId).Distinct()], cancellationToken);
}
