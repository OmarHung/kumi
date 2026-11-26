using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;
using PmTool.Storage;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 助理附件占的儲存空間，給 <see cref="IStorageQuota"/> 加總。全部資料列都算（待送與孤兒的檔案也在儲存空間裡，
/// 等維護清掉、資料列刪了才放掉）。只讀已經提交的資料，用自己的連線。
/// </summary>
internal sealed class AssistantAttachmentUsage(IDbContextFactory<AiDbContext> contexts) : IStorageUsageSource
{
    public StorageArea Area => StorageArea.AssistantAttachments;

    public async Task<StorageAreaUsage> UsageAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = db.Attachments.AsNoTracking().Where(a => a.OrganizationId == organizationId);
        var bytes = await rows.SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
        var files = await rows.LongCountAsync(cancellationToken);
        return new StorageAreaUsage(Area, bytes, files);
    }

    public async Task<long> UploadedBytesSinceAsync(
        Guid organizationId, Guid personId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Attachments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.PersonId == personId && a.UploadedAt >= since)
            .SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
    }
}
