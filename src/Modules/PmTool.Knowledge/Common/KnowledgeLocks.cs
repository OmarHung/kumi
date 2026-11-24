using Microsoft.EntityFrameworkCore;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Common;

/// <summary>
/// 知識庫的 advisory lock。鍵加上自己的前綴，和別的模組的鎖錯開；交易結束就自動放開，所以只在交易裡有意義。
/// </summary>
internal static class KnowledgeLocks
{
    /// <summary>建立空間排隊：上限、代號與「一個專案一個空間」不會被同時的寫入繞過。</summary>
    public static Task OrganizationAsync(KnowledgeDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"knowledge_org:" + organizationId}, 0))", cancellationToken);

    /// <summary>同一個空間的樹狀修改（新增、搬移、刪除、復原、改限制）排隊：位置的重新編號與編號的配發不會互相踩。</summary>
    public static Task SpaceAsync(KnowledgeDbContext db, Guid spaceId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"knowledge_space:" + spaceId}, 0))", cancellationToken);

    /// <summary>改內容鎖住那一頁：同時存檔的兩個人，後到的看到的是先到的結果。</summary>
    public static Task PageAsync(KnowledgeDbContext db, Guid organizationId, Guid pageId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM knowledge.pages WHERE organization_id = {organizationId} AND id = {pageId} FOR UPDATE", cancellationToken);
}
