namespace PmTool.Cli.Agent.Autostart;

/// <summary>
/// 背景程序開頭那幾個要連伺服器的呼叫：登入電腦時網路常常還沒好，連不上、5xx、429 都不能就此結束，
/// 依退避（1、2、4…最多 60 秒）一直試；401、403 與其他錯誤照舊丟出去，結束並說明。
/// </summary>
internal static class ServiceRetry
{
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, AgentOptions options, Action<string> notice, CancellationToken cancellation)
    {
        for (var failures = 1; ; failures++)
        {
            try
            {
                return await action();
            }
            catch (CliException exception) when (AgentLoop.IsTransient(exception))
            {
                var wait = AgentLoop.BackoffFor(failures, MaxBackoff);
                notice($"{exception.Message}（{wait.TotalSeconds:0} 秒後重試）");
                await options.Delay(wait, cancellation);
            }
        }
    }
}
