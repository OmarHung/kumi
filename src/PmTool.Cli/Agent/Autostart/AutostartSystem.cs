using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PmTool.Cli.Agent.Autostart;

/// <summary>
/// <see cref="AutostartEnvironment"/> 的真實作：跑系統指令、背景啟動、結束程序、登錄、藏主控台。
/// 測試不會進到這裡（測試的環境是假的），所以每個函式都盡量小，也只做一件事。
/// </summary>
internal static class AutostartSystem
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const int HideWindow = 0;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public static async Task<string> GetUidAsync(CancellationToken cancellation)
    {
        var result = await RunAsync("id", ["-u"], cancellation);
        var uid = result.Output.Trim();
        // 這個值會放進 launchctl 的網域，只收純數字。
        return result.ExitCode == 0 && uid.Length is > 0 and <= 10 && uid.All(char.IsAsciiDigit)
            ? uid
            : throw new CliException(ExitCodes.Error, "查不到這個使用者的編號（id -u 失敗）。");
    }

    public static async Task<CommandOutcome> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info) ?? throw new CliException(ExitCodes.Error, $"跑不起來：{file}");
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(CommandTimeout);
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new CommandOutcome(process.ExitCode, await output + await error);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            cancellation.ThrowIfCancellationRequested();
            return new CommandOutcome(127, $"{file} 跑不起來或逾時。");
        }
    }

    /// <summary>不經 shell、不繼承這個視窗的程序：關掉視窗它還在。</summary>
    public static Task StartDetachedAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info);
        return Task.CompletedTask;
    }

    public static string? ProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public static async Task<bool> KillAsync(int pid, CancellationToken cancellation)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // 程序已經不在了，或收不掉：呼叫端會再看一次鎖。
            return ProcessName(pid) is null;
        }
    }

    public static string? ReadRunValue(string name) =>
        OperatingSystem.IsWindows() ? WindowsRun.Read(name) : null;

    public static void WriteRunValue(string name, string commandLine)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsRun.Write(name, commandLine);
        }
    }

    public static bool DeleteRunValue(string name) =>
        OperatingSystem.IsWindows() && WindowsRun.Delete(name);

    public static void HideConsole()
    {
        if (OperatingSystem.IsWindows() && GetConsoleWindow() is var window && window != 0)
        {
            ShowWindow(window, HideWindow);
        }
    }

    [SupportedOSPlatform("windows")]
    private static class WindowsRun
    {
        public static string? Read(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(name) as string;
        }

        public static void Write(string name, string commandLine)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(name, commandLine, RegistryValueKind.String);
        }

        public static bool Delete(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(name) is null)
            {
                return false;
            }

            key.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
    }

    // 用 DllImport 而不是 LibraryImport：後者的產生器要專案開 AllowUnsafeBlocks。兩個函式的參數與回傳都是整數，不需要額外的封送。
    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern int ShowWindow(nint window, int command);
}
