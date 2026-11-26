using System.Runtime.Versioning;

namespace PmTool.Cli.Settings;

/// <summary>
/// 設定檔裡有權杖。檔案或資料夾是別人（群組、其他使用者）讀得到的——例如被備份工具、手動複製或 umask 太鬆的程式建出來——
/// 就收緊成只有自己：檔案 600、資料夾 700，並告訴使用者，因為在那之前權杖可能已經被別人讀過。
/// Windows 的存取控制不是這種位元：不處理。
/// </summary>
internal static class ConfigPermissions
{
    private const UnixFileMode OtherAccess =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectory = OwnerOnlyFile | UnixFileMode.UserExecute;

    // kumi 自己在設定資料夾裡建的其他檔案（鎖、電腦編號、用量、主機代理的狀態）：比照設定檔收緊，不另外警告（沒有權杖）。
    private static readonly string[] SiblingFiles = ["agent.lock", "agent.lock.json", "agent.json", "agent-usage.json", "host-agent.json"];

    // 資料夾只有是 kumi 自己的才動：設定檔不該讓我們把使用者指定的共用資料夾（例如 /tmp）改成 700。
    private static readonly string[] OwnedDirectoryNames = ["kumi", "pm-tool"];

    /// <summary>檢查並收緊；回傳要給使用者看的警告（沒有問題或沒有設定檔是空的）。</summary>
    public static IReadOnlyList<string> Repair(string configPath)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(configPath))
        {
            return [];
        }

        var warnings = new List<string>();
        try
        {
            if (TightenIfOpen(configPath, OwnerOnlyFile))
            {
                warnings.Add($"設定檔 {configPath} 原本別人讀得到，已改成只有你能讀寫（600）。裡面的權杖可能已經被看過：不放心請到網頁撤銷後重新登入。");
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(configPath));
            if (directory is not null)
            {
                TightenSiblings(directory);
            }

            if (directory is not null && OwnedDirectoryNames.Contains(Path.GetFileName(directory), StringComparer.Ordinal)
                && TightenIfOpen(directory, OwnerOnlyDirectory))
            {
                warnings.Add($"設定資料夾 {directory} 原本別人進得去，已改成只有你能進入（700）。");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"設定檔 {configPath} 的權限過寬，但改不了：{exception.Message}。請自己執行 chmod 600 這個檔案。");
        }

        return warnings;
    }

    [UnsupportedOSPlatform("windows")]
    private static void TightenSiblings(string directory)
    {
        foreach (var name in SiblingFiles)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                TightenIfOpen(path, OwnerOnlyFile);
            }
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static bool TightenIfOpen(string path, UnixFileMode target)
    {
        var mode = File.GetUnixFileMode(path);
        if ((mode & OtherAccess) == 0)
        {
            return false;
        }

        File.SetUnixFileMode(path, target);
        return true;
    }
}
