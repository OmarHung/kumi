namespace PmTool.Cli.Processes;

internal enum SystemPlatform
{
    MacOS,
    Linux,
    Windows,
}

/// <summary>
/// 作業系統自帶的指令一律用絕對路徑：.NET 在 Unix 上解析裸名稱時會先找 kumi 所在的資料夾與目前的工作目錄，
/// 使用者常在各種專案的資料夾裡跑 kumi，資料夾裡的同名檔案不該被執行。找不到就回 null，由呼叫端走降級路徑，不退回裸名稱。
/// </summary>
internal static class SystemCommands
{
    private static readonly string[] LinuxFolders = ["/usr/bin", "/bin", "/usr/sbin", "/sbin", "/usr/local/bin"];

    private static readonly Dictionary<string, string> MacOsLocations = new(StringComparer.Ordinal)
    {
        ["open"] = "/usr/bin/open",
        ["id"] = "/usr/bin/id",
        ["launchctl"] = "/bin/launchctl",
        ["ps"] = "/bin/ps",
        ["kill"] = "/bin/kill",
    };

    public static SystemPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? SystemPlatform.Windows : OperatingSystem.IsMacOS() ? SystemPlatform.MacOS : SystemPlatform.Linux;

    /// <summary>現在這台電腦上的絕對路徑；沒有這個指令是 null。</summary>
    public static string? Find(string name) =>
        Resolve(
            CurrentPlatform,
            name,
            File.Exists,
            Environment.SystemDirectory);

    internal static string? Resolve(SystemPlatform platform, string name, Func<string, bool> exists, string systemDirectory)
    {
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\'))
        {
            return null;
        }

        switch (platform)
        {
            case SystemPlatform.MacOS:
                return MacOsLocations.GetValueOrDefault(name);
            case SystemPlatform.Windows:
                // 系統資料夾拿到空字串時（環境壞掉）不組出 \rundll32.exe 這種依目前磁碟機解析的路徑，當作沒有這個指令。
                // 不用 Path.Combine：它依目前執行的系統選分隔字元，Windows 的路徑要固定用反斜線。
                return systemDirectory.TrimEnd('\\') is { Length: > 0 } directory ? $@"{directory}\{name}.exe" : null;
            default:
                return LinuxFolders.Select(folder => $"{folder}/{name}").FirstOrDefault(exists);
        }
    }
}
