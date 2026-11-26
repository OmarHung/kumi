using PmTool.Cli.Agent;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Uninstall;

/// <summary>這台電腦上 kumi 自己的設定與記錄。只刪認得出是 kumi 的東西。</summary>
internal static class LocalFileSteps
{
    // 本機代理的工作資料夾只在一件工作的期間（最多幾分鐘）用到。設定資料夾裡的鎖看不到用別的設定檔跑的代理，
    // 所以剛建的不刪，免得刪到正在進行的工作；比這個舊的都是殘留。
    private static readonly TimeSpan WorkDirectoryMinimumAge = TimeSpan.FromMinutes(15);

    public static async Task DeleteFilesAsync(UninstallSession session)
    {
        const string name = UninstallSession.Files;
        await session.HeadingAsync("設定與記錄");
        if (session.IsSkipped(name))
        {
            await session.RecordAsync(new(name, UninstallStatus.Skipped, ["略過（--skip files）。"]));
            return;
        }

        // 先看鎖再列要刪的：看鎖時會建出鎖檔，列在後面它才會一起被刪掉。
        if (RunningAgent(session.Context.ConfigPath) is { } running)
        {
            await session.RecordAsync(new(name, UninstallStatus.Failed, [running]));
            return;
        }

        var targets = Targets(session.Context);
        var tempRoot = session.Context.Autostart.TempDirectory;
        var hasWork = HasLeftoverWork(tempRoot);
        if (targets.Count == 0 && !hasWork)
        {
            await session.RecordAsync(new(name, UninstallStatus.NotFound, ["沒有 kumi 的設定或記錄。"]));
            return;
        }

        await ListAsync(session, targets, hasWork ? tempRoot : null);
        if (!await session.ConfirmAsync("刪掉這些設定與記錄？", defaultYes: !session.TokensOutstanding))
        {
            await session.RecordAsync(new(name, UninstallStatus.Declined, ["沒有刪設定與記錄。"]));
            return;
        }

        await session.RecordAsync(Delete(targets, tempRoot));
    }

    /// <summary>
    /// 本機代理開著設定資料夾裡的鎖、用著暫存的工作資料夾：刪到一半它會在別處重建，留下更難清的殘骸。
    /// 正在執行時回要給使用者看的說明，沒有在執行回 null。
    /// </summary>
    private static string? RunningAgent(string configPath)
    {
        try
        {
            return AgentLock.Peek(configPath) is { } holder
                ? $"本機代理正在執行{(holder.Pid is { } pid ? $"（程序 {pid}）" : string.Empty)}：先結束它（前景的按 Ctrl-C，背景的執行 kumi agent disable），再執行一次 kumi uninstall。"
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"看不了本機代理有沒有在執行（{exception.Message}），所以沒有刪設定與記錄。";
        }
    }

    private static async Task ListAsync(UninstallSession session, IReadOnlyList<string> targets, string? workRoot)
    {
        await session.Progress.WriteLineAsync("  會刪掉：");
        foreach (var target in targets)
        {
            await session.Progress.WriteLineAsync($"    {target}");
        }

        if (workRoot is not null)
        {
            await session.Progress.WriteLineAsync($"    {Path.Combine(workRoot, WorkDirectories.Prefix + "*")}（本機代理留下的暫存工作資料夾，15 分鐘內用過的不刪）");
        }

        if (session.TokensOutstanding)
        {
            await session.Progress.WriteLineAsync("  注意：設定檔裡的權杖還沒在伺服器上撤銷，刪掉之後只能到網頁「設定 → 命令列與整合」的「存取權杖」撤銷。");
        }
    }

    private static UninstallStep Delete(IReadOnlyList<string> targets, string tempRoot)
    {
        List<string> failures = [];
        List<string> notes = [];
        foreach (var target in targets)
        {
            if (LinkTarget(target) is { } pointsTo)
            {
                notes.Add($"{target} 是連結：只刪了連結，它指到的 {pointsTo} 沒有動，要的話請自己刪。");
            }

            if (TryDelete(target) is { } failure)
            {
                failures.Add(failure);
            }
        }

        WorkDirectories.SweepStale(tempRoot, DateTime.UtcNow, WorkDirectoryMinimumAge);
        if (HasLeftoverWork(tempRoot))
        {
            // SweepStale 只刪看得出是 kumi 建的（不是連結、權限 700）；剩下的不替使用者決定。
            failures.Add($"有暫存工作資料夾沒有刪（權限不是 700、是連結或被佔用）：{Path.Combine(tempRoot, WorkDirectories.Prefix + "*")}，確認之後請自己刪。");
        }

        return failures.Count == 0
            ? new(UninstallSession.Files, UninstallStatus.Done, [.. notes, "已刪掉設定與記錄。"])
            : new(UninstallSession.Files, UninstallStatus.Failed, [.. notes, .. failures]);
    }

    /// <summary>
    /// 要刪的東西。設定資料夾叫 kumi 或 pm-tool（kumi 自己建的）才整個刪，連同旁邊另一個名字的；
    /// 設定檔被指到別的資料夾時只刪 kumi 自己的那幾個檔案，不刪資料夾裡別人的東西。
    /// </summary>
    private static IReadOnlyList<string> Targets(CliContext context)
    {
        var configPath = Path.GetFullPath(context.ConfigPath);
        var directory = Path.GetDirectoryName(configPath)!;
        List<string> targets = [];
        if (ConfigPermissions.OwnedDirectoryNames.Contains(Path.GetFileName(directory), StringComparer.Ordinal))
        {
            var parent = Path.GetDirectoryName(directory)!;
            targets.AddRange(ConfigPermissions.OwnedDirectoryNames
                .Select(owned => Path.Combine(parent, owned))
                .Where(Directory.Exists));
        }
        else
        {
            targets.AddRange(ConfigPermissions.SiblingFiles.Prepend(Path.GetFileName(configPath))
                .Select(file => Path.Combine(directory, file))
                .Where(File.Exists));
        }

        if (context.Autostart.LogDirectory is { } logs && Directory.Exists(logs))
        {
            targets.Add(logs);
        }

        return targets;
    }

    private static bool HasLeftoverWork(string root)
    {
        try
        {
            var cutoff = DateTime.UtcNow - WorkDirectoryMinimumAge;
            return Directory.Exists(root) && Directory.EnumerateDirectories(root, WorkDirectories.Prefix + "*")
                .Any(path => Directory.GetLastWriteTimeUtc(path) <= cutoff);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? LinkTarget(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>刪掉檔案或資料夾；刪不掉回原因。資料夾是連結時只刪連結，不跟進去刪它指到的地方。</summary>
    internal static string? TryDelete(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (info.Exists && info.LinkTarget is null)
            {
                info.Delete(recursive: true);
            }
            else if (info.Exists)
            {
                Directory.Delete(path);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"{path} 刪不掉：{exception.Message}";
        }
    }
}
