using Microsoft.EntityFrameworkCore;
using PmTool.Knowledge.Persistence;
using PmTool.Storage;

namespace PmTool.Knowledge.Attachments;

/// <summary>
/// 頁面附件占的儲存空間，給 <see cref="IStorageQuota"/> 加總。只讀已經提交的資料，用自己的連線：
/// 垃圾桶裡的頁面的附件還在儲存空間裡，照算。
/// </summary>
internal sealed class PageAttachmentUsage(IDbContextFactory<KnowledgeDbContext> contexts) : IStorageUsageSource
{
    public StorageArea Area => StorageArea.KnowledgeAttachments;

    public async Task<StorageAreaUsage> UsageAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = db.PageAttachments.AsNoTracking().Where(a => a.OrganizationId == organizationId);
        var bytes = await rows.SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
        var files = await rows.LongCountAsync(cancellationToken);
        return new StorageAreaUsage(Area, bytes, files);
    }

    public async Task<long> UploadedBytesSinceAsync(
        Guid organizationId, Guid personId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.PageAttachments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.UploadedBy == personId && a.UploadedAt >= since)
            .SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
    }
}
