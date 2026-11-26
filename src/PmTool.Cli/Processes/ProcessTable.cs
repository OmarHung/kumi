using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace PmTool.Cli.Processes;

/// <summary>ps 的一列：程序編號、父程序編號、啟動時間（ps 印出的原文，只拿來比對是不是同一個程序）。</summary>
internal sealed record ProcessEntry(int ProcessId, int ParentId, string Start);

/// <summary>
/// 程序編號會被重用：只憑編號去結束程序，可能結束到別的程序。所以記下程序時一併記下它的啟動時間，之後動手前再比對。
/// 啟動時間用 <c>ps</c> 的 <c>lstart</c>（macOS 與 Linux 都有，秒為單位；編號要在同一秒內被重用才會誤判，等於不可能）。
/// 只在 macOS 與 Linux 做事；Windows 回空的（Windows 靠 <c>Kill(entireProcessTree)</c>）。
/// </summary>
internal static class ProcessTable
{
    /// <summary>讀整張表；沒有 ps 或讀不出來回空的。</summary>
    public static IReadOnlyList<ProcessEntry> Read() => RunPs(["-axo", "pid=,ppid=,lstart="]) is { } output ? Parse(output) : [];

    /// <summary>某個程序現在的啟動時間；沒有這個程序或查不出來是 null。</summary>
    public static string? StartOf(int processId)
    {
        if (processId <= 0 || RunPs(["-o", "lstart=", "-p", processId.ToString(CultureInfo.InvariantCulture)]) is not { } output)
        {
            return null;
        }

        return Normalize(output) is { Length: > 0 } start ? start : null;
    }

    /// <summary>這個編號現在的程序，啟動時間還是記下來的那個嗎？記下來的是 null（當時查不到）一律當作不是，不動手。</summary>
    public static bool IsSameProcess(int processId, string? recordedStart) =>
        recordedStart is { Length: > 0 } && string.Equals(StartOf(processId), recordedStart, StringComparison.Ordinal);

    /// <summary>每列「編號 父編號 啟動時間（五個欄位：星期 月 日 時間 年）」；格式不對的列略過。</summary>
    internal static IReadOnlyList<ProcessEntry> Parse(string output)
    {
        List<ProcessEntry> entries = [];
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3
                && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var processId)
                && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var parentId)
                && Normalize(parts[2]) is { Length: > 0 } start)
            {
                entries.Add(new ProcessEntry(processId, parentId, start));
            }
        }

        return entries;
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>固定用 C 語系與 UTC：記錄與比對用同一份，TZ 在兩次之間變了（夏令時間、使用者改設定）啟動時間字串也不會不一樣。</summary>
    internal static void ApplyStableEnvironment(ProcessStartInfo info)
    {
        info.Environment["LC_ALL"] = "C";
        info.Environment["TZ"] = "UTC";
    }

    private static string? RunPs(string[] arguments)
    {
        if (OperatingSystem.IsWindows() || SystemCommands.Find("ps") is not { } psPath)
        {
            return null;
        }

        try
        {
            var info = new ProcessStartInfo(psPath) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            ApplyStableEnvironment(info);
            using var ps = SystemProcess.Start(info);
            if (ps is null)
            {
                return null;
            }

            var output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit();
            return output;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            // 沒有 ps 就查不出來：呼叫端會當作不是同一個程序，不動手。
            return null;
        }
    }
}
