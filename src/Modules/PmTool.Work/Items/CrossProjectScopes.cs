using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>跨專案清單用到的專案資料：只取需要的欄位，不讀整列。</summary>
/// <param name="OwnOnly">呼叫者在這個專案只看得到自己負責或建立的項目。</param>
internal sealed record ScopedProject(Guid Id, string Key, long BlueprintVersion, bool OwnOnly);

/// <summary>
/// 跨專案的範圍：呼叫者看得到、沒有封存的專案（再用代號限縮）。「看得到」的規則和 <see cref="ReadableProjects"/> 同一份：
/// 組織管理員是全部，其他人是自己是成員、角色讀得到項目的。
/// </summary>
internal static class CrossProjectScopes
{
    private const string ProjectPath = "project";

    /// <summary>檢查代號的清單並轉成大寫。不合格式的代號當成找不到的專案，不算錯；清單太長或有控制字元是呼叫端給錯了。</summary>
    public static Result<IReadOnlyList<string>> NormalizeKeys(CrossProjectScope scope)
    {
        var keys = scope.ProjectKeys;
        if (keys is null)
        {
            return WorkFailures.QueryInvalid(ProjectPath, "專案代號的條件不能是 null；沒有條件時給空的清單。");
        }

        if (keys.Count > CrossProjectScope.MaxProjects)
        {
            return WorkFailures.QueryInvalid(ProjectPath, $"一次最多指定 {CrossProjectScope.MaxProjects} 個專案，現在有 {keys.Count} 個。");
        }

        if (keys.Any(key => key is null || TextRules.HasForbiddenCharacters(key, allowLineBreaks: false)))
        {
            return WorkFailures.QueryInvalid(ProjectPath, "專案代號不能是空值，也不能有換行、控制字元或無效的字元。");
        }

        return keys
            .Select(key => ProjectKeys.TryNormalize(key, out var normalized) ? normalized : null)
            .OfType<string>()
            .Distinct()
            .ToArray();
    }

    /// <param name="restrictedTo">已經轉成大寫的代號；空的是不限。</param>
    /// <param name="restricted">呼叫端有指定專案。指定了、但沒有一個是合格式的代號時，結果是空的，不是全部。</param>
    public static async Task<IReadOnlyList<ScopedProject>> ReadableAsync(
        WorkDbContext db,
        BlueprintCache cache,
        Actor actor,
        IReadOnlyList<string> restrictedTo,
        bool restricted,
        CancellationToken cancellationToken)
    {
        if (restricted && restrictedTo.Count == 0)
        {
            return [];
        }

        var projects = db.Projects.AsNoTracking().Where(p => p.OrganizationId == actor.OrganizationId && p.ArchivedAt == null);
        IReadOnlyDictionary<Guid, bool>? ownOnly = null;
        if (!actor.IsOrganizationAdmin)
        {
            ownOnly = await ReadableProjects.OwnOnlyByProjectAsync(db, cache, actor, cancellationToken);
            var readable = ownOnly.Keys.ToArray();
            projects = projects.Where(p => readable.Contains(p.Id));
        }

        if (restrictedTo.Count > 0)
        {
            var keys = restrictedTo.ToArray();
            projects = projects.Where(p => keys.Contains(p.Key));
        }

        var found = (await projects.Select(p => new { p.Id, p.Key, p.CurrentBlueprintVersion }).ToListAsync(cancellationToken))
            .Select(p => new ScopedProject(p.Id, p.Key, p.CurrentBlueprintVersion, ownOnly?.GetValueOrDefault(p.Id) ?? false))
            .ToList();

        // 照代號排，不受資料庫排序規則影響；名稱與專案清單的順序都由它決定。
        return [.. found.OrderBy(p => p.Key, StringComparer.Ordinal)];
    }
}
