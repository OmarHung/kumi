using Microsoft.EntityFrameworkCore;
using PmTool.Storage;
using PmTool.Work.Persistence;

namespace PmTool.Work.Attachments;

/// <summary>
/// 儲存空間用量的 advisory lock。項目與知識庫拿同一把（名稱由 <see cref="StorageQuotaRules.LockNameOf"/> 決定），
/// 兩邊的上傳才排在同一個隊伍。它一定是交易裡拿的最後一把鎖：鎖住項目那一列、原本的確認都做完才拿，
/// 沒有任何路拿了它之後還去等別的鎖，所以不會形成環。
/// </summary>
internal static class StorageLocks
{
    public static Task OrganizationAsync(WorkDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({StorageQuotaRules.LockNameOf(organizationId)}, 0))", cancellationToken);
}
