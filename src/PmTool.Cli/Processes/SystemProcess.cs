using System.Diagnostics;

namespace PmTool.Cli.Processes;

/// <summary>
/// kumi 啟動外部程式的唯一入口：程式必須是絕對路徑、不交給系統的 shell。
/// 之後有人加了裸名稱或 UseShellExecute，會在這裡被擋下（而不是靜默地依工作目錄解析）。
/// </summary>
internal static class SystemProcess
{
    public static Process? Start(ProcessStartInfo info)
    {
        // 不能用 IsPathRooted：Windows 上 \tool.exe（目前磁碟機的根）與 C:tool.exe（磁碟機的目前資料夾）也算 rooted，卻依目前位置解析。
        if (!Path.IsPathFullyQualified(info.FileName))
        {
            throw new ArgumentException($"啟動程式必須用絕對路徑：{info.FileName}", nameof(info));
        }

        if (info.UseShellExecute)
        {
            throw new ArgumentException("啟動程式不經過系統的 shell。", nameof(info));
        }

        return Process.Start(info);
    }
}
