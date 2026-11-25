using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace PmTool.Identity.OAuth;

/// <summary>OAuth 請求裡文字參數的檢查，與範圍的剖析。</summary>
internal static class OAuthText
{
    /// <summary>
    /// 參數可以送進資料庫與網址：沒有 NUL 與控制字元（PostgreSQL 不收，沒擋就是 500），也不過長。
    /// null 與空字串都當作沒給。
    /// </summary>
    public static bool IsUsable([NotNullWhen(true)] string? value, int maxLength) =>
        !string.IsNullOrEmpty(value) && value.Length <= maxLength && !InputText.HasForbiddenCharacters(value);

    /// <summary>
    /// 用戶端自己報的名稱要顯示在同意頁：去掉控制字元，與能讓顯示和實際不同的字元（雙向控制、零寬、行與段落分隔、填充字、變體選擇符），
    /// 去頭尾空白。結果是空的、或超過上限都回 null。
    /// </summary>
    public static string? CleanName(string? name, int maxLength)
    {
        if (name is null)
        {
            return null;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || InputText.IsInvisibleFormatting(rune))
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length == 0 || cleaned.Length > maxLength || InputText.HasForbiddenCharacters(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// 空白分開的範圍。null、空白是「要全部」（OAuth 能發的全部，不含 agent:run）；不認得的、不能用 OAuth 要的略過。
    /// <paramref name="requestedAny"/> 為 true 但結果是空的，表示一個認得的都沒有。
    /// </summary>
    public static IReadOnlyList<AccessTokenScope> ParseScopes(string? scope, out bool requestedAny)
    {
        var names = (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        requestedAny = names.Length > 0;
        if (!requestedAny)
        {
            return AccessTokenScopes.Grantable;
        }

        // 照固定的順序輸出，同一組範圍不會因為請求的寫法不同而有不同的排列。
        return [.. AccessTokenScopes.Grantable.Where(candidate => names.Contains(AccessTokenScopes.NameOf(candidate), StringComparer.Ordinal))];
    }

    public static string FormatScopes(IEnumerable<AccessTokenScope> scopes) =>
        string.Join(' ', scopes.Select(AccessTokenScopes.NameOf));

    public static IReadOnlyList<AccessTokenScope> ScopesFromNames(IEnumerable<string> names) =>
        [.. names.Select(name => AccessTokenScopes.TryParse(name, out var scope) ? (AccessTokenScope?)scope : null).OfType<AccessTokenScope>()];
}
