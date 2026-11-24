namespace PmTool.Cli.Import.Monday;

/// <summary>
/// 寫對應檔時附加的說明（plan 的草稿用）：鍵是欄位的路徑（<see cref="Key"/>），Trailing 接在那一行後面，
/// Above 寫在那一行的上面。說明來自 monday 的名稱，寫進 YAML 前會把換行等字元換成空白，免得跳出註解、變成設定。
/// </summary>
internal sealed class MappingAnnotations
{
    /// <summary>Unicode 的行分隔與段落分隔：YAML 把它們當換行。</summary>
    internal const char LineSeparator = (char)0x2028;

    internal const char ParagraphSeparator = (char)0x2029;

    public List<string> Header { get; } = [];

    public Dictionary<string, string> Trailing { get; } = [];

    public Dictionary<string, List<string>> Above { get; } = [];

    public static string Key(params string[] parts) => string.Join('/', parts);

    public MappingAnnotations Note(string key, string comment)
    {
        Trailing[key] = comment;
        return this;
    }

    public MappingAnnotations Before(string key, string line)
    {
        if (!Above.TryGetValue(key, out var lines))
        {
            Above[key] = lines = [];
        }

        lines.Add(line);
        return this;
    }

    /// <summary>YAML 的註解到換行為止；monday 的名稱可以含任何字元，換行（含 Unicode 的行分隔）一律換成空白（行首的空白留著，註解裡的縮排有意義）。</summary>
    public static string Clean(string text) =>
        string.Concat(text.Select(character =>
            char.IsControl(character) || character is LineSeparator or ParagraphSeparator ? ' ' : character)).TrimEnd();
}
