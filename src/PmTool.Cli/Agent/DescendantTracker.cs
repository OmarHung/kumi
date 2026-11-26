using System.ComponentModel;
using System.Diagnostics;

using PmTool.Cli.Processes;

namespace PmTool.Cli.Agent;

/// <summary>
/// .NET 沒辦法替子程序開新的程序群組，主程序一結束就找不到它的孫程序了。所以在主程序還活著時（拿到結果的時候）
/// 用 <c>ps</c> 記下它底下的程序（編號加啟動時間），主程序結束之後把還活著而且啟動時間一樣的結束掉：編號可能已經被別的程序重用，
/// 只憑編號會誤殺。只在 macOS 與 Linux 做事；Windows 靠 <c>Kill(entireProcessTree)</c>。
/// </summary>
internal sealed class DescendantTracker
{
    private readonly Dictionary<int, string> _known = [];

    public void Snapshot(int rootId)
    {
        var table = ProcessTable.Read();
        if (table.Count == 0)
        {
            // 沒有 ps 就沒辦法清，主程序的程序樹還是會被結束。
            return;
        }

        lock (_known)
        {
            Collect(rootId, table);
        }
    }

    /// <summary>直接記一個程序（測試用：模擬編號被重用）。</summary>
    internal void Track(int processId, string start)
    {
        lock (_known)
        {
            _known[processId] = start;
        }
    }

    private void Collect(int rootId, IReadOnlyList<ProcessEntry> table)
    {
        var added = true;
        var family = new HashSet<int> { rootId };
        while (added)
        {
            added = false;
            foreach (var entry in table)
            {
                if (family.Contains(entry.ParentId) && family.Add(entry.ProcessId))
                {
                    _known[entry.ProcessId] = entry.Start;
                    added = true;
                }
            }
        }
    }

    public async Task KillSurvivorsAsync()
    {
        KeyValuePair<int, string>[] known;
        lock (_known)
        {
            known = [.. _known];
        }

        foreach (var (pid, start) in known)
        {
            if (!ProcessTable.IsSameProcess(pid, start))
            {
                // 已經結束了，或編號已經是別的程序。
                continue;
            }

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
