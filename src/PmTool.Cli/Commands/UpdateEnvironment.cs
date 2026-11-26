using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Commands;

internal enum UpdateOs
{
    MacOS,
    Windows,
    Other,
}

/// <summary>
/// kumi update 碰得到的外部環境：自己的執行檔在哪、是不是從伺服器下載的單一執行檔、作業系統與架構、試跑新檔的方式。
/// 全部由外面交進來，測試才不必真的換掉測試程式自己，也能在 macOS 上跑 Windows 的改名流程。
/// </summary>
/// <summary>子程序的結果：結束代碼與標準輸出、標準錯誤。</summary>
internal sealed record ProcessOutput(int ExitCode, string Output, string Error);

internal sealed record UpdateEnvironment(
    string? ExecutablePath,
    bool IsStandalone,
    UpdateOs Os,
    Architecture OsArchitecture,
    Architecture ProcessArchitecture,
    Func<string, CancellationToken, Task<string?>> SmokeRun)
{
    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    // 試跑的是剛下載、還沒信任的檔案，不能讓它拿到我們的權杖。
    private static readonly string[] TokenVariables = ["KUMI_TOKEN", "PM_TOKEN", "KUMI_MCP_TOKEN"];

    /// <summary>下載時這麼久沒收到任何資料就放棄；測試換成短的。</summary>
    public TimeSpan DownloadIdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Windows 上改名被佔住（防毒軟體剛掃完新檔）時，重試之間的等待；測試換成零。</summary>
    public TimeSpan MoveRetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 換好執行檔之後，用<b>新的</b>執行檔跑 <c>skill update</c>（舊的程序內嵌的是舊版 skill）；回傳結束代碼與它印的文字；跑不起來回 null。
    /// 測試換成假的，不真的啟動程序。
    /// </summary>
    public Func<string, CancellationToken, Task<ProcessOutput?>> RunSkillUpdate { get; init; } = static (_, _) => Task.FromResult<ProcessOutput?>(null);

    public bool IsWindows => Os == UpdateOs.Windows;

    public static UpdateEnvironment Current() => new(
        Environment.ProcessPath,
        IsStandaloneBuild(),
        OperatingSystem.IsMacOS() ? UpdateOs.MacOS : OperatingSystem.IsWindows() ? UpdateOs.Windows : UpdateOs.Other,
        RuntimeInformation.OSArchitecture,
        RuntimeInformation.ProcessArchitecture,
        static async (path, cancellation) =>
            await RunAsync(path, ["--version"], cancellation) is { ExitCode: 0 } run ? run.Output.Trim() : null)
    {
        RunSkillUpdate = static (path, cancellation) => RunAsync(path, ["skill", "update"], cancellation),
    };

    /// <summary>
    /// 依序試的平台代碼：先看電腦本身的架構，再看這個程序的架構。
    /// Windows 的 ARM 電腦只有 x64 的版本（靠模擬執行），所以第一個找不到時要退到程序的架構。
    /// </summary>
    public IReadOnlyList<string> PlatformCandidates() =>
        [.. new[] { OsArchitecture, ProcessArchitecture }.Select(PlatformOf).OfType<string>().Distinct()];

    private string? PlatformOf(Architecture architecture) => (Os, architecture) switch
    {
        (UpdateOs.MacOS, Architecture.Arm64) => "macos-arm64",
        (UpdateOs.MacOS, Architecture.X64) => "macos-x64",
        (UpdateOs.Windows, Architecture.X64) => "windows-x64",
        _ => null,
    };

    private static bool IsStandaloneBuild() =>
        typeof(UpdateEnvironment).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute => attribute.Key == "KumiDistribution" && attribute.Value == "standalone");

    /// <summary>執行 &lt;檔案&gt; 加參數，回傳結束代碼與輸出；跑不起來或逾時回 null。參數不經過 shell。</summary>
    private static async Task<ProcessOutput?> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var name in TokenVariables)
        {
            info.Environment.Remove(name);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = SystemProcess.Start(info);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using var running = process;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SmokeTimeout);
        try
        {
            // 標準錯誤也要讀掉，不然緩衝滿了子程序會卡住。
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessOutput(process.ExitCode, output, await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                // 等它真的結束，Windows 才刪得掉還被它開著的暫存檔。
                process.WaitForExit(KillWait);
            }
        }
        catch (InvalidOperationException)
        {
            // 剛好在檢查之後結束了，不用殺。
        }
    }
}
