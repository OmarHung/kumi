using System.Text;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 下載附件時的回應標頭。使用者上傳的內容和網頁在同一個網域，瀏覽器直接打開 HTML 或 SVG 就能在這個網域執行腳本（XSS），
/// 所以只有四種點陣圖片原樣呈現，其他一律當成要下載的二進位檔；再加上 nosniff 與沙箱的 CSP，就算瀏覽器想猜類型也沒有用。
/// </summary>
internal static class AttachmentDownload
{
    public const string ContentSecurityPolicy = "default-src 'none'; sandbox";

    /// <summary>內容的 SHA-256（小寫十六進位）。只拿到附件編號的用戶端（CLI 的下載）不必另外查一次就能核對內容。</summary>
    public const string Sha256Header = "X-Attachment-Sha256";

    private const string OctetStream = "application/octet-stream";
    private const string CacheControl = "private, max-age=0";
    private const char FallbackCharacter = '_';

    private static readonly HashSet<string> InlineTypes = new(StringComparer.Ordinal)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
    };

    /// <summary>圖片直接在瀏覽器開；其他一律下載。比對的是服務存下來、已經整理過的小寫類型。</summary>
    public static bool IsInline(string contentType) => InlineTypes.Contains(contentType);

    public static void Apply(HttpResponse response, Attachment attachment)
    {
        var inline = IsInline(attachment.ContentType);
        var headers = response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.CacheControl = CacheControl;
        headers.ContentDisposition = DispositionOf(inline ? "inline" : "attachment", attachment.FileName);
        response.ContentType = inline ? attachment.ContentType : OctetStream;
        response.ContentLength = attachment.SizeBytes;
        headers[Sha256Header] = attachment.Sha256;
    }

    /// <summary>
    /// RFC 6266 與 5987：<c>filename*</c> 帶完整的檔名（UTF-8、百分比編碼），<c>filename</c> 是只含 ASCII 的退路，
    /// 給不認得 <c>filename*</c> 的舊用戶端。兩個一定都帶，連純 ASCII 的檔名也一樣。
    /// </summary>
    public static string DispositionOf(string type, string fileName) =>
        $"{type}; filename=\"{AsciiFallback(fileName)}\"; filename*=UTF-8''{PercentEncode(fileName)}";

    private static string AsciiFallback(string fileName)
    {
        var builder = new StringBuilder(fileName.Length);
        foreach (var rune in fileName.EnumerateRunes())
        {
            // 引號與反斜線會提前結束或跳脫引號字串，% 會被某些用戶端當成編碼的開頭。
            var plain = rune.Value is > 0x20 and < 0x7F && rune.Value is not ('"' or '\\' or '%' or ';' or ',');
            builder.Append(plain || rune.Value == ' ' ? (char)rune.Value : FallbackCharacter);
        }

        return builder.ToString();
    }

    private static string PercentEncode(string fileName)
    {
        var builder = new StringBuilder();
        foreach (var value in Encoding.UTF8.GetBytes(fileName))
        {
            if (IsAttributeCharacter(value))
            {
                builder.Append((char)value);
            }
            else
            {
                builder.Append('%').Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    // RFC 5987 的 attr-char：英數字與 ! # $ & + - . ^ _ ` | ~。
    private static bool IsAttributeCharacter(byte value) =>
        value is >= (byte)'a' and <= (byte)'z'
            or >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'!' or (byte)'#' or (byte)'$' or (byte)'&' or (byte)'+' or (byte)'-' or (byte)'.'
            or (byte)'^' or (byte)'_' or (byte)'`' or (byte)'|' or (byte)'~';
}
