using System.Globalization;
using System.Text;
using PmTool.Abstractions;

namespace PmTool.Identity.AccessTokens;

/// <summary>建立權杖的欄位檢查。一次列出每個欄位的問題，網頁才能一起標出來。</summary>
internal static class AccessTokenInput
{
    /// <summary>細項的路徑用 HTTP 內文的屬性名稱，網頁才對得到欄位。</summary>
    public const string NamePath = "name";

    public const string ScopesPath = "scopes";

    public const string ExpiresInDaysPath = "expiresInDays";

    /// <summary>通過時回傳去掉頭尾空白的名稱。</summary>
    public static Result<string> Check(NewAccessToken token)
    {
        var name = token.Name.Trim();
        FailureDetail?[] found = [CheckName(name), CheckScopes(token.Scopes), CheckExpiry(token.ExpiresInDays)];
        IReadOnlyList<FailureDetail> issues = [.. found.OfType<FailureDetail>()];
        if (issues.Count == 0)
        {
            return name;
        }

        return PersonInput.Invalid(issues);
    }

    private static FailureDetail? CheckName(string name)
    {
        if (name.Length == 0)
        {
            return new(IdentityErrorCodes.AccessTokenNameRequired, NamePath, "請填寫權杖的名稱。");
        }

        if (name.Length > AccessTokenRules.MaxNameLength)
        {
            return new(IdentityErrorCodes.AccessTokenNameTooLong, NamePath, $"權杖的名稱不能超過 {AccessTokenRules.MaxNameLength} 個字。");
        }

        // 合約沒有「名稱有不能用的字元」的代碼；這種名稱等於沒有可用的名稱，歸在 name_required。
        return InputText.HasForbiddenCharacters(name) || HasInvisibleFormatting(name)
            ? new(IdentityErrorCodes.AccessTokenNameRequired, NamePath, "權杖的名稱不能有換行、控制字元或無效的字元。")
            : null;
    }

    /// <summary>
    /// 雙向控制、零寬字元（Format）與行、段落分隔字元：不是控制字元，卻能讓名稱在清單裡顯示得和實際不同，
    /// 或把後面的文字藏起來。
    /// </summary>
    internal static bool HasInvisibleFormatting(string name) =>
        name.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune)
            is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    private static FailureDetail? CheckScopes(IReadOnlyList<AccessTokenScope> scopes)
    {
        if (scopes.Count == 0)
        {
            return new(IdentityErrorCodes.AccessTokenScopesRequired, ScopesPath, "至少要選一個範圍。");
        }

        return scopes.Distinct().Count() != scopes.Count || scopes.Any(scope => !Enum.IsDefined(scope))
            ? new(IdentityErrorCodes.AccessTokenScopesRequired, ScopesPath, "範圍不能重複，也不能有不認得的範圍。")
            : null;
    }

    internal static FailureDetail? CheckExpiry(int? days) =>
        days is null or (>= 1 and <= AccessTokenRules.MaxExpiresInDays)
            ? null
            : new(IdentityErrorCodes.AccessTokenExpiryInvalid, ExpiresInDaysPath, $"期限要是 1 到 {AccessTokenRules.MaxExpiresInDays} 天，或不過期。");
}
