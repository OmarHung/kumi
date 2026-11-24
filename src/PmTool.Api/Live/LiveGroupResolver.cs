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
            foreach (var group in desired.Except(connection.Groups))
            {
                await hub.Groups.AddToGroupAsync(connection.ConnectionId, group, cancellationToken);
                connection.Groups = connection.Groups.Add(group);
            }

            foreach (var group in connection.Groups.Except(desired))
            {
                await hub.Groups.RemoveFromGroupAsync(connection.ConnectionId, group, cancellationToken);
                connection.Groups = connection.Groups.Remove(group);
            }
        }
        finally
        {
            connection.GroupLock.Release();
        }
    }
}
