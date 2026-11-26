namespace PmTool.Cli.Skills;

internal sealed record BundledFile(string RelativePath, byte[] Content);

/// <param name="Name">skill 的名稱，同時是裝進去的資料夾名稱（SKILL.md 的 name 也是它）。</param>
internal sealed record BundledSkill(string Name, IReadOnlyList<BundledFile> Files);

/// <summary>
/// 內嵌在執行檔裡的 skill（PmTool.Cli.csproj 收 <c>skills/**</c>，資源名稱是 <c>Kumi.Skills/&lt;名稱&gt;/&lt;相對路徑&gt;</c>）。
/// 單一執行檔下載時只有一個檔案，skill 只能內嵌在裡面，所以 <c>kumi skill install</c> 裝的永遠和這份 kumi 同一版。
/// </summary>
internal static class SkillBundle
{
    private const string Prefix = "Kumi.Skills/";

    private static readonly Lazy<IReadOnlyList<BundledSkill>> Loaded = new(() => Read(typeof(SkillBundle).Assembly));

    /// <summary>舊名稱：改名前的 skill 資料夾，SKILL.md 的 name 也是這兩個。</summary>
    public static readonly IReadOnlyList<string> LegacyNames = ["pm-tool", "pm-tool-planning"];

    public static IReadOnlyList<BundledSkill> Skills => Loaded.Value;

    internal static IReadOnlyList<BundledSkill> Read(System.Reflection.Assembly assembly)
    {
        var files = new Dictionary<string, List<BundledFile>>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            // 在 Windows 上建置時 RecursiveDir 帶的是反斜線。
            var parts = resource[Prefix.Length..].Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            if (!files.TryGetValue(parts[0], out var list))
            {
                files[parts[0]] = list = [];
            }

            list.Add(new BundledFile(string.Join('/', parts.Skip(1)), buffer.ToArray()));
        }

        if (!files.ContainsKey("kumi"))
        {
            throw new CliException(ExitCodes.Error, "這份 kumi 沒有內含 skill：建置時少了 skills/ 資料夾。");
        }

        return [.. files.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new BundledSkill(pair.Key, [.. pair.Value.OrderBy(file => file.RelativePath, StringComparer.Ordinal)]))];
    }
}
