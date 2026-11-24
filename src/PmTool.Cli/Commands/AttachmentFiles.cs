using System.Globalization;

namespace PmTool.Cli.Commands;

/// <summary>附件的檔案處理：上傳前的檢查、類型的猜測，以及下載時檔名的整理。</summary>
internal static class AttachmentFiles
{
    /// <summary>和伺服器的上限一樣（AttachmentRules.MaxBytes）：先在本機擋，不必把 25 MiB 傳過去才被拒絕。</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    private const string FallbackContentType = "application/octet-stream";

    // 只列網頁會直接顯示的圖片：伺服器只拿類型決定要不要在瀏覽器裡直接顯示，其他一律當下載。
    private static readonly IReadOnlyDictionary<string, string> ImageTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
    };

    public static string ContentTypeOf(string fileName) =>
        ImageTypes.GetValueOrDefault(Path.GetExtension(fileName), FallbackContentType);

    /// <summary>檢查檔案能不能送：存在、是檔案、讀得到、沒超過上限。不合格回說明，合格回 null。</summary>
    public static string? Problem(string path)
    {
        if (Directory.Exists(path))
        {
            return "是資料夾，不是檔案。";
        }

        if (!File.Exists(path))
        {
            return "找不到這個檔案。";
        }

        try
        {
            var length = new FileInfo(path).Length;
            if (length > MaxBytes)
            {
                return $"檔案是 {Output.FileSizes.Format(length)}，超過上限 25 MiB，沒有送出。";
            }

            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "讀不了這個檔案（沒有讀取的權限，或被別的程式占用）。";
        }
    }

    /// <summary>
    /// 把伺服器給的檔名整理成可以安全寫在目前資料夾的名字：只留最後一段（去掉 / 與 \ 之前的部分），
    /// 去掉控制字元與格式字元（零寬、方向覆寫），作業系統不收的字元換成底線；整理完是空的、. 或 .. 就用備用的名字。
    /// 檔名來自上傳的人，不能讓它決定檔案寫到哪裡。
    /// </summary>
    public static string SafeFileName(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var lastSegment = raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..];
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. lastSegment
            .Where(character => !char.IsControl(character)
                && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)
            .Select(character => invalid.Contains(character) ? '_' : character)]).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
