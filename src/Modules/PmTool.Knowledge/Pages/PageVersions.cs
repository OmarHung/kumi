using Microsoft.EntityFrameworkCore;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Pages;

/// <summary>
/// 版本就是內容的歷史：每次存檔寫一版，同一個人在 <see cref="KnowledgeRules.VersionMergeWindow"/> 內連續存檔併成同一版
/// （寫文件的人會一直按儲存，版本清單要看得出「誰在什麼時候改了一輪」）。還原出來的版本不併：下一次存檔另開一版。
/// </summary>
internal static class PageVersions
{
    /// <summary>新增頁面時的第 1 版。</summary>
    public static void AddFirst(KnowledgeDbContext db, Guid organizationId, Guid pageId, string title, string body, Guid author, DateTimeOffset now) =>
        db.PageVersions.Add(NewVersion(organizationId, pageId, 1, title, body, author, now));

    /// <summary>存檔寫版本：併進最新的一版，或新增一版；超過上限時刪掉最舊的。呼叫端在交易裡，並且已經鎖住那一頁。</summary>
    public static async Task RecordAsync(
        KnowledgeDbContext db,
        Guid organizationId,
        Guid pageId,
        string title,
        string body,
        Guid author,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var latest = await db.PageVersions.AsNoTracking()
            .Where(v => v.PageId == pageId)
            .OrderByDescending(v => v.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null && latest.AuthorId == author && latest.RestoredFrom is null && now - latest.SavedAt <= KnowledgeRules.VersionMergeWindow)
        {
            await db.PageVersions.Where(v => v.Id == latest.Id)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(v => v.Title, title).SetProperty(v => v.Body, body).SetProperty(v => v.SavedAt, now),
                    cancellationToken);
            return;
        }

        var number = (latest?.Number ?? 0) + 1;
        db.PageVersions.Add(NewVersion(organizationId, pageId, number, title, body, author, now));
        await db.SaveChangesAsync(cancellationToken);

        // 版本的編號只增不減、只從最舊的刪，所以保留的永遠是最新的 MaxVersionsPerPage 個。
        var oldest = number - KnowledgeRules.MaxVersionsPerPage;
        if (oldest > 0)
        {
            await db.PageVersions.Where(v => v.PageId == pageId && v.Number <= oldest).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static PageVersionRow NewVersion(
        Guid organizationId, Guid pageId, int number, string title, string body, Guid author, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            OrganizationId = organizationId,
            PageId = pageId,
            Number = number,
            Title = title,
            Body = body,
            AuthorId = author,
            StartedAt = now,
            SavedAt = now,
        };
}
