using System.Security.Cryptography;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>到哪裡下載這件工作的附件：本機代理與主機的 worker 端點、憑證不同。</summary>
internal interface IAttachmentSource
{
    Task<DownloadStream> OpenAsync(Guid jobId, Guid attachmentId, CancellationToken cancellation);
}

/// <summary>本機代理：<c>GET /v1/agent/jobs/{jobId}/attachments/{attachmentId}</c>，同一把權杖。</summary>
internal sealed class AgentAttachmentSource(ApiClient api) : IAttachmentSource
{
    public Task<DownloadStream> OpenAsync(Guid jobId, Guid attachmentId, CancellationToken cancellation) =>
        api.OpenDownloadAsync($"agent/jobs/{jobId}/attachments/{attachmentId}", cancellation);
}

/// <summary>主機的 worker：機器憑證與 <c>X-Kumi-Machine</c>（api 已經帶），租約放在網址上，和回報端點的租約同一個。</summary>
internal sealed class HostAttachmentSource(ApiClient api, int lease) : IAttachmentSource
{
    public Task<DownloadStream> OpenAsync(Guid jobId, Guid attachmentId, CancellationToken cancellation) =>
        api.OpenDownloadAsync($"host-agent/jobs/{jobId}/attachments/{attachmentId}?lease={lease}", cancellation);
}

/// <summary>下載或核對附件失敗；訊息可以直接給使用者看（不含伺服器給的檔名、路徑）。</summary>
internal sealed class AttachmentStagingException(string message) : Exception(message);

/// <summary>把工作的附件下載到暫存資料夾，邊讀邊算 SHA-256，大小與雜湊都對得上才算成功。</summary>
internal static class AttachmentStaging
{
    private const int BufferSize = 81_920;

    public static async Task<IReadOnlyList<StagedAttachment>> StageAsync(
        AgentJob job, string directory, IAttachmentSource? source, CancellationToken cancellation)
    {
        if (job.Attachments.Count == 0)
        {
            return [];
        }

        if (source is null)
        {
            throw new AttachmentStagingException("下載附件失敗：這個執行環境沒有附件的下載來源。");
        }

        var staged = new List<StagedAttachment>();
        for (var index = 0; index < job.Attachments.Count; index++)
        {
            var attachment = job.Attachments[index];
            var path = Path.Combine(directory, attachment.LocalFileName(index + 1));
            await DownloadOneAsync(job.JobId, attachment, path, index + 1, source, cancellation);
            staged.Add(new StagedAttachment(path, attachment.ContentType, attachment.Kind));
        }

        return staged;
    }

    private static async Task DownloadOneAsync(
        Guid jobId, AgentAttachment attachment, string path, int number, IAttachmentSource source, CancellationToken cancellation)
    {
        try
        {
            using var download = await source.OpenAsync(jobId, attachment.Id, cancellation);
            var (size, hash) = await WriteAsync(download.Content, path, attachment.SizeBytes, cancellation);
            if (size != attachment.SizeBytes)
            {
                throw new AttachmentStagingException($"下載附件失敗：第 {number} 個附件的大小和伺服器說的不一樣。");
            }

            if (!string.Equals(hash, attachment.Sha256, StringComparison.Ordinal))
            {
                throw new AttachmentStagingException($"下載附件失敗：第 {number} 個附件的內容和伺服器說的不一樣（SHA-256 不符）。");
            }
        }
        catch (CliException exception)
        {
            // 訊息是整理過的（連不上、404 的原因），不含權杖；檔名不放進去。
            throw new AttachmentStagingException($"下載附件失敗：第 {number} 個附件：{exception.Message}");
        }
        catch (IOException)
        {
            throw new AttachmentStagingException($"下載附件失敗：第 {number} 個附件寫不進暫存資料夾。");
        }
    }

    /// <summary>寫到 <paramref name="path"/>；超過宣告的大小就停（伺服器多送、被換掉的回應都不能塞滿磁碟）。回實際的大小與小寫十六進位雜湊。</summary>
    private static async Task<(long Size, string Hash)> WriteAsync(Stream content, string path, long limit, CancellationToken cancellation)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long total = 0;
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        int read;
        while ((read = await content.ReadAsync(buffer, cancellation)) > 0)
        {
            total += read;
            if (total > limit)
            {
                break;
            }

            hasher.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
        }

        return (total, Convert.ToHexStringLower(hasher.GetHashAndReset()));
    }
}
