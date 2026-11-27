using Microsoft.EntityFrameworkCore;
using PmTool.Identity;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications;

/// <summary>收件匣清單的分類與搜尋。分類與搜尋疊在 <see cref="NotificationVisibility"/> 之後，所以看不到的專案不會因為搜尋而出現。</summary>
internal static class NotificationFilters
{
    private const string LikeEscape = "\\";

    /// <summary>去掉前後空白；空白是 null（不搜尋）。長度與字元不對回 null 與說明。</summary>
    public static (string? Text, string? Problem) NormalizeSearch(string? search)
    {
        var text = search?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (null, null);
        }

        if (text.Length > NotificationRules.MaxSearchLength)
        {
            return (null, $"搜尋的文字最多 {NotificationRules.MaxSearchLength} 個字。");
        }

        // PostgreSQL 不收 NUL，控制字元也沒有搜尋的意義。
        if (text.Any(char.IsControl))
        {
            return (null, "搜尋的文字不能有控制字元。");
        }

        // 落單的代理字元不是合法的 UTF-16，送進資料庫前先擋下，不要變成 500。
        return HasLoneSurrogate(text) ? (null, "搜尋的文字有不正確的字元。") : (text, null);
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static IQueryable<NotificationRow> ByCategory(IQueryable<NotificationRow> notifications, NotificationCategory category) => category switch
    {
        NotificationCategory.Mentions => notifications.Where(n => n.Kind == NotificationKind.Mentioned || n.Kind == NotificationKind.Replied),
        NotificationCategory.Assigned => notifications.Where(n => n.Kind == NotificationKind.Assigned),
        _ => notifications,
    };

    /// <summary>文字欄位用 ILIKE，做這件事的人用顯示名稱找出編號再比對；兩邊是 OR。</summary>
    public static async Task<IQueryable<NotificationRow>> BySearchAsync(
        IQueryable<NotificationRow> notifications, IPeopleDirectory people, Guid organizationId, string text, CancellationToken cancellationToken)
    {
        var pattern = "%" + EscapeLike(text) + "%";
        var actors = (await people.ListAsync(organizationId, cancellationToken))
            .Where(p => p.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.PersonId)
            .ToArray();
        return notifications.Where(n =>
            (n.ItemKey != null && EF.Functions.ILike(n.ItemKey, pattern, LikeEscape))
            || (n.ItemTitle != null && EF.Functions.ILike(n.ItemTitle, pattern, LikeEscape))
            || (n.ProjectKey != null && EF.Functions.ILike(n.ProjectKey, pattern, LikeEscape))
            || (n.Excerpt != null && EF.Functions.ILike(n.Excerpt, pattern, LikeEscape))
            || (n.FromStateName != null && EF.Functions.ILike(n.FromStateName, pattern, LikeEscape))
            || (n.ToStateName != null && EF.Functions.ILike(n.ToStateName, pattern, LikeEscape))
            || actors.Contains(n.ActorPersonId));
    }

    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
