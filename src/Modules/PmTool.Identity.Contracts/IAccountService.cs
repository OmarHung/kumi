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
    /// 成功後同一個帳號所有的工作階段（包括這一個）都作廢，換成一個新的（<see cref="PasswordChanged.Session"/>）：
    /// 密碼剛驗過，等於重新登入，絕對期限從現在重新算 <see cref="IdentityRules.SessionAbsoluteDays"/> 天。呼叫端要把新的權杖放進 Cookie。
    /// <paramref name="revokeAccessTokens"/> 為 true 時，這個帳號所有的個人存取權杖（命令列、本機代理、CI）一起撤銷；
    /// OAuth 授權與它們的權杖不受這個參數影響，一律撤銷。
    /// </summary>
    Task<Result<PasswordChanged>> ChangeOwnPasswordAsync(
        string sessionToken, string currentPassword, string newPassword, bool revokeAccessTokens, CancellationToken cancellationToken);
}

/// <param name="SignedOutSessions">因為改了密碼而登出的其他工作階段數（不含這一個）。</param>
/// <param name="RevokedAccessTokens">撤銷的個人存取權杖數；沒有要求撤銷時是 0。</param>
/// <param name="Session">換上的新工作階段，取代呼叫時用的那一個。</param>
public sealed record PasswordChanged(int SignedOutSessions, int RevokedAccessTokens, StartedSession Session);
