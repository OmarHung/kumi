namespace PmTool.Api.Hosting;

/// <summary>
/// 沒接住的例外（例如啟動前的遷移失敗）讓程序用結束代碼離開，而不是交給執行環境 abort()。
/// 容器裡的程序通常是 PID 1：核心不會替 PID 1 執行 SIGABRT 的預設動作，abort() 結束不了程序，
/// glibc 就在原地空轉（CPU 100%、一直不結束），平台看它還活著，restart: unless-stopped 與 Cloud Run 都不會重啟它。
/// </summary>
internal static class CrashExit
{
    public const int ExitCode = 1;

    private static int _installed;

    /// <summary>
    /// 只裝一次。測試裡的 WebApplicationFactory 每次都會跑一遍 Program，但它自己接住入口的例外，
    /// 這個處理器在測試裡不會被叫到。
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // 自己寫出來：在這裡結束程序，執行環境就不會再印「Unhandled exception.」。
        // 開頭用同樣的字，scripts/ops 的 status.sh 與 errors.sh 靠它數這種錯誤。
        Console.Error.WriteLine($"Unhandled exception. {e.ExceptionObject}");
        Console.Error.Flush();
        Environment.Exit(ExitCode);
    }
}
