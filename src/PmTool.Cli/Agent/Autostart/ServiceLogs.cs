using System.Text;
using PmTool.Cli.Commands;

namespace PmTool.Cli.Agent.Autostart;

/// <summary>
/// <c>kumi agent start --service</c> 的輸出去處。macOS 的 launchd 已經把標準輸出與錯誤接到記錄檔，這裡只加時間；
/// Windows 沒有人幫忙接：藏起主控台視窗之後，輸出自己寫進 %LOCALAPPDATA%\Kumi\agent.log。
/// macOS 的記錄檔不存在時，代表 launchd 開不了它（資料夾被清理工具刪掉了，launchd 不會自己建資料夾），
/// 標準輸出沒有人接：照 Windows 的做法自己建資料夾、自己寫，不然連不上伺服器的原因一個字都留不下來。
/// </summary>
internal sealed class ServiceLogs : IDisposable
{
    private readonly StreamWriter? _file;

    private ServiceLogs(TextWriter output, TextWriter error, bool logsErrors, StreamWriter? file)
    {
        Out = output;
        Error = error;
        LogsErrors = logsErrors;
        _file = file;
    }

    public TextWriter Out { get; }

    public TextWriter Error { get; }

    /// <summary>標準錯誤看不到（主控台藏起來了）：失敗的原因要自己寫進記錄。</summary>
    public bool LogsErrors { get; }

    public static ServiceLogs Open(CommandCall call, Func<DateTimeOffset> now)
    {
        var environment = call.Context.Autostart;
        if (environment.LogPath is not { } path || !WritesItsOwnLog(environment.Os, path))
        {
            // macOS 的 launchd 一直往記錄檔後面寫：不只 enable 的時候，每次背景程序啟動也修剪（登入就會啟動一次）。
            if (environment.LogPath is { } launchdLog)
            {
                AutostartManager.TrimLog(launchdLog);
            }

            return new ServiceLogs(new TimestampedWriter(call.Out, now), new TimestampedWriter(call.Error, now), false, null);
        }

        if (environment.Os == AutostartOs.Windows)
        {
            environment.HideConsole();
        }

        StreamWriter file;
        try
        {
            AutostartManager.EnsurePrivateDirectory(Path.GetDirectoryName(path)!);
            AutostartManager.TrimLog(path);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 記錄檔開不了（資料夾不是自己的、位置被資料夾佔住）不能讓本機代理跟著起不來：退回只寫標準輸出。
            return new ServiceLogs(new TimestampedWriter(call.Out, now), new TimestampedWriter(call.Error, now), false, null);
        }

        var stamped = new TimestampedWriter(file, now);
        return new ServiceLogs(stamped, stamped, true, file);
    }

    private static bool WritesItsOwnLog(AutostartOs os, string path) => os switch
    {
        AutostartOs.Windows => true,
        AutostartOs.MacOS => !File.Exists(path),
        _ => false,
    };

    public void Dispose() => _file?.Dispose();
}
