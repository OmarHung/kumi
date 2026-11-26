namespace PmTool.Cli.Agent.Autostart;

/// <summary>背景程序現在的樣子，給 status 與 enable 用。<see cref="Connection"/> 是它最近回報的連線狀態（沒有回報是 null）。</summary>
internal sealed record AgentRunState(bool Running, int? Pid, bool Background, AgentConnection? Connection = null);

/// <summary>
/// 啟動、停止背景的本機代理，與回報它的狀態。是不是在跑只看 <see cref="AgentLock"/>：
/// 啟動項目在不在、launchd 怎麼說，都可能和真的有沒有程序不一致。
/// </summary>
internal sealed class AutostartManager(AutostartEnvironment environment, IAutostartBackend backend, string configPath)
{
    private const string ProcessNamePrefix = "kumi";
    private const int LogTailBytes = 16 * 1024;
    private const long MaxLogBytes = 5 * 1024 * 1024;

    public AgentRunState State()
    {
        var holder = AgentLock.Peek(configPath);
        return holder is null
            ? new AgentRunState(false, null, false)
            : new AgentRunState(true, holder.Pid, holder.Background, holder.Connection);
    }

    /// <summary>停掉背景的程序：先請啟動管理員收尾，等不到再結束程序（確認名稱是 kumi 才動手）。回傳是否已經停了。</summary>
    public async Task<bool> StopBackgroundAsync(CancellationToken cancellation)
    {
        if (State() is not { Running: true, Background: true })
        {
            return true;
        }

        await backend.StopAsync(cancellation);
        if (await WaitUntilAsync(() => !State().Running, environment.StopTimeout, cancellation))
        {
            return true;
        }

        if (State() is { Running: true, Background: true, Pid: { } pid } && IsKumi(pid))
        {
            await environment.KillProcess(pid, cancellation);
        }

        return await WaitUntilAsync(() => !State().Running, environment.StopTimeout, cancellation);
    }

    /// <summary>啟動之後等背景程序拿到鎖；等到回 true。</summary>
    public Task<bool> WaitUntilRunningAsync(CancellationToken cancellation) =>
        WaitUntilAsync(() => State() is { Running: true, Background: true }, environment.StartTimeout, cancellation);

    public bool IsKumi(int pid) =>
        environment.GetProcessName(pid) is { } name && name.StartsWith(ProcessNamePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>記錄檔會有伺服器網址、電腦名稱與工作的錯誤訊息：資料夾只給自己讀寫。已經存在的也改成 700。</summary>
    public static void EnsurePrivateDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>記錄檔太大（超過 5 MB）就清掉重來：背景程序一直寫，不然會越長越大。</summary>
    public static void TrimLog(string logPath)
    {
        try
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > MaxLogBytes)
            {
                File.WriteAllText(logPath, string.Empty);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清不掉就留著，不擋啟動。
        }
    }

    /// <summary>記錄檔最後幾行；沒有檔案或讀不到回空的。只讀尾巴，檔案再大也不會整個讀進來。</summary>
    public static IReadOnlyList<string> LogTail(string logPath, int lines)
    {
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - LogTailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r'));
            // 從中間開始讀時，第一行可能只剩半行。
            var all = start > 0 ? text.Skip(1) : text;
            return [.. all.TakeLast(lines)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellation)
    {
        var waited = TimeSpan.Zero;
        while (true)
        {
            if (condition())
            {
                return true;
            }

            if (waited >= timeout)
            {
                return false;
            }

            await environment.Delay(environment.PollInterval, cancellation);
            waited += environment.PollInterval;
        }
    }
}
