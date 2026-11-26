using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;
using PmTool.Storage;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 儲存空間用量的 advisory lock，和項目、知識庫的附件共用同一把（名稱由 <see cref="StorageQuotaRules.LockNameOf"/> 決定）。
/// 一定是交易裡拿的最後一把：其他的鎖、確認都做完才拿，所以不會和別的鎖形成環。
/// </summary>
internal static class StorageLocks
{
    public static Task OrganizationAsync(AiDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({StorageQuotaRules.LockNameOf(organizationId)}, 0))", cancellationToken);
}
