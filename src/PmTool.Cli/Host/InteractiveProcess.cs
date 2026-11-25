using System.ComponentModel;
using System.Diagnostics;
using PmTool.Cli.Agent;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Host;

/// <summary>
/// 跑 CLI 自己的登入：<b>繼承這個終端機</b>（標準輸入、輸出、錯誤都不接管），登入碼、網址、貼上的 API 金鑰只在使用者與 CLI 之間，
/// Kumi 看不到、不轉送。環境變數拿掉會讓它改用別的計費來源的那些（和跑工作時同一份清單）。
/// </summary>
internal static class InteractiveProcess
{
    /// <summary>結束代碼；起不來回 null。取消時結束整個程序樹。</summary>
    public static async Task<int?> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var name in ProcessEngine.RemovedVariables)
        {
            info.Environment.Remove(name);
        }

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
