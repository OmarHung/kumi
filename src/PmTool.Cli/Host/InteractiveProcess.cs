using System.ComponentModel;
using System.Diagnostics;
using PmTool.Cli.Agent;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Host;

/// <summary>
/// 跑 CLI 自己的登入：<b>繼承這個終端機</b>（標準輸入、輸出、錯誤都不接管），登入碼、網址、貼上的 API 金鑰只在使用者與 CLI 之間，
/// Kumi 看不到、不轉送。環境變數只放白名單（和跑工作時同一份，另加開瀏覽器要用的幾個）；工作目錄是每次新建、只有自己進得去的空資料夾，
/// 不用共用的暫存資料夾：那裡別人放的 <c>.mcp.json</c> 之類的專案設定會被引擎讀進去。
/// </summary>
internal static class InteractiveProcess
{
    /// <summary>結束代碼；起不來回 null。取消時結束整個程序樹。</summary>
    public static async Task<int?> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        DirectoryInfo workDirectory;
        try
        {
            workDirectory = PrivateDirectory.Create("kumi-interactive-");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            return await RunInAsync(executable, arguments, workDirectory.FullName, cancellation);
        }
        finally
        {
            PrivateDirectory.DeleteQuietly(workDirectory);
        }
    }

    private static async Task<int?> RunInAsync(string executable, IReadOnlyList<string> arguments, string workDirectory, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workDirectory,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        EngineEnvironment.Apply(info.Environment, new Dictionary<string, string>(), interactive: true);

        Process? process;
        try
        {
            process = SystemProcess.Start(info);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        {
            try
            {
                await process.WaitForExitAsync(cancellation);
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 已經結束了。
                }

                throw;
            }
        }
    }
}
