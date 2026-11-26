using System.Text;
using PmTool.Ai.Attachments;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>看內容判斷附件的種類：不看副檔名與瀏覽器報的類型。</summary>
[TestFixture]
public sealed class AttachmentSnifferTests
{
    internal static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];

    internal static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];

    internal static readonly byte[] Gif = [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0];

    internal static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8];

    internal static readonly byte[] Pdf = [.. "%PDF-1.7\n1 0 obj\n"u8];

    private static Task<SniffedFile?> SniffAsync(byte[] bytes) =>
        AttachmentSniffer.SniffAsync(new MemoryStream(bytes), CancellationToken.None);

    [Test]
    public async Task Sniff_FourBitmapFormats_AreImagesWithTheirOwnTypes()
    {
        // Arrange / Act
        var results = new[] { await SniffAsync(Png), await SniffAsync(Jpeg), await SniffAsync(Gif), await SniffAsync(Webp) };

        // Assert
        Assert.That(
            results.Select(r => (r?.Kind, r?.ContentType)),
            Is.EqualTo(new (AssistantAttachmentKind?, string?)[]
            {
                (AssistantAttachmentKind.Image, "image/png"),
                (AssistantAttachmentKind.Image, "image/jpeg"),
                (AssistantAttachmentKind.Image, "image/gif"),
                (AssistantAttachmentKind.Image, "image/webp"),
            }));
    }

    [Test]
    public async Task Sniff_PdfHeader_IsPdf()
    {
        var result = await SniffAsync(Pdf);

        Assert.That((result?.Kind, result?.ContentType), Is.EqualTo((AssistantAttachmentKind.Pdf, "application/pdf")));
    }

    [Test]
    public async Task Sniff_RiffThatIsNotWebp_IsNotAnImage()
    {
        // Arrange：WAV 也是 RIFF 開頭，但裡面有 NUL，不是文字。
        byte[] wav = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WAVEfmt "u8];

        // Act / Assert
        Assert.That(await SniffAsync(wav), Is.Null);
    }

    [TestCase("# 筆記\n\n- 第一項\r\n- 第二項\t完\f")]
    [TestCase("﻿col1,col2\n1,2")]
    [TestCase("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    public async Task Sniff_ValidUtf8WithoutControlCharacters_IsText(string content)
    {
        var result = await SniffAsync(Encoding.UTF8.GetBytes(content));

        Assert.That((result?.Kind, result?.ContentType), Is.EqualTo((AssistantAttachmentKind.Text, "text/plain")));
    }

    [Test]
    public async Task Sniff_TextThatLooksLikeAPdfOnlyByNameButNotByContent_IsStillText()
    {
        var result = await SniffAsync("這不是 PDF，只是檔名叫 .pdf"u8.ToArray());

        Assert.That(result?.Kind, Is.EqualTo(AssistantAttachmentKind.Text));
    }

    [TestCase(new byte[] { 0x61, 0x00, 0x62 })]
    [TestCase(new byte[] { 0x61, 0x1B, 0x5B })]
    [TestCase(new byte[] { 0x61, 0x7F })]
    [TestCase(new byte[] { 0x61, 0xC2, 0x85 })]
    [TestCase(new byte[] { 0xFF, 0xFE, 0x61, 0x00 })]
    [TestCase(new byte[] { 0xC3, 0x28 })]
    [TestCase(new byte[] { })]
    public async Task Sniff_ControlCharactersInvalidUtf8OrEmpty_AreNotAllowed(byte[] content) =>
        Assert.That(await SniffAsync(content), Is.Null);

    [Test]
    public async Task Sniff_Zip_IsNotAllowed() =>
        Assert.That(await SniffAsync([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00]), Is.Null);

    [Test]
    public async Task Sniff_Utf8ByteOrderMarkBeforeText_IsText()
    {
        // Arrange：Excel 匯出的 CSV 常帶 BOM（EF BB BF），它不是控制字元。
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "a,b\n1,2"u8];

        // Act
        var result = await SniffAsync(bytes);

        // Assert
        Assert.That(result?.Kind, Is.EqualTo(AssistantAttachmentKind.Text));
    }

    [Test]
    public async Task Sniff_LineSeparatorU2028_IsText()
    {
        // Arrange：U+2028 是格式字元（E2 80 A8），不在 C0／C1 控制字元的範圍裡。
        byte[] bytes = [.. "第一行"u8, 0xE2, 0x80, 0xA8, .. "第二行"u8];

        // Act
        var result = await SniffAsync(bytes);

        // Assert
        Assert.That(result?.Kind, Is.EqualTo(AssistantAttachmentKind.Text));
    }

    [TestCase(0x80)]
    [TestCase(0x85)]
    [TestCase(0x9F)]
    public async Task Sniff_C1ControlCharacterAsUtf8_IsNotText(int second)
    {
        // Arrange：U+0080–U+009F 的 UTF-8 是 C2 80–C2 9F，夾在正常文字中間也一樣。
        byte[] bytes = [.. "前"u8, 0xC2, (byte)second, .. "後"u8];

        // Act / Assert
        Assert.That(await SniffAsync(bytes), Is.Null);
    }

    [Test]
    public async Task Sniff_NoBreakSpaceRightAfterTheC1Range_IsText()
    {
        // Arrange：U+00A0（C2 A0）是不換行空白，不是控制字元。
        byte[] bytes = [.. "a"u8, 0xC2, 0xA0, .. "b"u8];

        // Act
        var result = await SniffAsync(bytes);

        // Assert
        Assert.That(result?.Kind, Is.EqualTo(AssistantAttachmentKind.Text));
    }
}
