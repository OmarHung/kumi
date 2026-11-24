using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PmTool.Storage;
using PmTool.Work.Attachments;

namespace PmTool.Work.Tests;

/// <summary>附件測試用的小工具：造上傳的內容、數儲存空間裡的檔案。</summary>
internal static class AttachmentSupport
{
    public static NewAttachment Upload(string fileName, byte[] bytes, string? contentType = null) =>
        new() { FileName = fileName, ContentType = contentType, Content = new MemoryStream(bytes) };

    public static NewAttachment Upload(string fileName, string text, string? contentType = "text/plain") =>
        Upload(fileName, System.Text.Encoding.UTF8.GetBytes(text), contentType);

    /// <summary>一個不用真的配置記憶體的大檔案。</summary>
    public static NewAttachment Upload(string fileName, long length, string? contentType = "application/zip") =>
        new() { FileName = fileName, ContentType = contentType, Content = new ZeroStream(length) };

    public static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>儲存空間裡所有的檔案（含沒寫完的暫存檔）。</summary>
    public static string[] FilesIn(WorkTestHost host) =>
        [.. Directory.EnumerateFiles(host.AttachmentDirectory, "*", SearchOption.AllDirectories)];

    public static async Task<byte[]> ReadAllAsync(AttachmentContent content)
    {
        await using (content)
        {
            using var buffer = new MemoryStream();
            await content.Content.CopyToAsync(buffer);
            return buffer.ToArray();
        }
    }

    /// <summary>全是 0 的內容，到長度為止；不能 seek，和網路上傳進來的串流一樣。</summary>
    public sealed class ZeroStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>一讀就丟例外：用來證明被擋下的上傳沒有讀內容。</summary>
    public sealed class UnreadableStream : Stream
    {
        public bool WasRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            throw new InvalidOperationException("不應該讀內容。");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>包著真的儲存空間，讓測試在寫完之後動手腳、或讓刪除失敗。</summary>
internal sealed class HookedAttachmentStore(IFileStore inner) : IFileStore
{
    public Func<Task>? AfterWrite { get; set; }

    public bool FailDeletes { get; set; }

    public async Task<StoredFile?> WriteAsync(string key, Stream content, string contentType, long maxBytes, CancellationToken cancellationToken)
    {
        var stored = await inner.WriteAsync(key, content, contentType, maxBytes, cancellationToken);
        if (stored is not null && AfterWrite is { } hook)
        {
            await hook();
        }

        return stored;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) => inner.OpenReadAsync(key, cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        FailDeletes ? throw new IOException("測試：刪不掉。") : inner.DeleteAsync(key, cancellationToken);

    public Task VerifyAsync(CancellationToken cancellationToken) => inner.VerifyAsync(cancellationToken);
}

/// <summary>收集模組寫的記錄，看有沒有在該記的時候記。</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new Capturing(_entries);

    public void Dispose()
    {
    }

    private sealed class Capturing(ConcurrentQueue<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
