using System.Collections.Immutable;
using Microsoft.AspNetCore.SignalR;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Live;

/// <summary>
/// 一個人現在該在哪些群組。權限由工作引擎決定（<see cref="IWorkAccessQueries"/>），這裡只把它換成群組：
/// 看得到的專案裡，只看自己項目的放進各自的 <c>own:</c> 群組，其他放進 <c>project:</c>；
/// 另外每個人都有自己的 <c>person:</c> 群組（收件匣）。
/// </summary>
public sealed class LiveGroupResolver(IWorkAccessQueries access)
{
    public async Task<ImmutableHashSet<string>> ResolveAsync(Actor actor, CancellationToken cancellationToken)
    {
        var readable = await access.ReadableProjectIdsAsync(actor, cancellationToken);
        var ownOnly = await access.OwnOnlyProjectIdsAsync(actor, cancellationToken);
        var groups = readable.Select(project => ownOnly.Contains(project)
            ? LiveGroups.Own(project, actor.PersonId)
            : LiveGroups.Project(project));
        return [LiveGroups.Person(actor.PersonId), .. groups];
    }
}

/// <summary>讓一條連線的群組變成指定的樣子：只加缺的、只退多的，沒變的不動。</summary>
public sealed class LiveGroupSync(IHubContext<LiveHub> hub)
{
    public async Task ApplyAsync(LiveConnection connection, ImmutableHashSet<string> desired, CancellationToken cancellationToken)
    {
        await connection.GroupLock.WaitAsync(cancellationToken);
        try
        {
            // 推送是依連線自己記的 Groups 找人。離開時先改 Groups、再通知 SignalR：權限剛被拿掉的人，
            // 在等 SignalR 的這段時間裡就已經不會再被送到；萬一 SignalR 那邊失敗，只是多留一筆沒人用的成員資料。
            foreach (var group in connection.Groups.Except(desired))
            {
                connection.Groups = connection.Groups.Remove(group);
                await hub.Groups.RemoveFromGroupAsync(connection.ConnectionId, group, cancellationToken);
            }

            // 加入時反過來：先讓 SignalR 加進去成功、才開始被送到；加入失敗時 Groups 沒有這個群組，下一輪檢查會再試。
            foreach (var group in desired.Except(connection.Groups))
            {
                await hub.Groups.AddToGroupAsync(connection.ConnectionId, group, cancellationToken);
                connection.Groups = connection.Groups.Add(group);
            }
        }
        finally
        {
            connection.GroupLock.Release();
        }
    }
}
