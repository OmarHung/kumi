using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday;

/// <summary>沒有下載的檔案與原因（寫進 files/skipped.json，之後匯入時留言裡會註明）。</summary>
internal sealed record SkippedFile(string AssetId, string Name, long? SizeBytes, string Reason, string? Detail);

/// <summary>
/// 下載 --files 要的檔案到 dump 的 files 資料夾。用 asset 的簽章網址（一小時內有效，所以抓到一頁就下載一頁）。
/// 下載請求不帶 monday 的權杖：簽章網址自己就是憑證，權杖不該送到別的主機。
/// </summary>
internal sealed class MondayFileDownloader(HttpClient http, string dumpDirectory, string? previousDumpDirectory)
{
    /// <summary>單一檔案的大小上限（與伺服器的附件上限一致）；超過的不下載。</summary>
    public const long MaxFileBytes = 25L * 1024 * 1024;

    public const string TooLarge = "too_large";
    public const string DownloadFailed = "download_failed";
    public const string NoUrl = "no_url";
    public const string IoError = "io_error";

    private const int MaxRedirects = 5;
    private static readonly TimeSpan FileTimeout = TimeSpan.FromMinutes(10);

    private readonly HashSet<string> _handled = [];
    private readonly List<SkippedFile> _skipped = [];

    public int Downloaded { get; private set; }

    public int Reused { get; private set; }

    public IReadOnlyList<SkippedFile> Skipped => _skipped;

    public async Task EnsureAsync(MondayAssetRef asset, CancellationToken cancellationToken)
    {
        if (asset.Id.Length == 0 || !_handled.Add(asset.Id))
        {
            return;
        }

        if (asset.SizeBytes > MaxFileBytes)
        {
            _skipped.Add(new SkippedFile(asset.Id, asset.Name, asset.SizeBytes, TooLarge, "超過 25 MiB"));
            return;
        }

        var target = MondayDumpLayout.AssetPath(dumpDirectory, asset.Id, asset.Name);
        if (TryReuse(asset, target))
        {
            Reused++;
            return;
        }

        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            _skipped.Add(new SkippedFile(asset.Id, asset.Name, asset.SizeBytes, NoUrl, "monday 沒有給下載網址"));
            return;
        }

        await DownloadAsync(asset, url, target, cancellationToken);
    }

    /// <summary>寫 files/skipped.json（沒有跳過的也寫空陣列，讓人知道有檢查過）。</summary>
    public void WriteSkipped()
    {
        var list = new JsonArray();
        foreach (var file in _skipped)
        {
            list.Add(new JsonObject
            {
                ["assetId"] = file.AssetId,
                ["name"] = file.Name,
                ["sizeBytes"] = file.SizeBytes,
                ["reason"] = file.Reason,
                ["detail"] = file.Detail,
            });
        }

        MondayDumpLayout.WriteJson(MondayDumpLayout.SkippedPath(dumpDirectory), list);
    }

    /// <summary>上一份 dump 裡同編號、同大小的檔案直接複製過來，不重下（也不需要還有效的網址）。</summary>
    private bool TryReuse(MondayAssetRef asset, string target)
    {
        if (previousDumpDirectory is null || asset.SizeBytes is not { } size)
        {
            return false;
        }

        var old = MondayDumpLayout.AssetPath(previousDumpDirectory, asset.Id, asset.Name);
        if (!File.Exists(old) || new FileInfo(old).Length != size)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(old, target, overwrite: true);
        return true;
    }

    private async Task DownloadAsync(MondayAssetRef asset, Uri url, string target, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FileTimeout);
        var part = target + ".part";
        try
        {
            using var response = await OpenAsync(url, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _skipped.Add(new SkippedFile(asset.Id, asset.Name, asset.SizeBytes, DownloadFailed, $"HTTP {(int)response.StatusCode}"));
                return;
            }

            if (response.Content.Headers.ContentLength > MaxFileBytes)
            {
                _skipped.Add(new SkippedFile(asset.Id, asset.Name, response.Content.Headers.ContentLength, TooLarge, "超過 25 MiB"));
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!await CopyLimitedAsync(response, part, timeout.Token))
            {
                _skipped.Add(new SkippedFile(asset.Id, asset.Name, asset.SizeBytes, TooLarge, "超過 25 MiB"));
                return;
            }

            File.Move(part, target, overwrite: true);
            Downloaded++;
        }
        catch (HttpRequestException exception)
        {
            // 訊息可能帶著簽章網址，只記錯誤的種類。
            _skipped.Add(new SkippedFile(asset.Id, asset.Name, asset.SizeBytes, DownloadFailed, exception.HttpRequestError.ToString()));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _skipped.Add(new SkippedFile(asset.Id, asset.Name, asset.SizeBytes, DownloadFailed, "逾時"));
        }
        catch (IOException exception)
        {
            // 檔名太長、磁碟滿了：這個檔案不要，其他的照抓；不讓整個 fetch 中斷。
            _skipped.Add(new SkippedFile(asset.Id, asset.Name, asset.SizeBytes, IoError, exception.GetType().Name));
        }
        finally
        {
            // 失敗時 .part 可能還在（資料夾也可能根本還沒建）。
            if (File.Exists(part))
            {
                File.Delete(part);
            }
        }
    }

    /// <summary>自己跟著轉址（最多幾次、只收 https）：HttpClient 設成不自動轉，簽章網址常常是轉去儲存空間的。</summary>
    private async Task<HttpResponseMessage> OpenAsync(Uri start, CancellationToken cancellationToken)
    {
        var current = start;
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("kumi-cli");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is not (>= 300 and < 400) || response.Headers.Location is not { } location)
            {
                return response;
            }

            response.Dispose();
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (hop >= MaxRedirects || next.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException(HttpRequestError.InvalidResponse);
            }

            current = next;
        }
    }

    /// <summary>邊收邊寫，超過上限就停（Content-Length 可能沒給或不準）。回傳 false 表示超過。</summary>
    private static async Task<bool> CopyLimitedAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(path);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxFileBytes)
            {
                return false;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return true;
    }
}
