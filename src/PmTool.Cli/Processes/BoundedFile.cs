namespace PmTool.Cli.Processes;

/// <summary>
/// 讀引擎家目錄裡的檔案（登入資料、模型快取）：那個資料夾可能被別的程式寫入，所以只讀一般檔案、不跟隨符號連結
/// （連結可以指到 /dev/zero、別人的檔案或巨大的檔案）、最多讀 <see cref="MaxBytes"/>。讀不到、太大、不是一般檔案一律回 null 並說明原因，
/// 任何 I/O 例外都攔下來，不能讓 kumi 因為這種檔案當掉。
/// </summary>
internal static class BoundedFile
{
    public const int MaxBytes = 1024 * 1024;

    public static byte[]? TryReadBytes(string path, Action<string>? log = null)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                log?.Invoke($"{path} 是符號連結，不讀（引擎的資料夾裡只讀一般檔案），當作沒有。");
                return null;
            }

            if (info.Attributes.HasFlag(FileAttributes.Directory) || info.Attributes.HasFlag(FileAttributes.Device) || info.Length > MaxBytes)
            {
                log?.Invoke($"{path} 不是一般檔案或超過 1 MiB，不讀，當作沒有。");
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            // 長度會在檢查之後變：多讀一個位元組，超過上限就放棄。
            var buffer = new byte[MaxBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            if (total > MaxBytes)
            {
                log?.Invoke($"{path} 超過 1 MiB，不讀，當作沒有。");
                return null;
            }

            return buffer[..total];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            log?.Invoke($"讀不了 {path}（{exception.GetType().Name}），當作沒有。");
            return null;
        }
    }

    public static string? TryReadText(string path, Action<string>? log = null) =>
        TryReadBytes(path, log) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;
}
