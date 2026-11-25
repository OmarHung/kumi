using System.ComponentModel;
using System.Diagnostics;
using PmTool.Cli.Processes;

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

    /// <summary>
    /// 真的環境：macOS 用 open、Windows 用 rundll32.exe url.dll,FileProtocolHandler、其他用 xdg-open，都是系統資料夾裡的絕對路徑。
    /// Windows 這條路沒有在實機上測過（只驗證過組出來的程式與參數）。
    /// </summary>
    public static LoginEnvironment Current() => new()
    {
        OpenBrowser = TryOpenBrowser,
        MachineName = static () => Environment.MachineName,
    };

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            if (BrowserStartInfo(url, SystemCommands.CurrentPlatform, SystemCommands.Find) is not { } info)
            {
                return false;
            }

            using var process = SystemProcess.Start(info);
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 開網址要執行的程式與參數；網址不是 http／https 的絕對網址、或這台電腦找不到開網址的程式就回 null（呼叫端會印網址讓人自己開）。
    /// 先驗證網址再組命令：交給系統去「開」的字串不能是檔案、網路路徑或像選項的東西。
    /// Windows 不用 explorer.exe（它不是專門開網址的程式，沒有 Explorer 殼層的環境會開出檔案總管或什麼都不做），
    /// 改用 rundll32.exe 的 url.dll,FileProtocolHandler；這條路沒有在 Windows 實機上測過。
    /// </summary>
    internal static ProcessStartInfo? BrowserStartInfo(string url, SystemPlatform platform, Func<string, string?> find)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var opener = find(platform switch
        {
            SystemPlatform.Windows => "rundll32",
            SystemPlatform.MacOS => "open",
            _ => "xdg-open",
        });
        if (opener is null)
        {
            return null;
        }

        var info = new ProcessStartInfo(opener)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // xdg-open 找不到瀏覽器時會在終端機上印一堆話，蓋掉我們要印的網址。
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (platform == SystemPlatform.Windows)
        {
            info.ArgumentList.Add("url.dll,FileProtocolHandler");
        }

        info.ArgumentList.Add(uri.AbsoluteUri);
        return info;
    }
}
