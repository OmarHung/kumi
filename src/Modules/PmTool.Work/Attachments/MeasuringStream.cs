using System.Security.Cryptography;

namespace PmTool.Work.Attachments;

/// <summary>讀的量超過上限；只在 <see cref="MeasuringStream"/> 與兩個儲存空間之間用，不會漏到模組外面。</summary>
internal sealed class AttachmentTooLargeException() : Exception("附件超過大小上限。");

/// <summary>
/// 包著要上傳的內容，讀多少就算多少、同時算 SHA-256；超過上限時丟 <see cref="AttachmentTooLargeException"/>。
/// 不管儲存空間怎麼讀（本機的複製迴圈、GCS 的可續傳上傳），大小與雜湊都在同一個地方算，兩個實作不會各算各的。
/// 上限檢查讀到上限加一個位元組為止：剛好等於上限的檔案可以過。
/// </summary>
internal sealed class MeasuringStream(Stream inner, long maxBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>讀完之後呼叫，一次。</summary>
    public StoredFile Complete() => new(BytesRead, Convert.ToHexStringLower(_hash.GetHashAndReset()));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        Count(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Count(buffer[..read]);
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Count(ReadOnlySpan<byte> data)
    {
        BytesRead += data.Length;
        if (BytesRead > maxBytes)
        {
            throw new AttachmentTooLargeException();
        }

        _hash.AppendData(data);
    }
}
