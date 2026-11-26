using System.Text;

namespace PmTool.Cli.Agent.Autostart;

/// <summary>Windows：<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> 的 <c>KumiLocalAgent</c>，登入時啟動。</summary>
internal sealed class WindowsRunBackend(AutostartEnvironment environment) : IAutostartBackend
{
    public const string ValueName = "KumiLocalAgent";

    public bool IsInstalled() => environment.ReadRunValue(ValueName) is not null;

    public Task InstallAsync(AutostartSpec spec, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(spec.LogPath)!);
        environment.WriteRunValue(ValueName, CommandLine(spec));
        return Task.CompletedTask;
    }

    public Task LaunchAsync(AutostartSpec spec, CancellationToken cancellation) =>
        environment.StartBackground(spec.KumiPath, spec.Arguments, cancellation);

    // Windows 沒有可以請它收尾的管理員：停止靠呼叫端看鎖、確認程序名稱之後結束。
    public Task StopAsync(CancellationToken cancellation) => Task.CompletedTask;

    public Task<bool> RemoveAsync(CancellationToken cancellation) => Task.FromResult(environment.DeleteRunValue(ValueName));

    public Task<int?> QueryPidAsync(CancellationToken cancellation) => Task.FromResult<int?>(null);

    // 重新啟動要結束舊的再照登錄的整行命令開新的；Windows 的開機自動啟動還沒在真的 Windows 上驗證過，
    // 先不做會結束程序的自動重啟，請使用者登出再登入（啟動項目照舊會用新的執行檔）。
    public bool SupportsRestart => false;

    public Task<CommandOutcome> RestartAsync(CancellationToken cancellation) =>
        Task.FromResult(new CommandOutcome(1, "Windows 還不支援自動重新啟動。"));

    /// <summary>登錄裡的整行命令：執行檔與每個參數照 Windows 的規則加引號。</summary>
    public static string CommandLine(AutostartSpec spec) =>
        string.Join(' ', new[] { spec.KumiPath }.Concat(spec.Arguments).Select(Quote));

    /// <summary>
    /// CommandLineToArgvW 的規則：有空白、引號或是空字串才加引號；引號前面的反斜線加倍、引號本身加反斜線，
    /// 結尾的反斜線也要加倍（不然會吃掉收尾的引號）。
    /// </summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                quoted.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                quoted.Append('\\', backslashes).Append(character);
            }

            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
