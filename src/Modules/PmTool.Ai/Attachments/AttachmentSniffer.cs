using System.Text.Unicode;

namespace PmTool.Ai.Attachments;

/// <summary>看內容判斷出來的種類與類型。</summary>
/// <param name="Problem">
/// 類型認得、但供應商一定會拒絕的原因（圖片的寬或高太大、PDF 加密），給使用者看的繁體中文；沒有問題是 null。
/// 上傳時擋下來，不然壞檔案存進歷史之後，每一輪重送都會被供應商退回。
/// </param>
internal sealed record SniffedFile(AssistantAttachmentKind Kind, string ContentType, string? Problem = null);

/// <summary>
/// 看內容判斷附件的種類，不信任檔名與瀏覽器報的類型：四種點陣圖與 PDF 看開頭的位元組；
/// 其他只有「合法的 UTF-8、沒有 NUL 與控制字元（Tab、換行、換頁、BOM 以外）」的才算文字檔。
/// SVG 這類文字會被判成文字檔：下載時照舊一律當二進位檔（<c>AttachmentDownload</c>），不會在網域上執行。
/// </summary>
internal static class AttachmentSniffer
{
    public const string PngType = "image/png";

    public const string JpegType = "image/jpeg";

    public const string GifType = "image/gif";

    public const string WebpType = "image/webp";

    public const string PdfType = "application/pdf";

    public const string TextType = "text/plain";

    private const int HeaderLength = 12;

    /// <summary>圖片的寬或高上限（像素）：Anthropic 單張超過 8000 就拒絕，其他供應商的上限更寬，取最嚴的。</summary>
    internal const int MaxImageDimension = 8000;

    private static readonly byte[] EncryptMark = "/Encrypt"u8.ToArray();

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private static readonly byte[] Gif87 = "GIF87a"u8.ToArray();

    private static readonly byte[] Gif89 = "GIF89a"u8.ToArray();

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    private static readonly byte[] Riff = "RIFF"u8.ToArray();

    private static readonly byte[] WebpMark = "WEBP"u8.ToArray();

    /// <summary>
    /// 判斷串流的內容；空的或不認得的回 null。<paramref name="content"/> 要從頭開始、可以一直讀到結束，
    /// 長度不超過 <see cref="AssistantAttachmentRules.MaxUploadBytes"/>（寫檔時已經擋住）。
    /// </summary>
    public static async Task<SniffedFile?> SniffAsync(Stream content, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];
        var read = await content.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, cancellationToken);
        if (read == 0)
        {
            return null;
        }

        // 圖片的尺寸（JPEG 的 SOF 可能在很後面）、PDF 的加密標記、文字檔的內容，都要整份看過才知道：最大 10 MiB（上傳的上限）。
        using var rest = new MemoryStream();
        rest.Write(header, 0, read);
        await content.CopyToAsync(rest, cancellationToken);
        var all = rest.GetBuffer().AsSpan(0, (int)rest.Length);
        if (SniffBinary(all) is { } binary)
        {
            return binary with { Problem = ProblemOf(binary, all) };
        }

        return IsText(all) ? new SniffedFile(AssistantAttachmentKind.Text, TextType) : null;
    }

    private static string? ProblemOf(SniffedFile file, ReadOnlySpan<byte> all) => file.Kind switch
    {
        AssistantAttachmentKind.Image when ImageDimensions.TryRead(file.ContentType, all, out var width, out var height)
            && (width > MaxImageDimension || height > MaxImageDimension) =>
            $"圖片的寬或高不能超過 {MaxImageDimension} 像素（這張是 {width}×{height}），模型不收；請縮小後再上傳。",
        AssistantAttachmentKind.Pdf when all.IndexOf(EncryptMark) >= 0 =>
            "這個 PDF 有加密（需要密碼才能讀），模型讀不了；請先解除加密再上傳。",
        _ => null,
    };

    public static SniffedFile? SniffBinary(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(PngSignature))
        {
            return new SniffedFile(AssistantAttachmentKind.Image, PngType);
        }

        if (header.StartsWith(JpegSignature))
        {
            return new SniffedFile(AssistantAttachmentKind.Image, JpegType);
        }

        if (header.StartsWith(Gif87) || header.StartsWith(Gif89))
        {
            return new SniffedFile(AssistantAttachmentKind.Image, GifType);
        }

        if (header.Length >= HeaderLength && header.StartsWith(Riff) && header[8..12].SequenceEqual(WebpMark))
        {
            return new SniffedFile(AssistantAttachmentKind.Image, WebpType);
        }

        return header.StartsWith(PdfSignature) ? new SniffedFile(AssistantAttachmentKind.Pdf, PdfType) : null;
    }

    /// <summary>
    /// 合法的 UTF-8，而且沒有 NUL 與控制字元：C0（0x00–0x1F）只放行 Tab、換行、換頁與 CR，再加上 0x7F 與 C1（U+0080–U+009F，UTF-8 是 C2 80–C2 9F）。
    /// 直接看位元組，不必把整份解成字串。
    /// </summary>
    public static bool IsText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || !Utf8.IsValid(bytes))
        {
            return false;
        }

        for (var index = 0; index < bytes.Length; index++)
        {
            var b = bytes[index];
            if (b < 0x20 ? b is not (0x09 or 0x0A or 0x0C or 0x0D) : b == 0x7F)
            {
                return false;
            }

            if (b == 0xC2 && index + 1 < bytes.Length && bytes[index + 1] is >= 0x80 and <= 0x9F)
            {
                return false;
            }
        }

        return true;
    }
}
