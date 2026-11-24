using System.ComponentModel;
using System.Diagnostics;

namespace PmTool.Cli.Login;

/// <summary>
/// 瀏覽器登入與設定精靈碰得到的外部環境：打開瀏覽器、等多久、這台電腦的名稱、這個作業系統能不能設開機自動啟動。
/// 測試換成假的（不真的開瀏覽器）。合約（internal/HANDOFF-setup-wizard.md 第 4.1 節）：需要的外部屬性加在這個檔案，<see cref="CliContext"/> 不必再改。
/// </summary>
internal sealed record LoginEnvironment
{
    /// <summary>打開瀏覽器到這個網址；打不開回 false（呼叫端照樣印出網址讓人自己貼）。</summary>
    public required Func<string, bool> OpenBrowser { get; init; }

    /// <summary>開了瀏覽器之後最多等多久。</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>報給伺服器的電腦名稱（預設是主機名稱，和本機代理的預設名稱同一個）。</summary>
    public required Func<string> MachineName { get; init; }

    /// <summary>這個作業系統有沒有開機自動啟動可以設（macOS、Windows）；精靈只在有的時候問。</summary>
    public bool AutostartSupported { get; init; } = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    /// <summary>
    /// 精靈呼叫別的 kumi 指令（kumi agent enable、kumi agent start）的方式。測試換成假的：不能真的替開發電腦裝上開機自動啟動，也不能真的開著長駐的代理。
    /// </summary>
    public Func<IReadOnlyList<string>, CliContext, CancellationToken, Task<int>> RunCommand { get; init; } =
        static (arguments, context, cancellation) => CliRunner.RunAsync(arguments, context, cancellation);

    /// <summary>真的環境：macOS 用 open、Windows 用 ShellExecute、其他用 xdg-open。</summary>
    public static LoginEnvironment Current() => new()
    {
        OpenBrowser = TryOpenBrowser,
        MachineName = static () => Environment.MachineName,
    };

    private static bool TryOpenBrowser(string url)
    {
        // 只開我們自己組出來的 http／https 網址：UseShellExecute 會把字串交給系統去「開」，不能是任意的檔案或指令。
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(StartInfoFor(uri.AbsoluteUri));
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static ProcessStartInfo StartInfoFor(string url)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(url) { UseShellExecute = true };
        }

        var info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // xdg-open 找不到瀏覽器時會在終端機上印一堆話，蓋掉我們要印的網址。
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(url);
        return info;
    }
}
