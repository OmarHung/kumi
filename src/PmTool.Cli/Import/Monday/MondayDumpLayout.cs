using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday;

/// <summary>dump 資料夾裡的檔案放在哪裡（internal/HANDOFF-monday-import.md 第 4 節）。fetch 寫、MondayDumpReader 讀，兩邊用同一份路徑規則。</summary>
internal static class MondayDumpLayout
{
    public const string MetaFile = "meta.json";
    public const string UsersFile = "users.json";
    public const string FilesFolder = "files";
    public const string SkippedFile = "skipped.json";

    // 檔案系統的上限是 255「位元組」（中文一個字 3 位元組）；還要留 ".part" 給下載中的暫存檔。
    private const int MaxFileNameBytes = 200;
    private const int MaxExtensionBytes = 32;
    private const string FallbackFileName = "file";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string MetaPath(string dump) => Path.Combine(dump, MetaFile);

    public static string UsersPath(string dump) => Path.Combine(dump, UsersFile);

    public static string BoardPath(string dump, string boardId) => Path.Combine(dump, "boards", $"{SafeName(boardId)}.json");

    public static string SubitemBoardPath(string dump, string boardId) => Path.Combine(dump, "boards", $"{SafeName(boardId)}-subitems.json");

    public static string ItemsFolder(string dump, string boardId) => Path.Combine(dump, "items", SafeName(boardId));

    /// <summary>頁碼補零，檔名照字串排序就是抓取的順序。</summary>
    public static string ItemsPagePath(string dump, string boardId, int page) =>
        Path.Combine(ItemsFolder(dump, boardId), $"{page:D4}.json");

    public static string UpdatesPath(string dump, string itemId) => Path.Combine(dump, "updates", $"{SafeName(itemId)}.json");

    public static string FilesPath(string dump) => Path.Combine(dump, FilesFolder);

    public static string SkippedPath(string dump) => Path.Combine(FilesPath(dump), SkippedFile);

    /// <summary>下載的檔案放在「files/資產編號/檔名」：同一個檔名在不同資產裡不會撞在一起。</summary>
    public static string AssetPath(string dump, string assetId, string fileName) =>
        Path.Combine(FilesPath(dump), SafeName(assetId), SafeFileName(fileName));

    /// <summary>編號、看板編號來自 monday 或 meta.json，不能直接當路徑用：只留英數、底線與連字號。</summary>
    public static string SafeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }

    /// <summary>檔名來自使用者上傳的檔案，要擋掉路徑分隔符、控制字元與 Windows 不收的字元。</summary>
    public static string SafeFileName(string name)
    {
        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        var tail = lastSeparator < 0 ? name : name[(lastSeparator + 1)..];
        var builder = new StringBuilder(tail.Length);
        foreach (var character in tail)
        {
            builder.Append(char.IsControl(character) || "<>:\"|?*".Contains(character, StringComparison.Ordinal) ? '_' : character);
        }

        var cleaned = builder.ToString().Trim().Trim('.');
        if (cleaned.Length == 0)
        {
            return FallbackFileName;
        }

        if (Encoding.UTF8.GetByteCount(cleaned) <= MaxFileNameBytes)
        {
            return cleaned;
        }

        var extension = Path.GetExtension(cleaned);
        if (Encoding.UTF8.GetByteCount(extension) > MaxExtensionBytes)
        {
            extension = string.Empty;
        }

        var stem = cleaned[..^extension.Length];
        return TruncateToBytes(stem, MaxFileNameBytes - Encoding.UTF8.GetByteCount(extension)) + extension;
    }

    private static string TruncateToBytes(string text, int maxBytes)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    public static void WriteJson(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(WriteOptions), new UTF8Encoding(false));
    }
}
