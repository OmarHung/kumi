namespace PmTool.Api.Composition;

/// <summary>
/// <c>dotnet PmTool.Api.dll migrate</c>：遷移資料表、授權給執行的角色、建立第一個擁有者，做完就結束。
/// 雲端一次起好幾個容器時，各自在啟動時遷移會撞在一起；所以服務設 Database:MigrateOnStartup=false，
/// 部署時先用同一個映像檔跑一次這個命令（例如 Cloud Run 的 Job），成功了才更新服務。
/// </summary>
internal static partial class MigrateCommand
{
    public const string Name = "migrate";

    public const string Usage = "dotnet PmTool.Api.dll " + Name;

    public const int SuccessExitCode = 0;

    public const int FailureExitCode = 1;

    /// <summary>只認第一個參數，後面的照常交給主機（例如 --environment）。</summary>
    public static bool IsRequested(IReadOnlyList<string> args) => args.Count > 0 && args[0] == Name;

    /// <summary>交給 <see cref="WebApplication.CreateBuilder(string[])"/> 的參數：拿掉命令本身。</summary>
    public static string[] HostArguments(string[] args) => IsRequested(args) ? args[1..] : args;

    /// <summary>
    /// 執行並回傳結束代碼。不啟動主機，所以不開連接埠、不跑背景服務；做完釋放應用程式（和 Run 一樣），
    /// 記錄才寫得完。失敗時把例外記成 Error 再回傳 <see cref="FailureExitCode"/>，部署工具看結束代碼就知道要不要往下做。
    /// </summary>
    public static async Task<int> RunAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrateCommand).FullName!);
        try
        {
            LogStarted(logger);
            await app.Services.PrepareDatabaseAsync(app.Configuration, StartupTasks.CreateLogger(app.Services), CancellationToken.None);
            LogSucceeded(logger);
            return SuccessExitCode;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            return FailureExitCode;
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "開始遷移資料表、授權與建立第一個擁有者。")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "遷移完成，可以更新服務了。")]
    private static partial void LogSucceeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "遷移失敗，不要更新服務。")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
