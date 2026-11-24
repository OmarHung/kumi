using System.Text.Json;
using System.Text.RegularExpressions;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Agent;

/// <summary>
/// 這台電腦的編號：第一次產生、存在設定資料夾的 agent.json（和 config.json 同一個資料夾，Unix 上權限 600），之後都用同一個，
/// 伺服器才認得出是同一台電腦。編號不是機密，但和權杖放在同一個地方，用同樣的寫法。
/// </summary>
internal static partial class MachineIdentity
{
    public const string FileName = "agent.json";
    private const int MaxNameLength = 60;
    private const string FallbackName = "kumi-computer";

    public static string LoadOrCreateKey(string configPath)
    {
        var path = PathOf(configPath);
        if (ReadKey(path) is { } existing)
        {
            return existing;
        }

        var key = Guid.NewGuid().ToString("N");
        ConfigStore.WriteAtomically(path, JsonSerializer.Serialize(new { machineKey = key }));
        return key;
    }

    /// <summary>--name，沒有就用電腦的主機名稱；拿掉控制字元、截到伺服器收的長度。</summary>
    public static string ResolveName(string? requested)
    {
        var candidate = string.IsNullOrWhiteSpace(requested) ? Environment.MachineName : requested;
        var clean = new string([.. candidate.Where(character => !char.IsControl(character))]).Trim();
        if (clean.Length > MaxNameLength)
        {
            clean = clean[..MaxNameLength].TrimEnd();
        }

        return clean.Length == 0 ? FallbackName : clean;
    }

    internal static string PathOf(string configPath) => Path.Combine(Path.GetDirectoryName(configPath)!, FileName);

    private static string? ReadKey(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var key = document.RootElement.TryGetProperty("machineKey", out var value) ? value.GetString() : null;
            return key is not null && KeyPattern().IsMatch(key) ? key : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            // 沒有或壞掉：重新產生（伺服器會當成新的一台電腦）。
            return null;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9-]{8,64}\z")]
    private static partial Regex KeyPattern();
}
