using Microsoft.AspNetCore.SignalR;
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

    /// <summary>
    /// 這些人的專案權限剛變了（專案建立、成員設定或移除、角色改變、停用），立刻重算他們的連線，不等每兩分鐘的檢查。
    /// null 是所有人（新專案：管理員看得到全部）。立刻回來，重算在背景做，失敗只記錄。
    /// </summary>
    void AccessChanged(IReadOnlyCollection<Guid>? personIds);
}

public sealed class NullLiveNotifier : ILiveNotifier
{
    public Task InboxChangedAsync(Guid personId, CancellationToken cancellationToken) => Task.CompletedTask;

    public void AccessChanged(IReadOnlyCollection<Guid>? personIds)
    {
    }
}

internal sealed partial class HubLiveNotifier(
    IHubContext<LiveHub> hub, LiveSender sender, LiveConnectionRefresher refresher, ILogger<HubLiveNotifier> logger) : ILiveNotifier
{
    public Task InboxChangedAsync(Guid personId, CancellationToken cancellationToken) =>
        sender.SendAsync(
            $"inbox:{personId}",
            token => hub.Clients.Group(LiveGroups.Person(personId)).SendAsync(LiveMessages.Inbox, new LiveInboxMessage(), token),
            cancellationToken);

    public void AccessChanged(IReadOnlyCollection<Guid>? personIds) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await refresher.RefreshAsync(personIds, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogRefreshFailed(exception);
            }
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "專案權限變了，重算即時連線的群組失敗；要等每兩分鐘的檢查才會更正。")]
    private partial void LogRefreshFailed(Exception exception);
}

/// <summary>
/// 通知模組存好新通知之後，叫收件人的連線去重抓收件匣。收件人同時送，每一個有自己的期限
/// （<see cref="LiveSender"/>），一個人的連線卡住不會拖住其他人，也不會拖住通知的處理。
/// </summary>
internal sealed class LiveNotificationObserver(ILiveNotifier notifier) : INotificationObserver
{
    public Task NotifiedAsync(IReadOnlyCollection<NotifiedRecipient> recipients, CancellationToken cancellationToken) =>
        Task.WhenAll(recipients.Select(r => r.PersonId).Distinct().Select(personId => notifier.InboxChangedAsync(personId, cancellationToken)));
}
