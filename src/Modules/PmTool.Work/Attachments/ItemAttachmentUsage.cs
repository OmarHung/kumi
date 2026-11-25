using Microsoft.EntityFrameworkCore;
using PmTool.Storage;
using PmTool.Work.History;
using PmTool.Work.Persistence;

namespace PmTool.Work.Attachments;

/// <summary>
/// 項目附件占的儲存空間，給 <see cref="IStorageQuota"/> 加總。只讀已經提交的資料，用自己的連線：
/// 刪除的項目的附件檔案還在儲存空間裡，照算。
/// 「每人每日」看的是這一列的 <c>uploaded_by</c> 與 <c>uploaded_at</c>；歷史匯入記的是原本的人與過去的時間，
/// 所以那一條路不算每日（<see cref="IStorageQuota.CheckAsync"/> 的 personId 給 null）；
/// 加總每日量時也要略過這些附件（<c>history_refs</c> 記著它們），否則那個人沒傳過東西，額度卻被別人的匯入用掉。
/// </summary>
internal sealed class ItemAttachmentUsage(IDbContextFactory<WorkDbContext> contexts) : IStorageUsageSource
{
    public StorageArea Area => StorageArea.ItemAttachments;

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
        var importedKind = HistoryRules.KindText(HistoryRefKind.Attachment);
        return await db.Attachments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.UploadedBy == personId && a.UploadedAt >= since
                && !db.HistoryRefs.Any(r => r.OrganizationId == organizationId && r.Kind == importedKind && r.TargetId == a.Id))
            .SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
    }
}
