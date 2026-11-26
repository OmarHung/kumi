using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;

namespace PmTool.Cli.Tests.Agent;

internal static class TestAttachments
{
    public static (AgentAttachment Attachment, byte[] Content) Image(string fileName = "截圖.png", int size = 40, byte fill = 7) =>
        Make(fileName, "image/png", AttachmentKinds.Image, size, fill);

    public static (AgentAttachment Attachment, byte[] Content) Pdf(string fileName = "報告.pdf", int size = 60, byte fill = 9) =>
        Make(fileName, "application/pdf", AttachmentKinds.Pdf, size, fill);

    private static (AgentAttachment, byte[]) Make(string fileName, string contentType, string kind, int size, byte fill)
    {
        var content = Enumerable.Repeat(fill, size).ToArray();
        return (new AgentAttachment(Guid.NewGuid(), fileName, contentType, kind, size, Convert.ToHexStringLower(SHA256.HashData(content))), content);
    }

    public static AgentJob With(AgentJob job, params AgentAttachment[] attachments) => job with { Attachments = attachments };

    public static JsonObject ToWire(AgentAttachment attachment) => new()
    {
        ["id"] = attachment.Id,
        ["fileName"] = attachment.FileName,
        ["contentType"] = attachment.ContentType,
        ["kind"] = attachment.Kind,
        ["sizeBytes"] = attachment.SizeBytes,
        ["sha256"] = attachment.Sha256,
    };
}

/// <summary>假的下載來源：依附件編號回內容；沒登記的丟 404 那一類的 <see cref="CliException"/>。</summary>
internal sealed class FakeAttachmentSource : IAttachmentSource
{
    private readonly Dictionary<Guid, byte[]> _contents = [];

    public List<(Guid JobId, Guid AttachmentId)> Opened { get; } = [];

    public FakeAttachmentSource Add(AgentAttachment attachment, byte[] content)
    {
        _contents[attachment.Id] = content;
        return this;
    }

    public Task<DownloadStream> OpenAsync(Guid jobId, Guid attachmentId, CancellationToken cancellation)
    {
        Opened.Add((jobId, attachmentId));
        if (!_contents.TryGetValue(attachmentId, out var content))
        {
            throw new CliException(ExitCodes.Error, "找不到這個附件（ai.attachment_not_found）。");
        }

        return Task.FromResult(new DownloadStream(new HttpResponseMessage(), new MemoryStream(content), null, content.Length, null));
    }
}

/// <summary>內容由測試給的串流（斷線、卡住）；不管要哪個附件都回同一個。</summary>
internal sealed class StreamAttachmentSource(Func<CancellationToken, Stream> open) : IAttachmentSource
{
    public Task<DownloadStream> OpenAsync(Guid jobId, Guid attachmentId, CancellationToken cancellation) =>
        Task.FromResult(new DownloadStream(new HttpResponseMessage(), open(cancellation), null, 1000, null));
}

/// <summary>先給一段資料，再照指定的方式出狀況：丟 <see cref="IOException"/>，或一直等到被取消。</summary>
internal sealed class BrokenStream(byte[] firstChunk, bool hang) : Stream
{
    private bool _gaveFirst;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_gaveFirst)
        {
            _gaveFirst = true;
            firstChunk.CopyTo(buffer);
            return firstChunk.Length;
        }

        if (!hang)
        {
            throw new IOException("The response ended prematurely.");
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
