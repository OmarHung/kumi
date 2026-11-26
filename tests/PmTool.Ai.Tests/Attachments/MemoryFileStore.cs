using System.Collections.Concurrent;
using System.Security.Cryptography;
using PmTool.Storage;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>測試用的儲存空間：檔案放在記憶體裡，可以讓刪除失敗、可以偷偷抽掉檔案，記住被刪掉的鍵。</summary>
internal sealed class MemoryFileStore : IFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();
    private readonly ConcurrentQueue<string> _deleted = new();

    /// <summary>設成 true 時，刪除丟例外（模擬儲存空間壞了）。</summary>
    public bool FailDeletes { get; set; }

    public IReadOnlyCollection<string> Keys => [.. _files.Keys];

    public IReadOnlyList<string> DeletedKeys => [.. _deleted];

    public bool Contains(Guid id) => _files.ContainsKey(id.ToString("D"));

    public byte[] Bytes(Guid id) => _files[id.ToString("D")];

    public void Remove(Guid id) => _files.TryRemove(id.ToString("D"), out _);

    public async Task<StoredFile?> WriteAsync(string key, Stream content, string contentType, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > maxBytes)
        {
            return null;
        }

        var bytes = buffer.ToArray();
        _files[key] = bytes;
        return new StoredFile(bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(_files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (FailDeletes)
        {
            throw new IOException("儲存空間壞了。");
        }

        _files.TryRemove(key, out _);
        _deleted.Enqueue(key);
        return Task.CompletedTask;
    }

    public Task VerifyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
