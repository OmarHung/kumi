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
}
