using System.Text.Json;

namespace PmTool.Cli.Skills;

/// <summary>
/// 裝好的 skill 資料夾裡的標記：靠它分辨「kumi 裝的」與「使用者自己放的」（沒有標記，不覆蓋、不刪），
/// 以及「kumi 裝的、但之後被改過」：<paramref name="Files"/> 記下裝的當下每個檔案的 SHA-256（相對路徑 → 雜湊），
/// 資料夾裡現在的檔案和它不一樣（改過、多了、少了）就是改過，不覆蓋，要 <c>--force</c>。
/// 沒有 <paramref name="Files"/> 的是舊格式（0.11.0 的開發版），當成沒改過。
/// </summary>
internal sealed record SkillMarker(string Name, string Version, DateTimeOffset InstalledAt, IReadOnlyDictionary<string, string>? Files = null)
{
    public const string FileName = ".kumi-skill.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>讀資料夾裡的標記；沒有、壞掉、或裡面的名稱和資料夾名稱不一致都回 null（當作不是 kumi 裝的）。</summary>
    public static SkillMarker? Read(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var marker = JsonSerializer.Deserialize<SkillMarker>(File.ReadAllText(path), Options);
            return marker is { Name.Length: > 0, Version.Length: > 0 } && marker.Name == Path.GetFileName(directory) ? marker : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
