namespace PmTool.Identity;

/// <summary>身分模組回報的失敗代碼。這些字串是對外合約，改名等於破壞相容性。</summary>
public static class IdentityErrorCodes
{
    public const string Forbidden = "person.forbidden";
    public const string NameRequired = "person.name_required";
    public const string NameTooLong = "person.name_too_long";
    public const string EmailInvalid = "person.email_invalid";
    public const string EmailTaken = "person.email_taken";
    public const string PasswordTooShort = "person.password_too_short";
    public const string PasswordTooLong = "person.password_too_long";

    /// <summary>這個組織裡沒有這個人（NotFound）。和 API 層的 <c>person.not_found</c> 是同一個字串。</summary>
    public const string NotFound = "person.not_found";

    /// <summary>不能對自己的帳號做這件事：停用自己、用管理員的方式重設自己的密碼（Conflict）。</summary>
    public const string IsSelf = "person.is_self";

    /// <summary>這個人沒有登入帳號，沒有東西可以停用或重設（Conflict）。</summary>
    public const string NoAccount = "person.no_account";

    /// <summary>改自己的密碼時，目前的密碼不對（Invalid）。</summary>
    public const string PasswordIncorrect = "account.password_incorrect";

    /// <summary>
    /// 用來認人的工作階段已經無效（Forbidden）。API 把它轉成 401 <c>auth.required</c>，網頁就回登入頁；
    /// 這個字串不會照原樣出現在回應裡。
    /// </summary>
    public const string SessionEnded = "session.ended";

    /// <summary>權杖的名稱是空的、只有空白或帶控制字元（Invalid，細項路徑 <c>name</c>）。</summary>
    public const string AccessTokenNameRequired = "access_token.name_required";

    /// <summary>權杖的名稱超過 <see cref="AccessTokenRules.MaxNameLength"/> 字（Invalid，細項路徑 <c>name</c>）。</summary>
    public const string AccessTokenNameTooLong = "access_token.name_too_long";

    /// <summary>沒有給任何範圍，或有重複的範圍（Invalid，細項路徑 <c>scopes</c>）。</summary>
    public const string AccessTokenScopesRequired = "access_token.scopes_required";

    /// <summary>期限不在 1～<see cref="AccessTokenRules.MaxExpiresInDays"/> 天（Invalid，細項路徑 <c>expiresInDays</c>）。</summary>
    public const string AccessTokenExpiryInvalid = "access_token.expiry_invalid";

    /// <summary>已經有 <see cref="AccessTokenRules.MaxActiveTokensPerAccount"/> 把還能用的權杖（Conflict）。</summary>
    public const string AccessTokenLimitReached = "access_token.limit_reached";

    /// <summary>沒有這把權杖，或不是自己的，或已經撤銷（NotFound）。</summary>
    public const string AccessTokenNotFound = "access_token.not_found";

    /// <summary>找不到這筆 OAuth 授權（別人的、已經撤銷的也是這個代碼）。</summary>
    public const string OAuthGrantNotFound = "oauth.grant_not_found";

    /// <summary>命令列的瀏覽器登入請求不對：連接埠、state、challenge、電腦名稱、要的權杖或範圍（Invalid，細項路徑是欄位名稱）。</summary>
    public const string CliLoginRequestInvalid = "cli_login.request_invalid";

    /// <summary>瀏覽器登入的碼不存在、用過、過期、verifier 不對或帳號已停用（Invalid）。不分是哪一種。</summary>
    public const string CliLoginCodeInvalid = "cli_login.code_invalid";

    /// <summary>只要本機代理的權杖，但組織不允許成員使用自己的 AI 帳號（Forbidden）。</summary>
    public const string CliLoginAgentNotAllowed = "cli_login.agent_not_allowed";
}
