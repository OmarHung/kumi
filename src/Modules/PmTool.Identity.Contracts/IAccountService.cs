using PmTool.Abstractions;

namespace PmTool.Identity;

/// <summary>自己的帳號。用工作階段的權杖認人：它同時說明了是哪個帳號，以及改完之後要留下哪一個工作階段。</summary>
public interface IAccountService
{
    /// <summary>
    /// 改自己的密碼。目前的密碼不對是 <see cref="IdentityErrorCodes.PasswordIncorrect"/>（細項路徑 <c>currentPassword</c>）；
    /// 新密碼太短、太長是 <see cref="IdentityErrorCodes.PasswordTooShort"/>、<see cref="IdentityErrorCodes.PasswordTooLong"/>（細項路徑 <c>newPassword</c>）。
    /// 這個工作階段已經無效時是 <see cref="IdentityErrorCodes.SessionEnded"/>：包括改的同時它被作廢
    /// （別的裝置按了「登出其他裝置」）、密碼被別人重設或改掉、帳號被停用；這時什麼都不改。
    /// 成功後同一個帳號的其他工作階段全部作廢，這一個留著。
    /// </summary>
    Task<Result<PasswordChanged>> ChangeOwnPasswordAsync(
        string sessionToken, string currentPassword, string newPassword, CancellationToken cancellationToken);
}

/// <param name="SignedOutSessions">因為改了密碼而作廢的其他工作階段數。</param>
public sealed record PasswordChanged(int SignedOutSessions);
