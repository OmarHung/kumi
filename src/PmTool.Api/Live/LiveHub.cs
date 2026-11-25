using Microsoft.AspNetCore.SignalR;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Live;

/// <summary>
/// 即時連線的端點（<c>/v1/live</c>）。伺服器單向推給網頁，網頁不呼叫任何方法。
/// 連線時確認是 Cookie 登入的人（存取權杖與沒登入的在更前面的中介軟體就被擋下，見 <see cref="LiveGuards"/>）、
/// 登記、算出群組並加入；斷線時取消登記。連線之後的權限變化由 <see cref="LiveConnectionRefresher"/> 定期調整。
/// </summary>
public sealed partial class LiveHub(
    LiveConnections connections, LiveGroupResolver resolver, LiveGroupSync sync, ILogger<LiveHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = SessionPrincipal.TryRead(Context.User);
        var token = SessionPrincipal.TryReadToken(Context.User);
        if (user is null || token is null)
        {
            Context.Abort();
            return;
        }

        var connection = new LiveConnection(Context.ConnectionId, user.OrganizationId, user.PersonId, user.AccountId, token, Context.Abort);

        // 先登記（占掉名額）再算群組：名額是上限的依據，不能等到查完資料庫才算，不然同時進來的連線都過得了。
        if (!connections.TryAdd(connection))
        {
            Context.Abort();
            return;
        }

        var joined = await JoinGroupsAsync(connection, user);
        if (!joined)
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// 算群組並加入。不管怎麼失敗（算不出來、連線被取消、丟出任何例外）都取消登記，名額才不會洩漏；
    /// 只有一切順利的時候才留著，之後由斷線時的 <see cref="OnDisconnectedAsync"/> 取消。
    /// </summary>
    private async Task<bool> JoinGroupsAsync(LiveConnection connection, AuthenticatedUser user)
    {
        var joined = false;
        try
        {
            await sync.ApplyAsync(connection, await resolver.ResolveAsync(user.ToActor(), Context.ConnectionAborted), Context.ConnectionAborted);
            joined = true;
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            // 網頁在我們算群組的時候就關掉或換頁了，不是錯誤。
            LogCancelled();
        }
        catch (Exception exception)
        {
            // 查不到權限就不能留著：沒有群組的連線什麼都收不到，卻讓網頁以為有在即時更新。斷開它，網頁會重連。
            LogGroupsFailed(exception);
        }
        finally
        {
            if (!joined)
            {
                connections.Remove(connection.ConnectionId);
            }
        }

        return joined;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "即時連線在算群組的時候就被取消了，取消登記。")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "算不出即時連線要加入的群組，連線斷開讓網頁重連。")]
    private partial void LogGroupsFailed(Exception exception);
}
