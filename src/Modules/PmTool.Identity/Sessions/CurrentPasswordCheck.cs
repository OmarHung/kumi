using Microsoft.AspNetCore.Identity;
using PmTool.Abstractions;

namespace PmTool.Identity.Sessions;

/// <summary>「目前的密碼」的比對與失敗的樣子。改密碼與登出其他裝置都要先確認它，兩邊的規則與訊息才只有一份。</summary>
internal static class CurrentPasswordCheck
{
    public static readonly FailureDetail Incorrect =
        new(IdentityErrorCodes.PasswordIncorrect, PersonInput.CurrentPasswordPath, "目前的密碼不正確。");

    /// <summary>太長的不拿去算雜湊（成本跟著長度走）；存著的密碼不可能那麼長，所以一定不對。</summary>
    public static bool Matches(IPasswordHasher<PasswordCredential> hasher, string storedHash, string currentPassword) =>
        !PersonInput.IsPasswordTooLong(currentPassword)
        && hasher.VerifyHashedPassword(PasswordCredential.Instance, storedHash, currentPassword) != PasswordVerificationResult.Failed;
}
