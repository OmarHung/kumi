namespace PmTool.Identity.Sessions;

/// <summary>工作階段為什麼作廢。存在資料表裡，查問題時看得出是誰、因為什麼讓它失效。</summary>
internal static class SessionEndReasons
{
    public const string SignedOut = "signed_out";

    public const string OtherSessions = "other_sessions";

    public const string PasswordChanged = "password_changed";

    public const string PasswordReset = "password_reset";

    /// <summary>自己撤銷了一把存取權杖（只用在權杖）。</summary>
    public const string TokenRevoked = "token_revoked";

    /// <summary>使用者（或用戶端）撤銷了一筆 OAuth 授權；它的權杖一起結束。</summary>
    public const string OAuthGrantRevoked = "oauth_grant_revoked";

    /// <summary>用過的授權碼或更新權杖又出現，當作外流，撤銷它發出的權杖。</summary>
    public const string OAuthReuseDetected = "oauth_reuse_detected";

    public const string AccountDisabled = "account_disabled";

    /// <summary>改密碼、停用、管理員重設密碼時，這個帳號所有的 OAuth 授權一起結束（授權上記的原因）。</summary>
    public const string OAuthPasswordChanged = "oauth_password_changed";

    public const string OAuthPasswordReset = "oauth_password_reset";

    /// <summary>重新授權時縮小了範圍，超過新範圍的存取權杖結束。</summary>
    public const string OAuthScopeNarrowed = "oauth_scope_narrowed";

    public const string OAuthAccountDisabled = "oauth_account_disabled";

    /// <summary>
    /// 資料庫換回某份備份之後，還有效的工作階段全部作廢（維運的還原腳本直接用 SQL 寫，程式裡沒有用到）：
    /// 備份裡的工作階段可能是之後已經登出、改過密碼的。記在這裡，原因只有一份清單。
    /// </summary>
    public const string Restored = "restored";
}
