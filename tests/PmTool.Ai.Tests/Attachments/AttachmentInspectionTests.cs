using System.Buffers.Binary;
using System.Text;
using PmTool.Abstractions;
using PmTool.Ai.Attachments;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>上傳時擋掉「一定會被供應商拒絕」的檔案：圖片的寬或高超過 8000 像素、加密的 PDF；讀不出尺寸的照收。</summary>
[TestFixture]
public sealed class AttachmentInspectionTests
{
    private const int Limit = 8000;

    private static Task<SniffedFile?> SniffAsync(byte[] bytes) =>
        AttachmentSniffer.SniffAsync(new MemoryStream(bytes), CancellationToken.None);

    internal static byte[] PngOf(int width, int height)
    {
        var bytes = new byte[33];
        AttachmentSnifferTests.Png.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        return bytes;
    }

    internal static byte[] JpegOf(int width, int height)
    {
        // SOI、APP0（JFIF，長度 16）、SOF0（長度 17：精度、高、寬、三個元件）。
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 };
        bytes.AddRange([0xFF, 0xC0, 0, 17, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
        return [.. bytes];
    }

    internal static byte[] GifOf(int width, int height) =>
        [.. "GIF89a"u8, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0, 0, 0];

    private static byte[] WebpLossy(int width, int height) =>
        [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8, 0x18, 0, 0, 0, 0x10, 0, 0, 0x9D, 0x01, 0x2A,
            (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8)];

    private static byte[] WebpLossless(int width, int height)
    {
        var packed = (uint)(width - 1) | ((uint)(height - 1) << 14);
        var bits = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bits, packed);
        return [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8L"u8, 0x18, 0, 0, 0, 0x2F, .. bits];
    }

    private static byte[] WebpExtended(int width, int height)
    {
        var bytes = new List<byte>([.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8X"u8, 10, 0, 0, 0, 0, 0, 0, 0]);
        bytes.AddRange([(byte)(width - 1), (byte)((width - 1) >> 8), (byte)((width - 1) >> 16)]);
        bytes.AddRange([(byte)(height - 1), (byte)((height - 1) >> 8), (byte)((height - 1) >> 16)]);
        return [.. bytes];
    }

    public static IEnumerable<TestCaseData> OversizedImages()
    {
        yield return new TestCaseData(PngOf(9000, 100)).SetName("PNG 太寬");
        yield return new TestCaseData(PngOf(100, 8001)).SetName("PNG 太高");
        yield return new TestCaseData(JpegOf(9000, 100)).SetName("JPEG 太寬");
        yield return new TestCaseData(JpegOf(100, 12000)).SetName("JPEG 太高");
        yield return new TestCaseData(GifOf(9000, 10)).SetName("GIF 太寬");
        yield return new TestCaseData(WebpLossy(9000, 10)).SetName("WebP（有損）太寬");
        yield return new TestCaseData(WebpLossless(10, 9000)).SetName("WebP（無損）太高");
        yield return new TestCaseData(WebpExtended(9000, 10)).SetName("WebP（擴充）太寬");
    }

    public static IEnumerable<TestCaseData> AcceptableImages()
    {
        yield return new TestCaseData(PngOf(Limit, Limit)).SetName("PNG 剛好上限");
        yield return new TestCaseData(JpegOf(Limit, 100)).SetName("JPEG 剛好上限");
        yield return new TestCaseData(GifOf(100, 100)).SetName("GIF 正常");
        yield return new TestCaseData(WebpLossy(Limit, 100)).SetName("WebP（有損）剛好上限");
        yield return new TestCaseData(WebpLossless(Limit, 100)).SetName("WebP（無損）剛好上限");
        yield return new TestCaseData(WebpExtended(Limit, Limit)).SetName("WebP（擴充）剛好上限");
        yield return new TestCaseData(AttachmentSnifferTests.Png).SetName("PNG 只有檔頭、讀不出尺寸");
        yield return new TestCaseData(AttachmentSnifferTests.Jpeg).SetName("JPEG 沒有 SOF、讀不出尺寸");
        yield return new TestCaseData(AttachmentSnifferTests.Webp).SetName("WebP 截斷、讀不出尺寸");
        yield return new TestCaseData(Image(200)).SetName("全零的 PNG（寬高 0）");
    }

    [TestCaseSource(nameof(OversizedImages))]
    public async Task Sniff_ImageWiderOrTallerThanTheLimit_ReportsAProblemWithTheSizes(byte[] image)
    {
        // Act
        var result = await SniffAsync(image);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.Kind, Is.EqualTo(AssistantAttachmentKind.Image));
            Assert.That(result?.Problem, Does.Contain("8000").And.Contain("像素"));
        }
    }

    [TestCaseSource(nameof(AcceptableImages))]
    public async Task Sniff_ImageWithinTheLimitOrWithUnreadableSize_HasNoProblem(byte[] image)
    {
        // Act
        var result = await SniffAsync(image);

        // Assert
        Assert.That(result, Has.Property("Kind").EqualTo(AssistantAttachmentKind.Image).And.Property("Problem").Null);
    }

    [TestCase("%PDF-1.7\n1 0 obj\n<<>>\nendobj\ntrailer\n<< /Root 1 0 R /Encrypt 5 0 R >>\n%%EOF")]
    [TestCase("%PDF-1.5\n%%EOF\n<< /Type /XRef /Encrypt 9 0 R /Size 10 >>")]
    public async Task Sniff_PdfWithEncrypt_ReportsAProblem(string pdf)
    {
        // Act
        var result = await SniffAsync(Encoding.ASCII.GetBytes(pdf));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.Kind, Is.EqualTo(AssistantAttachmentKind.Pdf));
            Assert.That(result?.Problem, Does.Contain("加密"));
        }
    }

    [Test]
    public async Task Sniff_PdfWithoutEncrypt_HasNoProblem()
    {
        var result = await SniffAsync(Encoding.ASCII.GetBytes("%PDF-1.7\ntrailer\n<< /Root 1 0 R >>\n%%EOF"));

        Assert.That(result, Has.Property("Kind").EqualTo(AssistantAttachmentKind.Pdf).And.Property("Problem").Null);
    }

    [Test]
    public async Task Upload_OversizedImage_IsRejectedAsTypeNotAllowedWithTheLimitAndNothingIsKept()
    {
        // Arrange
        await using var h = await AssistantHarness.StartAsync();

        // Act
        var result = await h.TryUploadAsync("大圖.png", PngOf(9000, 100));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTypeNotAllowed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Does.Contain("8000").And.Contain("9000"));
            Assert.That(h.Store().Keys, Is.Empty, "被擋下的上傳不留檔案");
        }
    }

    [Test]
    public async Task Upload_EncryptedPdf_IsRejectedAsTypeNotAllowedWithAnExplanation()
    {
        // Arrange
        await using var h = await AssistantHarness.StartAsync();

        // Act
        var result = await h.TryUploadAsync("機密.pdf", Encoding.ASCII.GetBytes("%PDF-1.6\ntrailer\n<< /Encrypt 3 0 R >>\n"));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTypeNotAllowed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Does.Contain("加密"));
            Assert.That(h.Store().Keys, Is.Empty);
        }
    }

    [Test]
    public async Task Upload_ImageWithinTheLimit_IsAccepted()
    {
        // Arrange
        await using var h = await AssistantHarness.StartAsync();

        // Act
        var result = await h.TryUploadAsync("正常.png", PngOf(1600, 900));

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
    }
}
