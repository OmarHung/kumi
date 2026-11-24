using System.IO.Pipelines;

namespace PmTool.Storage;

/// <summary>
/// 把「寫進一個串流」的下載（GCS 的 DownloadObject）接到一個可以邊下載邊讀的串流：下載寫進管線，讀的人從另一頭讀，
/// 讀得慢下載也跟著慢。
/// </summary>
internal static class PipedDownload
{
    public static Stream Start(Func<Stream, CancellationToken, Task> download)
    {
        var pipe = new Pipe();
        var cancellation = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                // leaveOpen：串流關閉時不能替管線「成功完成」，否則下載失敗時讀的人只會看到正常結束，拿到截斷的檔案。
                await download(pipe.Writer.AsStream(leaveOpen: true), cancellation.Token);
                await pipe.Writer.CompleteAsync();
            }
            catch (Exception exception)
            {
                // 讀的人下一次讀就會看到這個例外，不會把下載到一半的內容當成完整的檔案。
                await pipe.Writer.CompleteAsync(exception);
            }
        });
        return new CancellingStream(pipe.Reader.AsStream(), cancellation);
    }

    /// <summary>讀的人關掉串流（用戶端中斷）時，一併取消還在跑的下載。</summary>
    internal sealed class CancellingStream(Stream inner, CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

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
                cancellation.Cancel();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
