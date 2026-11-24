using PmTool.Abstractions;

namespace PmTool.Identity;

/// <summary>檢查過、要存進資料庫的顯示名稱與 Email。</summary>
internal sealed record CheckedPerson(string DisplayName, string Email);

/// <summary>新增人員與第一個擁有者共用的檢查，兩邊的規則才不會分岔。</summary>
internal static class PersonInput
{
    /// <summary>細項的路徑用 HTTP 內文的屬性名稱，網頁才對得到欄位。</summary>
    public const string DisplayNamePath = "displayName";

    public const string EmailPath = "email";

    public const string PasswordPath = "password";

    public const string NewPasswordPath = "newPassword";

    public const string CurrentPasswordPath = "currentPassword";

    private static readonly FailureDetail InvalidEmail = new(IdentityErrorCodes.EmailInvalid, EmailPath, "Email 的格式不正確。");

    /// <summary>去掉顯示名稱頭尾的空白、正規化 Email，再一次列出每一個欄位的問題，網頁才能一次標出來。</summary>
    public static Result<CheckedPerson> Check(string displayName, string email, string password)
    {
        var trimmedName = displayName.Trim();
        FailureDetail?[] found =
        [
            CheckDisplayName(trimmedName),
            EmailAddress.TryNormalize(email, out var normalizedEmail) ? null : InvalidEmail,
            CheckPassword(password, PasswordPath),
        ];
        IReadOnlyList<FailureDetail> issues = [.. found.OfType<FailureDetail>()];

        if (issues.Count == 0 && normalizedEmail is not null)
        {
            return new CheckedPerson(trimmedName, normalizedEmail);
        }

        return Invalid(issues);
    }

    /// <summary>密碼太長不能拿去算雜湊：雜湊的成本跟著長度走，等於讓人用很長的密碼拖垮伺服器。</summary>
    public static bool IsPasswordTooLong(string password) => password.Length > IdentityRules.MaxPasswordLength;

    private static FailureDetail? CheckDisplayName(string displayName)
    {
        if (displayName.Length == 0)
        {
            return new(IdentityErrorCodes.NameRequired, DisplayNamePath, "請填寫顯示名稱。");
        }

        if (displayName.Length > IdentityRules.MaxDisplayNameLength)
        {
            return new(IdentityErrorCodes.NameTooLong, DisplayNamePath, $"顯示名稱不能超過 {IdentityRules.MaxDisplayNameLength} 個字。");
        }

        // 合約沒有「名稱有不能用的字元」的代碼；這種名稱等於沒有可用的名稱，和工作引擎一樣歸在 name_required。
        return InputText.HasForbiddenCharacters(displayName)
            ? new(IdentityErrorCodes.NameRequired, DisplayNamePath, "顯示名稱不能有換行、控制字元或無效的字元。")
            : null;
    }

    /// <summary>
    /// 新增人員、改自己的密碼、重設別人的密碼共用的長度規則。<paramref name="path"/> 是那個請求裡密碼欄位的名稱，
    /// 各自不同（<c>password</c>、<c>newPassword</c>），網頁才標得到欄位。
    /// </summary>
    public static FailureDetail? CheckPassword(string password, string path)
    {
        if (password.Length < IdentityRules.MinPasswordLength)
        {
            return new(IdentityErrorCodes.PasswordTooShort, path, $"密碼至少要 {IdentityRules.MinPasswordLength} 個字元。");
        }

        return IsPasswordTooLong(password)
            ? new(IdentityErrorCodes.PasswordTooLong, path, $"密碼不能超過 {IdentityRules.MaxPasswordLength} 個字元。")
            : null;
    }

    /// <summary>只有一個欄位有問題時也用細項回報，和新增人員一樣，網頁只要認一種寫法。</summary>
    public static Failure Invalid(IReadOnlyList<FailureDetail> issues) =>
        new(FailureKind.Invalid, issues[0].Code, issues[0].Message) { Details = issues };
}
