namespace PmTool.Cli.Agent.Autostart;

/// <summary>
/// 開機自動啟動每次登入都會執行啟動項目指到的檔案。檔案放在別的程式或別的使用者換得掉的地方（下載資料夾、暫存資料夾、
/// 大家都寫得進去的資料夾），等於讓那些人在你每次登入時執行他們的程式、而且帶著你的 AI 訂閱。這裡只擋這幾種位置。
/// 先解析符號連結再判斷（連結指到下載資料夾一樣算）；macOS 的檔案系統預設不分大小寫，比對也不分。
/// Windows 的資料夾權限（ACL）這裡不查，維持原樣：Windows 的開機自動啟動本來就標示為實驗性，靠位置的檢查（下載、暫存）擋最常見的情況。
/// </summary>
internal static class AutostartPathPolicy
{
    private const int MaxLinkDepth = 8;

    private static readonly string[] DownloadFolderNames = ["Downloads", "下載"];

    private static readonly string[] UnixTempRoots = ["/tmp", "/private/tmp", "/var/tmp", "/private/var/tmp", "/var/folders", "/private/var/folders", "/dev/shm"];

    /// <summary>可以用回傳 null；不行時回「為什麼、怎麼辦」的說明。</summary>
    public static string? Check(string kumiPath, AutostartEnvironment environment)
    {
        var resolved = ResolveLinks(Path.GetFullPath(kumiPath));
        var directory = Path.GetDirectoryName(resolved);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var place = Describe(directory, environment)
            ?? (environment.IsWritableByOthers(resolved) ? "別的使用者也寫得進去的執行檔" : null);
        if (place is null)
        {
            return null;
        }

        var safe = environment.Os == AutostartOs.Windows ? @"%LOCALAPPDATA%\Programs\Kumi" : "~/.local/bin";
        var where = place.EndsWith("執行檔", StringComparison.Ordinal) ? resolved : directory;
        return $"kumi 的執行檔在 {where}，這是{place}。開機自動啟動每次登入都會執行這個檔案，放在這種地方，別的程式換掉它就會跟著被執行"
            + $"（而且是在背景、用你的 AI 訂閱）。請把 kumi 搬到只有你能寫的資料夾（例如 {safe}），從新的位置再執行一次 kumi agent enable。";
    }

    private static string? Describe(string directory, AutostartEnvironment environment)
    {
        // 下載資料夾先看：它比暫存資料夾具體（家目錄本身在暫存資料夾底下的環境，也要說對是哪一種）。
        if (IsInsideDownloads(directory, environment.HomeDirectory, IgnoresCase(environment)))
        {
            return "下載資料夾";
        }

        var inUnixTemp = environment.Os != AutostartOs.Windows && UnixTempRoots.Any(root => IsInside(directory, root, IgnoresCase(environment)));
        if (IsInside(directory, environment.TempDirectory, IgnoresCase(environment)) || inUnixTemp)
        {
            return "暫存資料夾";
        }

        return environment.IsWritableByOthers(directory) ? "別的使用者也寫得進去的資料夾" : null;
    }

    private static bool IsInsideDownloads(string directory, string home, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(home))
        {
            return false;
        }

        return DownloadFolderNames.Any(name => IsInside(directory, Path.Combine(home, name), ignoreCase));
    }

    private static bool IgnoresCase(AutostartEnvironment environment) => environment.Os != AutostartOs.Other;

    /// <summary>路徑裡每一層若是符號連結，換成它指到的真正位置；解析不了的照原樣。</summary>
    internal static string ResolveLinks(string fullPath, int depth = 0)
    {
        if (depth > MaxLinkDepth)
        {
            return fullPath;
        }

        try
        {
            var root = Path.GetPathRoot(fullPath) ?? string.Empty;
            var current = root;
            foreach (var part in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    // 連結指到的位置自己的上層也可能是連結（macOS 的 /var），再解析一次。
                    current = ResolveLinks(target.FullName, depth + 1);
                }
            }

            return current;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return fullPath;
        }
    }

    private static bool IsInside(string directory, string root, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(ResolveLinks(Path.GetFullPath(root)));
        var normalized = Path.TrimEndingDirectorySeparator(directory);
        return normalized.Equals(normalizedRoot, comparison)
            || normalized.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
}
