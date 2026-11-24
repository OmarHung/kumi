using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace PmTool.Cli.Agent;

/// <summary>
/// .NET 沒辦法替子程序開新的程序群組，主程序一結束就找不到它的孫程序了。所以在主程序還活著時（拿到結果的時候）
/// 用 <c>ps</c> 記下它底下的程序，主程序結束之後把還活著的結束掉。只在 macOS 與 Linux 做事；Windows 靠 <c>Kill(entireProcessTree)</c>。
/// </summary>
internal sealed class DescendantTracker
{
    private readonly HashSet<int> _known = [];

    public void Snapshot(int rootId)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var info = new ProcessStartInfo("ps") { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add("-axo");
            info.ArgumentList.Add("pid=,ppid=");
            using var ps = Process.Start(info);
            if (ps is null)
            {
                return;
            }

            var parents = new Dictionary<int, int>();
            foreach (var line in ps.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var pid) && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var parent))
                {
                    parents[pid] = parent;
                }
            }

            ps.WaitForExit();
            lock (_known)
            {
                Collect(rootId, parents);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            // 沒有 ps 就沒辦法清，主程序的程序樹還是會被結束。
        }
    }

    private void Collect(int rootId, Dictionary<int, int> parents)
    {
        var added = true;
        var family = new HashSet<int> { rootId };
        while (added)
        {
            added = false;
            foreach (var (pid, parent) in parents)
            {
                if (family.Contains(parent) && family.Add(pid))
                {
                    _known.Add(pid);
                    added = true;
                }
            }
        }
    }

    public async Task KillSurvivorsAsync()
    {
        int[] pids;
        lock (_known)
        {
            pids = [.. _known];
        }

        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or OperationCanceledException)
            {
                // 已經結束了。
            }
        }
    }
}
