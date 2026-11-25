using PmTool.Abstractions;

namespace PmTool.Identity.Sessions;

/// <summary>
/// 收到「憑證作廢」的訊號時清掉這個程序的工作階段快取（只清本機，不再發訊號）。
/// 接收的連線斷過又接上時，中間的訊號可能漏了，整個快取作廢。
/// </summary>
internal sealed class SessionCacheSignals(SessionCache cache) : IClusterSignalHandler
{
    public Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken)
    {
        if (signal is { Kind: ClusterSignalKind.CredentialsRevoked, AccountId: { } accountId })
        {
            cache.ForgetLocal(accountId);
        }

        return Task.CompletedTask;
    }

    public Task ResynchronizeAsync(CancellationToken cancellationToken)
    {
        cache.Clear();
        return Task.CompletedTask;
    }
}
