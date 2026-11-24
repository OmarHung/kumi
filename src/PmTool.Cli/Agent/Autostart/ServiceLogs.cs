using System.Text;
using PmTool.Cli.Commands;

namespace PmTool.Cli.Agent.Autostart;

/// <summary>
/// <c>kumi agent start --service</c> 的輸出去處。macOS 的 launchd 已經把標準輸出與錯誤接到記錄檔，這裡只加時間；
/// Windows 沒有人幫忙接：藏起主控台視窗之後，輸出自己寫進 %LOCALAPPDATA%\Kumi\agent.log。
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
        if (environment.Os != AutostartOs.Windows || environment.LogPath is not { } path)
        {
            return new ServiceLogs(new TimestampedWriter(call.Out, now), new TimestampedWriter(call.Error, now), false, null);
        }

        environment.HideConsole();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AutostartManager.TrimLog(path);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        var stamped = new TimestampedWriter(file, now);
        return new ServiceLogs(stamped, stamped, true, file);
    }

    public void Dispose() => _file?.Dispose();
}
