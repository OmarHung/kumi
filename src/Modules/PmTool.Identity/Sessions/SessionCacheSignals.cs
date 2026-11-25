using PmTool.Abstractions;

namespace PmTool.Identity.Sessions;

/// <summary>
/// 收到「憑證作廢」的訊號時清掉這個程序的工作階段快取。先空著，內容由跨程序廣播那一包填：
/// 先把註冊接好，之後只動這一個檔案與 <see cref="SessionCache"/>。
/// </summary>
internal sealed class SessionCacheSignals : IClusterSignalHandler
{
    public Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ResynchronizeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
