using System.Collections.Concurrent;
using System.Security.Cryptography;
using PmTool.Storage;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>測試用的儲存空間：檔案放在記憶體裡，可以讓刪除失敗、可以偷偷抽掉檔案，記住被刪掉的鍵。</summary>
internal sealed class MemoryFileStore : IFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();
    private readonly ConcurrentQueue<string> _deleted = new();
    private readonly ConcurrentDictionary<string, int> _openCounts = new();

    /// <summary>設成 true 時，刪除丟例外（模擬儲存空間壞了）。</summary>
    public bool FailDeletes { get; set; }

    /// <summary>設成 true 時，讀檔丟例外（模擬儲存空間壞了）。</summary>
    public bool FailReads { get; set; }

    /// <summary>只讓這幾個鍵刪不掉（其他照常），模擬某幾個檔案一直刪不掉。</summary>
    public ISet<string> FailDeleteKeys { get; } = new HashSet<string>();

    /// <summary>每次開檔讀之前執行（參數是鍵）：測試用它在「檔案讀不到」的瞬間改資料庫。</summary>
    public Action<string>? BeforeOpen { get; set; }

    /// <summary>每個鍵被開檔讀過幾次；用來確認不該讀的檔案沒有被白讀。</summary>
    public IReadOnlyDictionary<string, int> OpenCounts => _openCounts;

    public int OpensOf(Guid id) => _openCounts.GetValueOrDefault(id.ToString("D"));

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

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        BeforeOpen?.Invoke(key);
        if (FailReads)
        {
            throw new IOException("儲存空間壞了。");
        }

        _openCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
        return Task.FromResult<Stream?>(_files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (FailDeletes || FailDeleteKeys.Contains(key))
        {
            throw new IOException("儲存空間壞了。");
        }

        _files.TryRemove(key, out _);
        _deleted.Enqueue(key);
        return Task.CompletedTask;
    }

    public Task VerifyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
