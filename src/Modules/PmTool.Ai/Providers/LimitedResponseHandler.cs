using System.Net;

namespace PmTool.Ai.Providers;

/// <summary>
/// 供應商回的東西（模型清單、錯誤內文、串流的整則回覆）最多讀 <see cref="AiRules.MaxProviderResponseBytes"/>，超過就中斷（AI-05）。
/// 相容端點是使用者填的網址：它可以回一個讀不完的串流，把伺服器的記憶體或連線佔住。
/// 限制放在 HTTP 這一層，SDK 怎麼讀（整個讀進來或一段一段讀）都一樣管得到。
/// </summary>
internal sealed class LimitedResponseHandler(HttpMessageHandler inner, long maxBytes = AiRules.MaxProviderResponseBytes)
    : DelegatingHandler(inner)
{
    internal const string TooLargeMessage = "供應商回的內容太大，已經中斷。";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            response.Dispose();
            throw new AiProviderException(TooLargeMessage);
        }

        response.Content = new LimitedContent(response.Content, maxBytes);
        return response;
    }

    private sealed class LimitedContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly long _max;

        public LimitedContent(HttpContent inner, long max)
        {
            _inner = inner;
            _max = max;
            foreach (var header in inner.Headers)
            {
                // 長度由實際讀到的為準；原本的 Content-Length 照搬會和真正送出的對不上。
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await using var source = new LimitedStream(await _inner.ReadAsStreamAsync(cancellationToken), _max);
            await source.CopyToAsync(stream, cancellationToken);
        }

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            new LimitedStream(await _inner.ReadAsStreamAsync(cancellationToken), _max);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class LimitedStream(Stream inner, long max) : Stream
    {
        private long _total;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

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
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }

        private int Count(int read)
        {
            _total += read;
            return _total > max ? throw new AiProviderException(TooLargeMessage) : read;
        }
    }
}
