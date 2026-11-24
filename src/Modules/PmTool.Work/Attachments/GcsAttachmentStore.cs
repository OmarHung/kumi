using System.IO.Pipelines;
using System.Net;
using Google;
using Google.Cloud.Storage.V1;

namespace PmTool.Work.Attachments;

/// <summary>
/// Google Cloud Storage 的一個 bucket，物件名稱就是附件的編號。認證用 Application Default Credentials
/// （Cloud Run 上是服務帳號；只需要這個 bucket 的 roles/storage.objectAdmin）。
/// 寫入用可續傳上傳：內容不會整個放進記憶體，只會暫存一個區塊。
/// </summary>
internal sealed class GcsAttachmentStore(StorageClient client, string bucket) : IAttachmentStore
{
    // 必須是 256 KiB 的倍數。一個區塊就是上傳時放在記憶體裡的量。
    private const int UploadChunkBytes = 4 * 1024 * 1024;

    public async Task<StoredFile?> WriteAsync(string key, Stream content, string contentType, long maxBytes, CancellationToken cancellationToken)
    {
        await using var measured = new MeasuringStream(content, maxBytes);
        try
        {
            await client.UploadObjectAsync(
                bucket, key, contentType, measured, new UploadObjectOptions { ChunkSize = UploadChunkBytes }, cancellationToken);
            return measured.Complete();
        }
        catch (Exception exception) when (IsTooLarge(exception))
        {
            // 可續傳上傳沒完成就不會有物件；還是刪一次，萬一最後一個區塊已經送出去了。
            await DeleteAsync(key, CancellationToken.None);
            return null;
        }
        catch
        {
            await DeleteAsync(key, CancellationToken.None);
            throw;
        }
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        // 先確認物件在：下載一開始就失敗的話，管線的另一頭才不會是一個讀不到東西的串流。
        try
        {
            await client.GetObjectAsync(bucket, key, cancellationToken: cancellationToken);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return PipedDownload.Start((destination, token) => client.DownloadObjectAsync(bucket, key, destination, cancellationToken: token));
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteObjectAsync(bucket, key, cancellationToken: cancellationToken);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // 本來就沒有。
        }
    }

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        // 列一筆就夠：確認 bucket 在、這個身分讀得到。不用 GetBucket：objectAdmin 沒有 storage.buckets.get。
        try
        {
            await client.ListObjectsAsync(bucket, options: new ListObjectsOptions { PageSize = 1 })
                .ReadPageAsync(1, cancellationToken);
        }
        catch (Exception exception) when (exception is GoogleApiException or InvalidOperationException or HttpRequestException)
        {
            throw new InvalidOperationException(
                $"{AttachmentSettings.BucketSetting} 指定的 bucket「{bucket}」用不了（{exception.Message}）："
                + "請確認 bucket 存在，而且服務帳號對它有 roles/storage.objectAdmin。",
                exception);
        }
    }

    private static bool IsTooLarge(Exception exception) =>
        exception is AttachmentTooLargeException || exception.InnerException is { } inner && IsTooLarge(inner);
}
