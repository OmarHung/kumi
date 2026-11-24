using System.Globalization;

namespace PmTool.Storage;

/// <summary>
/// 主機上的一個資料夾。檔案放在「編號最後兩個十六進位字元」的子資料夾裡，單一資料夾的檔案不會太多（最多 256 個子資料夾）。
/// 先寫到同一個子資料夾的暫存檔、寫完再 rename：寫一半的（程序被砍、磁碟滿）不會被當成完成的檔案，
/// 同一個資料夾內的 rename 在同一個檔案系統上是不可分割的。
/// </summary>
internal sealed class LocalFileStore(string directory) : IFileStore
{
    private const int BufferSize = 81_920;
    private const string TemporarySuffix = ".tmp";

    public async Task<StoredFile?> WriteAsync(string key, Stream content, string contentType, long maxBytes, CancellationToken cancellationToken)
    {
        var finalPath = PathOf(key);
        var temporaryPath = $"{finalPath}.{Guid.NewGuid():N}{TemporarySuffix}";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(finalPath)!);
        await using var measured = new MeasuringStream(content, maxBytes);
        try
        {
            await using (var file = new FileStream(temporaryPath, WriteOptions()))
            {
                await measured.CopyToAsync(file, BufferSize, cancellationToken);

                // 落盤再 rename：只 flush 的話，斷電時 rename 可能先落盤、內容沒有，留下 0 位元組的正式檔，
                // 而且備份只補缺的檔案，壞檔會被固定在備份裡。
                file.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            return measured.Complete();
        }
        catch (FileTooLargeException)
        {
            DeleteQuietly(temporaryPath);
            return null;
        }
        catch
        {
            DeleteQuietly(temporaryPath);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = BufferSize,
            };
            return Task.FromResult<Stream?>(new FileStream(PathOf(key), options));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            File.Delete(PathOf(key));
        }
        catch (DirectoryNotFoundException)
        {
            // 子資料夾都還沒建過：本來就沒有這個檔案。
        }

        return Task.CompletedTask;
    }

    public Task VerifyAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                $"{FileStoreSettings.DirectorySetting} 指定的資料夾「{directory}」不存在：請先建立它，並讓執行服務的使用者可以寫入。");
        }

        // 試寫一個暫存檔再刪掉：目錄存在不代表寫得進去（唯讀掛載、使用者沒有權限）。
        var probe = System.IO.Path.Combine(directory, $".write-test-{Guid.NewGuid():N}{TemporarySuffix}");
        try
        {
            using (new FileStream(probe, WriteOptions()))
            {
            }

            File.Delete(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(probe);
            throw new InvalidOperationException(
                $"{FileStoreSettings.DirectorySetting} 指定的資料夾「{directory}」寫不進去（{exception.Message}）：請讓執行服務的使用者可以寫入。",
                exception);
        }

        return Task.CompletedTask;
    }

    /// <summary>檔案只給執行服務的使用者讀寫（600）；Windows 沒有這個設定，用預設。</summary>
    private static FileStreamOptions WriteOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = BufferSize,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    /// <summary>鍵一定是 Guid 的格式：檔案路徑完全由它決定，不給路徑符號進來的機會。</summary>
    private string PathOf(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id))
        {
            throw new ArgumentException("附件的鍵必須是編號。", nameof(key));
        }

        var canonical = id.ToString("D", CultureInfo.InvariantCulture);
        return System.IO.Path.Combine(directory, canonical[^2..], canonical);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 暫存檔刪不掉不影響結果；它不會被當成完成的檔案。
        }
    }
}
