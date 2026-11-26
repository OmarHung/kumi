using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Agent;

internal enum LineVerdict
{
    Continue,

    /// <summary>已經有結果了：引擎應該很快自己結束，超過 <see cref="AgentOptions.ExitGrace"/> 就強制結束。</summary>
    Done,

    /// <summary>馬上結束引擎。</summary>
    Abort,
}

/// <param name="SetVariables">加進子程序環境的變數；子程序的環境其餘只有 <see cref="EngineEnvironment"/> 的白名單。</param>
internal sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string StandardInput,
    IReadOnlyDictionary<string, string> SetVariables);

/// <summary>不送提示的小指令的結果；版本在標準輸出，登入狀態有時印在標準錯誤。</summary>
internal sealed record ProbeResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Output => StandardOutput + StandardError;
}

internal sealed record ProcessResult(int ExitCode, string StandardError, bool StartFailed, bool TimedOut, bool Cancelled)
{
    public static readonly ProcessResult CouldNotStart = new(-1, string.Empty, true, false, false);
}

/// <summary>
/// 跑一個子程序：提示從標準輸入送、標準輸出一行一行交給呼叫端、逾時或取消時結束整個程序樹。
/// 輸出的內容（可能是對話）只交給轉換器，這裡不存、不記錄；標準錯誤只留尾端一小段，給失敗的原因用。
/// </summary>
internal sealed class ProcessRunner(AgentOptions options, Func<string, string?> findCommand)
{
    public ProcessRunner(AgentOptions options)
        : this(options, SystemCommands.Find)
    {
    }

    private const int MaxStandardErrorCharacters = 32 * 1024;

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request, Func<string, LineVerdict> onLine, TimeSpan timeout, CancellationToken cancellation)
    {
        if (Start(request) is not { } process)
        {
            return ProcessResult.CouldNotStart;
        }

        using (process)
        using (var doneSource = new CancellationTokenSource())
        using (var timeoutSource = new CancellationTokenSource(timeout))
        using (var guard = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeoutSource.Token))
        {
            var standardError = new StringBuilder();
            var descendants = new DescendantTracker();
            var readers = Task.WhenAll(
                ReadOutputAsync(process, onLine, descendants, doneSource.Token),
                ReadErrorAsync(process, standardError),
                WriteInputAsync(process, request.StandardInput));
            var (timedOut, cancelled) = (false, false);
            try
            {
                await process.WaitForExitAsync(guard.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = timeoutSource.IsCancellationRequested && !cancellation.IsCancellationRequested;
                cancelled = !timedOut;
                await StopAsync(process);
            }

            // 孫程序可能還抓著管線不放，不能為了讀完輸出一直等下去。
            await Task.WhenAny(readers, options.Delay(options.DrainTimeout, CancellationToken.None));
            await doneSource.CancelAsync();
            KillTree(process);
            // 引擎自己結束了，它留下的孫程序（背景的輔助程式）不能留在使用者的電腦上。
            await descendants.KillSurvivorsAsync();
            return new ProcessResult(process.HasExited ? process.ExitCode : -1, standardError.ToString(), false, timedOut, cancelled);
        }
    }

    /// <summary>不送提示的小指令（查版本、登入狀態）。輸出全部收起來；起不來回 null。</summary>
    public async Task<ProbeResult?> ProbeAsync(ProcessRequest request, CancellationToken cancellation)
    {
        var output = new StringBuilder();
        var result = await RunAsync(request, line =>
        {
            output.AppendLine(line);
            return LineVerdict.Continue;
        }, options.ProbeTimeout, cancellation);
        return result.StartFailed || result.TimedOut ? null : new ProbeResult(result.ExitCode, output.ToString(), result.StandardError);
    }

    private static Process? Start(ProcessRequest request)
    {
        var info = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        EngineEnvironment.Apply(info.Environment, request.SetVariables);

        try
        {
            return SystemProcess.Start(info);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private async Task ReadOutputAsync(Process process, Func<string, LineVerdict> onLine, DescendantTracker descendants, CancellationToken done)
    {
        try
        {
            var first = true;
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (first)
                {
                    // 第一行出來時它的輔助程式多半已經啟動：先記一次，引擎結束得太快時才來得及知道要清誰。
                    first = false;
                    descendants.Snapshot(process.Id);
                }

                switch (onLine(line))
                {
                    case LineVerdict.Abort:
                        KillTree(process);
                        return;
                    case LineVerdict.Done:
                        // 結果出來之後引擎通常就要結束：趁它還活著，記下它底下有哪些程序，結束後才知道要清誰。
                        descendants.Snapshot(process.Id);
                        ScheduleKill(process, done);
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 程序被結束、管線關掉：正常的收尾。
        }
    }

    private void ScheduleKill(Process process, CancellationToken done) =>
        _ = options.Delay(options.ExitGrace, done).ContinueWith(
            task =>
            {
                if (!task.IsCanceled)
                {
                    KillTree(process);
                }
            },
            TaskScheduler.Default);

    private static async Task ReadErrorAsync(Process process, StringBuilder buffer)
    {
        try
        {
            var chunk = new char[2048];
            int read;
            while ((read = await process.StandardError.ReadAsync(chunk)) > 0)
            {
                buffer.Append(chunk, 0, read);
                // 只留尾端：Codex 啟動時會印很長的記錄，原因在最後。
                if (buffer.Length > MaxStandardErrorCharacters)
                {
                    buffer.Remove(0, buffer.Length - MaxStandardErrorCharacters);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 同上。
        }
    }

    private static async Task WriteInputAsync(Process process, string text)
    {
        try
        {
            await process.StandardInput.WriteAsync(text);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 引擎沒讀完就結束了（例如啟動失敗）：原因看它的結束代碼與標準錯誤。
        }
        finally
        {
            // 關掉標準輸入是必要的：Codex 的 exec 在標準輸入開著時會一直等。
            TryCloseInput(process);
        }
    }

    private static void TryCloseInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 已經關了。
        }
    }

    /// <summary>先送中斷（Unix），給引擎一點時間收尾；還沒結束就強制結束整個程序樹。Windows 沒有可用的中斷，直接強制結束。</summary>
    private async Task StopAsync(Process process)
    {
        if (!OperatingSystem.IsWindows() && !process.HasExited)
        {
            // 沒送出中斷（極簡環境找不到 kill）就不必白等寬限，直接強制結束。
            if (await SendInterruptAsync(process.Id))
            {
                using var grace = new CancellationTokenSource(options.InterruptGrace);
                try
                {
                    await process.WaitForExitAsync(grace.Token);
                }
                catch (OperationCanceledException)
                {
                    // 沒有在時間內結束，下面強制結束。
                }
            }
        }

        KillTree(process);
        using var final = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(final.Token);
        }
        catch (OperationCanceledException)
        {
            // 連強制結束都沒有結束：放棄，不能卡住整個代理。
        }
    }

    /// <summary>送出了中斷回 true；找不到 kill 或送不出去回 false，呼叫端不必等寬限。</summary>
    private async Task<bool> SendInterruptAsync(int processId)
    {
        if (findCommand("kill") is not { } killPath)
        {
            return false;
        }

        var info = new ProcessStartInfo(killPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-INT");
        info.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            using var kill = SystemProcess.Start(info);
            if (kill is null)
            {
                return false;
            }

            await kill.WaitForExitAsync();
            return kill.ExitCode == 0;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            // 送不出中斷就直接強制結束。
            return false;
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 已經結束了。
        }
    }
}
