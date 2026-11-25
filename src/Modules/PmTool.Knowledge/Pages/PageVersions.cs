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

    /// <summary>
    /// 存檔寫版本：併進最新的一版，或新增一版；超過上限時依 <see cref="Trim"/> 刪掉幾版。呼叫端在交易裡，並且已經鎖住那一頁。
    /// <paramref name="zone"/> 是組織的時區：保留每一天的第一版與最後一版要依組織的日子算。
    /// </summary>
    public static async Task RecordAsync(
        KnowledgeDbContext db,
        TimeZoneInfo zone,
        Guid organizationId,
        Guid pageId,
        string title,
        string body,
        Guid author,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var latest = await db.PageVersions.AsNoTracking()
            .Where(v => v.OrganizationId == organizationId && v.PageId == pageId)
            .OrderByDescending(v => v.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null && latest.AuthorId == author && latest.RestoredFrom is null && now - latest.SavedAt <= KnowledgeRules.VersionMergeWindow)
        {
            await db.PageVersions.Where(v => v.OrganizationId == organizationId && v.Id == latest.Id)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(v => v.Title, title).SetProperty(v => v.Body, body).SetProperty(v => v.SavedAt, now),
                    cancellationToken);
            return;
        }

        var number = (latest?.Number ?? 0) + 1;
        db.PageVersions.Add(NewVersion(organizationId, pageId, number, title, body, author, now));
        await db.SaveChangesAsync(cancellationToken);
        await TrimAsync(db, zone, organizationId, pageId, cancellationToken);
    }

    /// <summary>
    /// 版本超過 <see cref="KnowledgeRules.MaxVersionsPerPage"/> 時刪掉多出來的。第 1 版、最新的一版、組織時區每一天裡最早與最晚的一版受保護：
    /// 先刪最舊的沒受保護的；只剩受保護的時，從最舊的那一天開始刪「當天最早的一版」，再不夠才刪「當天最晚的一版」；
    /// 第 1 版與最新的一版一直留著。兩個帳號輪流存檔，只洗得掉當天的中間版本，以前每一天開頭與結尾的樣子都在。
    /// </summary>
    private static async Task TrimAsync(
        KnowledgeDbContext db, TimeZoneInfo zone, Guid organizationId, Guid pageId, CancellationToken cancellationToken)
    {
        var versions = await db.PageVersions.AsNoTracking()
            .Where(v => v.OrganizationId == organizationId && v.PageId == pageId)
            .OrderBy(v => v.Number)
            .Select(v => new VersionStamp(v.Id, v.Number, v.StartedAt))
            .Take(KnowledgeRules.MaxVersionsPerPage * 2)
            .ToListAsync(cancellationToken);
        var surplus = versions.Count - KnowledgeRules.MaxVersionsPerPage;
        if (surplus <= 0)
        {
            return;
        }

        var doomed = Trim(versions, zone, surplus);
        await db.PageVersions.Where(v => v.OrganizationId == organizationId && v.PageId == pageId && doomed.Contains(v.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>要刪的 <paramref name="surplus"/> 個版本的編號；<paramref name="versions"/> 依版本號由小到大。</summary>
    internal static IReadOnlyList<Guid> Trim(IReadOnlyList<VersionStamp> versions, TimeZoneInfo zone, int surplus)
    {
        var alwaysKept = new HashSet<Guid> { versions[0].Id, versions[^1].Id };
        var days = versions
            .GroupBy(v => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(v.StartedAt, zone).DateTime))
            .ToList();
        var earliest = days.Select(day => day.OrderBy(v => v.StartedAt).ThenBy(v => v.Number).First()).ToList();
        var latest = days.Select(day => day.OrderByDescending(v => v.StartedAt).ThenByDescending(v => v.Number).First()).ToList();
        var edgeIds = earliest.Concat(latest).Select(v => v.Id).ToHashSet();
        var earliestIds = earliest.Select(v => v.Id).ToHashSet();
        var plain = versions.Where(v => !alwaysKept.Contains(v.Id) && !edgeIds.Contains(v.Id));
        var firstsOfDays = earliest.Where(v => !alwaysKept.Contains(v.Id)).OrderBy(v => v.StartedAt).ThenBy(v => v.Number);
        var lastsOfDays = latest.Where(v => !alwaysKept.Contains(v.Id) && !earliestIds.Contains(v.Id)).OrderBy(v => v.StartedAt).ThenBy(v => v.Number);
        return [.. plain.Concat(firstsOfDays).Concat(lastsOfDays).Take(surplus).Select(v => v.Id)];
    }

    internal sealed record VersionStamp(Guid Id, int Number, DateTimeOffset StartedAt);

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
