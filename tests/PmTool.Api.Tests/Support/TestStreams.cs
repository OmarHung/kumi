namespace PmTool.Api.Tests.Support;

/// <summary>記錄應用程式實際從內文讀了多少位元組。</summary>
internal sealed class CountingStream(Stream inner) : Stream
{
    private long _bytesRead;

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read)
    {
        Interlocked.Add(ref _bytesRead, read);
        return read;
    }
}

/// <summary>一讀就丟指定的例外，模擬 Kestrel 讀內文時發現格式錯誤或超過它自己的上限。</summary>
internal sealed class ThrowingStream(Exception exception) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw exception;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(exception);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromException<int>(exception);

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// 模擬把內文送到一半就停住的用戶端：讀取會一直等到 <see cref="Open"/> 被呼叫（之後回報內文結束），或讀取被取消。
/// 測試可以等到指定數量的請求真的卡在讀內文，再做下一步。
/// </summary>
internal sealed class StalledBodies
{
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _waiting = new(0);

    public Stream CreateStream() => new StalledStream(this);

    public void Open() => _opened.TrySetResult();

    public async Task WaitForStalledRequestsAsync(int count, TimeSpan timeout)
    {
        for (var index = 0; index < count; index++)
        {
            if (!await _waiting.WaitAsync(timeout))
            {
                throw new AssertionException($"等了 {timeout.TotalSeconds} 秒，只有 {index} 個請求開始讀內文，預期 {count} 個。");
            }
        }
    }

    private sealed class StalledStream(StalledBodies owner) : Stream
    {
        private int _announced;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _announced, 1) == 0)
            {
                owner._waiting.Release();
            }

            await owner._opened.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
