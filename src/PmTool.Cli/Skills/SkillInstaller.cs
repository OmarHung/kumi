using System.Security.Cryptography;
using PmTool.Cli.Commands;

namespace PmTool.Cli.Skills;

internal enum SkillState
{
    Missing,

    /// <summary>有標記、版本和這份 kumi 一樣、檔案沒改過。</summary>
    Current,

    /// <summary>有標記、版本比這份 kumi 舊、檔案沒改過。</summary>
    Outdated,

    /// <summary>有同名資料夾但沒有標記：使用者自己放的。</summary>
    Foreign,

    /// <summary>有標記，但資料夾裡的檔案和標記記的不一樣（使用者改過）。</summary>
    Modified,

    /// <summary>有標記，版本比這份 kumi 內含的新：不降級。</summary>
    Newer,
}

/// <summary>一個 skill 資料夾的檢查與換裝。換裝先寫到旁邊的暫存資料夾再改名，換到一半失敗不會留下半套。</summary>
internal static class SkillInstaller
{
    private const string BackupPrefix = ".kumi-skill-old-";
    private const string IgnoredFile = ".DS_Store";

    public static SkillState Inspect(string directory, string version)
    {
        if (!Directory.Exists(directory))
        {
            return SkillState.Missing;
        }

        if (SkillMarker.Read(directory) is not { } marker)
        {
            return SkillState.Foreign;
        }

        if (UpdateCommand.CompareLenient(marker.Version, version) > 0)
        {
            return SkillState.Newer;
        }

        if (IsModified(directory, marker))
        {
            return SkillState.Modified;
        }

        return marker.Version == version ? SkillState.Current : SkillState.Outdated;
    }

    /// <summary>資料夾裡的檔案和標記記的雜湊不一樣（改過、多了、少了）。舊格式的標記沒有雜湊，不算改過。</summary>
    public static bool IsModified(string directory, SkillMarker marker)
    {
        if (marker.Files is null)
        {
            return false;
        }

        try
        {
            var actual = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => (Relative: Path.GetRelativePath(directory, path).Replace('\\', '/'), Path: path))
                .Where(file => file.Relative != SkillMarker.FileName && Path.GetFileName(file.Path) != IgnoredFile)
                .ToDictionary(file => file.Relative, file => HashOf(File.ReadAllBytes(file.Path)), StringComparer.Ordinal);
            return actual.Count != marker.Files.Count
                || marker.Files.Any(pair => !actual.TryGetValue(pair.Key, out var hash) || hash != pair.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 讀不了就當成改過：寧可不覆蓋。
            return true;
        }
    }

    /// <summary>
    /// 舊名稱的資料夾：SKILL.md 開頭的 front matter（第一行 --- 到下一個 --- 之間）裡的 name 就是資料夾名稱
    /// （pm-tool、pm-tool-planning）。正文裡寫的 name: 不算；名稱對不上的是使用者自己的東西。
    /// </summary>
    public static bool IsLegacy(string directory, string name)
    {
        var path = Path.Combine(directory, "SKILL.md");
        try
        {
            return File.Exists(path) && FrontMatterName(File.ReadLines(path)) == name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string? FrontMatterName(IEnumerable<string> lines)
    {
        var first = true;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (first)
            {
                if (line != "---")
                {
                    return null;
                }

                first = false;
                continue;
            }

            if (line == "---")
            {
                return null;
            }

            if (line.StartsWith("name:", StringComparison.Ordinal))
            {
                return line["name:".Length..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// 上次換版在兩次改名之間被中斷（或還原失敗）時，目標不見了、旁邊留著 <c>.kumi-skill-old-&lt;名稱&gt;-*</c>：把最新的那份放回原位。
    /// 回傳有沒有救回來。
    /// </summary>
    public static bool Recover(string destination)
    {
        if (Directory.Exists(destination) || Path.GetDirectoryName(destination) is not { } parent || !Directory.Exists(parent))
        {
            return false;
        }

        var backup = Directory.EnumerateDirectories(parent, $"{BackupPrefix}{Path.GetFileName(destination)}-*")
            .OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
        if (backup is null)
        {
            return false;
        }

        try
        {
            Directory.Move(backup, destination);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Install(BundledSkill skill, string destination, string version, DateTimeOffset now)
    {
        var parent = Path.GetDirectoryName(destination)!;
        var staging = Path.Combine(parent, $".kumi-skill-new-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $"{BackupPrefix}{skill.Name}-{Guid.NewGuid():N}");
        var keepBackup = false;
        try
        {
            Directory.CreateDirectory(staging);
            Write(skill, staging, new SkillMarker(skill.Name, version, now, HashesOf(skill)));
            if (!Directory.Exists(destination))
            {
                Directory.Move(staging, destination);
                return;
            }

            Directory.Move(destination, backup);
            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                // 新的換不進去：把舊的放回原位；放不回去時舊的在備份資料夾裡，不能刪。
                try
                {
                    Directory.Move(backup, destination);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    keepBackup = true;
                    throw new CliException(ExitCodes.Error,
                        $"換不上新版，原本的 {skill.Name} 也放不回去（{restore.GetType().Name}）。原本的內容在 {backup}，請手動改名回 {destination}。");
                }

                throw;
            }
        }
        finally
        {
            DeleteQuietly(staging);
            if (!keepBackup)
            {
                DeleteQuietly(backup);
            }
        }
    }

    public static void Remove(string directory) => Directory.Delete(directory, recursive: true);

    private static Dictionary<string, string> HashesOf(BundledSkill skill) =>
        skill.Files.ToDictionary(file => file.RelativePath, file => HashOf(file.Content), StringComparer.Ordinal);

    private static string HashOf(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static void Write(BundledSkill skill, string directory, SkillMarker marker)
    {
        foreach (var file in skill.Files)
        {
            var path = Path.GetFullPath(Path.Combine(directory, file.RelativePath));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new CliException(ExitCodes.Error, $"內嵌的 skill 檔案路徑不對：{file.RelativePath}。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.Content);
        }

        File.WriteAllText(Path.Combine(directory, SkillMarker.FileName), marker.ToJson());
    }

    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 刪不掉的暫存資料夾不蓋過真正的結果；名稱帶 .kumi-skill- 前綴，不會被當成 skill。
        }
    }
}
