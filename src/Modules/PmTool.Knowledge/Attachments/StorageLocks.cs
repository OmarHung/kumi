using Microsoft.EntityFrameworkCore;
using PmTool.Knowledge.Persistence;
using PmTool.Storage;

namespace PmTool.Knowledge.Attachments;

/// <summary>
/// 儲存空間用量的 advisory lock，和項目的附件共用同一把（名稱由 <see cref="StorageQuotaRules.LockNameOf"/> 決定）。
/// 一定是交易裡拿的最後一把：頁面那一列、原本的確認都做完才拿，所以不會和別的鎖形成環。
/// </summary>
internal static class StorageLocks
{
    public static Task OrganizationAsync(KnowledgeDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({StorageQuotaRules.LockNameOf(organizationId)}, 0))", cancellationToken);
}
