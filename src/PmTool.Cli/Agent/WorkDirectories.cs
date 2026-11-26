namespace PmTool.Cli.Agent;

/// <summary>
/// 每件工作的暫存資料夾（<c>kumi-agent-*</c>）：建立、刪除、啟動時清掉上次沒刪乾淨的。
/// 裡面會有使用者的圖片與 PDF（工作的附件），所以刪不掉時不能放著不管。
/// </summary>
internal static class WorkDirectories
{
    public const string Prefix = "kumi-agent-";

    /// <summary>殘留超過這麼久的才清：比一件工作最長的時間（5 分半）長很多，不會刪到還在跑的。</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    public static DirectoryInfo Create(string? root)
    {
        if (root is null)
        {
            return Directory.CreateTempSubdirectory(Prefix);
        }

        // 和 CreateTempSubdirectory 一樣只有本人能進（Unix 的 0700）。
        var directory = Directory.CreateDirectory(Path.Combine(root, Prefix + Guid.NewGuid().ToString("N")));
        if (!OperatingSystem.IsWindows())
        {
            directory.UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }

        return directory;
    }

    /// <summary>刪掉資料夾；刪不掉（防毒軟體、剛結束的子程序還抓著檔案）就等一下再試一次，還是不行才放棄，讓下次啟動的清理接手。</summary>
    public static async Task DeleteQuietlyAsync(
        DirectoryInfo directory, TimeSpan retryDelay, Func<TimeSpan, CancellationToken, Task> delay, Action<DirectoryInfo>? delete = null)
    {
        delete ??= target => target.Delete(recursive: true);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                delete(directory);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 0)
                {
                    await delay(retryDelay, CancellationToken.None);
                }
            }
        }
    }

    /// <summary>
    /// 啟動時掃一次：刪掉 <paramref name="root"/> 底下名字是 <c>kumi-agent-*</c>、是資料夾（不是連結）、超過 <paramref name="maxAge"/> 沒動過的。
    /// Unix 上還要確認權限是我們建立時的 0700：共用的 /tmp 裡別人放的同名資料夾不碰。回刪掉幾個。
    /// </summary>
    public static int SweepStale(string root, DateTime utcNow, TimeSpan maxAge)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var removed = 0;
        try
        {
            foreach (var path in Directory.EnumerateDirectories(root, Prefix + "*", SearchOption.TopDirectoryOnly))
            {
                if (TrySweep(new DirectoryInfo(path), utcNow, maxAge))
                {
                    removed++;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 列不出來就算了，下次啟動再掃。
        }

        return removed;
    }

    public static int SweepStale() => SweepStale(Path.GetTempPath(), DateTime.UtcNow, StaleAfter);

    private static bool TrySweep(DirectoryInfo directory, DateTime utcNow, TimeSpan maxAge)
    {
        try
        {
            if (directory.LinkTarget is not null || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            if (!OperatingSystem.IsWindows() && directory.UnixFileMode != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
            {
                return false;
            }

            if (utcNow - directory.LastWriteTimeUtc < maxAge)
            {
                return false;
            }

            directory.Delete(recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
