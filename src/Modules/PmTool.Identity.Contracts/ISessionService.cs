namespace PmTool.Identity;

/// <summary>
/// 伺服器端的工作階段。登入時開一筆，Cookie 裡只帶權杖；每個請求都拿權杖來問一次，
/// 所以登出、改密碼、停用帳號作廢的工作階段，下一個請求就失效，不必等 Cookie 過期。
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// 用 Email 與密碼登入，並替這個帳號開一個工作階段。Email 不存在、密碼不對、帳號停用都回傳 null，
    /// 花的時間差不多（理由見 <see cref="IAuthenticationService.SignInAsync"/>）。
    /// 驗過的密碼綁在開工作階段的那一刻：驗證的同時密碼被改掉、被重設或帳號被停用，就不開，一樣回 null；
    /// 否則拿舊密碼登入的人，會在那些操作作廢所有工作階段之後，還多出一個新的。
    /// 回傳的權杖只在這時候拿得到明文（資料庫只存雜湊），呼叫端把它放進 Cookie，之後不要記在任何地方。
    /// </summary>
    Task<SignedIn?> SignInAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>
    /// 依權杖找還有效的工作階段。權杖不對、已作廢、閒置超過 <see cref="IdentityRules.SessionIdleHours"/>、
    /// 超過 <see cref="IdentityRules.SessionAbsoluteDays"/>、帳號已停用，都回傳 null。
    /// 有效時記下這次使用的時刻（閒置期限從這裡算），並回傳帳號現在的資料（名稱與角色可能已經改了）。
    /// 結果在程序裡保留最多 <see cref="IdentityRules.SessionCacheSeconds"/> 秒；同一個程序裡作廢的工作階段立刻失效。
    /// </summary>
    Task<ActiveSession?> FindAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// 只回答這個工作階段現在還有沒有效，不更新最後使用時間（不續命）：沒有作廢、沒有閒置過期、沒有超過絕對期限、帳號沒有停用。
    /// 和 <see cref="FindAsync"/> 同一個定義，但每次都問資料庫、不用快取。給長時間開著的即時連線定期確認用：
    /// 用 FindAsync 的話，開著不動的分頁會一直把閒置期限延下去，永遠不會失效。權杖不對一樣回 false。
    /// </summary>
    Task<bool> IsActiveAsync(string token, CancellationToken cancellationToken);

    /// <summary>登出：作廢這個工作階段。權杖不對或已經作廢都當作成功，結果一樣是登出了。</summary>
    Task EndAsync(string token, CancellationToken cancellationToken);

    /// <summary>作廢同一個帳號的其他工作階段，這一個留著。回傳作廢了幾個；權杖無效時是 0。</summary>
    Task<int> EndOthersAsync(string token, CancellationToken cancellationToken);
}

/// <summary>剛登入的帳號與替它開的工作階段。</summary>
public sealed record SignedIn(AuthenticatedUser User, StartedSession Session);

/// <summary>剛開的工作階段。<paramref name="Token"/> 是明文，只在這裡出現一次。</summary>
public sealed record StartedSession(Guid SessionId, string Token, DateTimeOffset ExpiresAt);

/// <summary>還有效的工作階段與帳號現在的資料。</summary>
public sealed record ActiveSession(Guid SessionId, AuthenticatedUser User, DateTimeOffset ExpiresAt);
