using PmTool.Abstractions;

namespace PmTool.Identity;

/// <summary>
/// 命令列工具（kumi）用瀏覽器登入：kumi 在這台電腦的 127.0.0.1 開一個暫時的連接埠、打開瀏覽器到網頁的 <c>/cli/authorize</c>，
/// 使用者按「允許」，網頁把一次性的碼帶回 127.0.0.1，kumi 用 PKCE 的 verifier 換成存取權杖。不必在網頁建權杖、複製貼上。
/// 規格見 <c>internal/HANDOFF-setup-wizard.md</c> 第 3 節。
/// 發出去的就是一般的個人存取權杖（同一張表、同一套範圍與上限），多帶 <see cref="AccessTokenSummary.MachineName"/>。
/// 一次最多兩把：命令列用的（<see cref="CliLoginTokenKind.Cli"/>）與本機代理用的（<see cref="CliLoginTokenKind.Agent"/>，
/// 範圍固定是 <see cref="CliLoginRules.AgentScopes"/>）。分成兩把：本機代理的引擎拿到的權杖不能改項目。
/// 碼綁來源：核准與兌換各帶一個由連線算出來的來源鍵（<see cref="CliLoginGrant.Origin"/>、<see cref="CliLoginRedemption.Origin"/>），
/// 瀏覽器與 kumi 要從同一個網路位址連到伺服器才換得到；不同時和其他失敗一樣回 <see cref="IdentityErrorCodes.CliLoginCodeInvalid"/>，而且碼照樣算用過。
/// 導回網址一律由伺服器組成 <c>http://127.0.0.1:{port}{CallbackPath}</c>，不收別的主機或網址，所以碼只會送到這台電腦。
/// </summary>
public interface ICliLoginService
{
    /// <summary>
    /// 檢查請求（使用者打開同意頁時）：連接埠、state、PKCE 的 challenge、電腦名稱、要的權杖、命令列的範圍。
    /// 不對：<see cref="IdentityErrorCodes.CliLoginRequestInvalid"/>（Invalid，細項路徑是欄位名稱：port、state、challenge、machine、tokens、scopes）。
    /// 回傳去掉頭尾空白的電腦名稱。
    /// </summary>
    Task<Result<CliLoginRequest>> CheckAsync(Actor actor, CliLoginParameters parameters, CancellationToken cancellationToken);

    /// <summary>
    /// 使用者按「允許」：照 <see cref="CheckAsync"/> 重新檢查請求，再檢查 <paramref name="grant"/>，
    /// 存一筆一次性的碼（<see cref="CliLoginRules.CodeLifetime"/> 後過期，只存 SHA-256），回傳帶 code、state 的導回網址。
    /// 這時還不建立權杖（兌換時才建，碼沒被換走就什麼都沒有）。
    /// <list type="bullet">
    /// <item><see cref="CliLoginGrant.CliScopes"/>：請求有 cli 時是使用者勾的範圍，至少一個、不重複、不能有 <see cref="AccessTokenScope.AgentRun"/>
    /// （可以和請求的不同：使用者在同意頁替自己決定）；請求沒有 cli 時一定是空的。不對：<see cref="IdentityErrorCodes.CliLoginRequestInvalid"/>（細項路徑 scopes）。</item>
    /// <item><see cref="CliLoginGrant.IncludeAgent"/>：請求有 agent、而且呼叫端確認組織允許成員使用自己的 AI 帳號時才是 true。
    /// 請求有 agent 卻是 false，兌換的結果標 <see cref="CliLoginTokens.AgentSkipped"/>；請求只有 agent 卻是 false：<see cref="IdentityErrorCodes.CliLoginAgentNotAllowed"/>（Forbidden）。</item>
    /// <item><see cref="CliLoginGrant.ExpiresInDays"/>：1～<see cref="AccessTokenRules.MaxExpiresInDays"/>，不收 null（這條路發的權杖一定有期限）。不對：<see cref="IdentityErrorCodes.AccessTokenExpiryInvalid"/>。</item>
    /// <item><see cref="CliLoginGrant.Origin"/>：存在碼上，兌換時比對。空白或超過 <see cref="CliLoginRules.MaxOriginLength"/> 字：<see cref="IdentityErrorCodes.CliLoginRequestInvalid"/>。</item>
    /// <item>就算兌換時換掉 <see cref="CliLoginRules.MaxReplaceTokens"/> 把舊的，這一次要建的把數加上還能用的（不算 OAuth 發的）
    /// 還是超過 <see cref="AccessTokenRules.MaxActiveTokensPerAccount"/>：<see cref="IdentityErrorCodes.AccessTokenLimitReached"/>（Conflict）。
    /// 同一台電腦重跑精靈會換掉自己的舊權杖，不能在這裡先擋；一次最多兩把、最多換兩把時這一條等於不會發生，真正的判斷在兌換。</item>
    /// </list>
    /// 順手刪掉這個組織過期超過一天的碼。
    /// </summary>
    Task<Result<CliLoginApproval>> ApproveAsync(Actor actor, CliLoginParameters parameters, CliLoginGrant grant, CancellationToken cancellationToken);

    /// <summary>
    /// 兌換（不用登入）。碼不存在、用過、過期、<see cref="CliLoginRedemption.Origin"/> 和核准時存的不同（或空白、過長）、verifier 和 challenge 對不上、帳號已經停用：一律 <see cref="IdentityErrorCodes.CliLoginCodeInvalid"/>（Invalid），
    /// 不透露是哪一種；碼只要找到就已經算用過，來源不符的人換不到，之後從對的來源也不能再用同一個碼。在同一個交易裡：把碼標成用過（條件更新，同時兩個請求只有一個換得到）、鎖住帳號那一列、
    /// 撤銷 <see cref="CliLoginRedemption.ReplaceTokens"/> 裡屬於同一個帳號、自己建的（不是 OAuth 發的）、還能用的權杖
    /// （這台電腦重新登入時換掉自己之前的那把；別人的、不認得的照樣略過，不回錯誤），
    /// 再建立權杖：命令列的叫 <see cref="CliLoginRules.CliTokenName"/>、本機代理的叫 <see cref="CliLoginRules.AgentTokenName"/>，
    /// <see cref="AccessTokenSummary.MachineName"/> 是電腦名稱；命令列的權杖期限是核准時選的天數，本機代理的固定 <see cref="CliLoginRules.AgentTokenLifetimeDays"/> 天。撤銷之後還是超過上限：<see cref="IdentityErrorCodes.AccessTokenLimitReached"/>（Conflict，碼照樣算用過）。
    /// </summary>
    Task<Result<CliLoginTokens>> RedeemAsync(CliLoginRedemption redemption, CancellationToken cancellationToken);
}

/// <summary>要哪一把權杖。JSON 是 <c>cli</c>、<c>agent</c>。</summary>
public enum CliLoginTokenKind
{
    /// <summary>命令列用的：範圍是使用者在同意頁勾的。</summary>
    Cli,

    /// <summary>本機代理用的（<c>kumi agent start</c>）：範圍固定是 <see cref="CliLoginRules.AgentScopes"/>。</summary>
    Agent,
}

/// <param name="Port">kumi 在 127.0.0.1 開的連接埠，<see cref="CliLoginRules.MinPort"/>～<see cref="CliLoginRules.MaxPort"/>。</param>
/// <param name="State">kumi 產生的亂數（base64url，<see cref="CliLoginRules.MinStateLength"/>～<see cref="CliLoginRules.MaxStateLength"/> 字），原樣帶回去，kumi 用它認出是自己的請求。</param>
/// <param name="CodeChallenge">PKCE 的 S256 challenge（43～128 個 base64url 字元）。</param>
/// <param name="MachineName">kumi 報的電腦名稱（沒有驗證過，畫面上要說是那台電腦自己報的），去掉頭尾空白後 1～<see cref="CliLoginRules.MaxMachineNameLength"/> 字，不能有控制字元與看不見的格式字元。</param>
/// <param name="Tokens">要的權杖，至少一種、不重複。</param>
/// <param name="Scopes">命令列的權杖要的範圍（同意頁預先勾好的）；有 cli 時至少一個、不重複、不能有 agent:run，沒有 cli 時是空的。</param>
public sealed record CliLoginParameters(
    int Port,
    string State,
    string CodeChallenge,
    string MachineName,
    IReadOnlyList<CliLoginTokenKind> Tokens,
    IReadOnlyList<AccessTokenScope> Scopes);

/// <summary>檢查過的請求，給同意頁顯示。</summary>
public sealed record CliLoginRequest(string MachineName, IReadOnlyList<CliLoginTokenKind> Tokens, IReadOnlyList<AccessTokenScope> Scopes);

/// <summary>使用者在同意頁的決定。</summary>
/// <param name="ExpiresInDays">這條路發的權杖一定有期限：1～<see cref="AccessTokenRules.MaxExpiresInDays"/>，null 不收。只管命令列的權杖；本機代理的固定 <see cref="CliLoginRules.AgentTokenLifetimeDays"/> 天。</param>
/// <param name="Origin">按下「允許」的瀏覽器的來源鍵，由呼叫端（API）從連線算出來，模組不碰 HTTP。兌換時要帶同一個鍵才換得到。</param>
public sealed record CliLoginGrant(IReadOnlyList<AccessTokenScope> CliScopes, bool IncludeAgent, int? ExpiresInDays, string Origin);

/// <param name="RedirectUri"><c>http://127.0.0.1:{port}/kumi-login?code=…&amp;state=…</c>，網頁直接把瀏覽器導過去。</param>
public sealed record CliLoginApproval(string RedirectUri);

/// <param name="ReplaceTokens">這台電腦之前存的權杖明文（最多 <see cref="CliLoginRules.MaxReplaceTokens"/> 把），換到新的之後撤銷；沒有就是空的。</param>
/// <param name="Origin">兌換的呼叫端（kumi 所在電腦）的來源鍵，算法和 <see cref="CliLoginGrant.Origin"/> 相同；和核准時存的不同就換不到。</param>
public sealed record CliLoginRedemption(string Code, string CodeVerifier, IReadOnlyList<string> ReplaceTokens, string Origin)
{
    // 前三個都是祕密：record 預設的 ToString 會把它們印進記錄。
    public override string ToString() => $"CliLoginRedemption {{ ReplaceTokens = {ReplaceTokens.Count} }}";
}

/// <summary>兌換的結果：登入的是誰，與建出來的權杖（沒要或沒建的是 null）。</summary>
/// <param name="AgentSkipped">請求有 agent，但核准時組織不允許，沒有建本機代理的權杖。</param>
public sealed record CliLoginTokens(CliLoginAccount Account, CreatedAccessToken? Cli, CreatedAccessToken? Agent, bool AgentSkipped);

public sealed record CliLoginAccount(string DisplayName, string Email, string OrganizationName);

public static class CliLoginRules
{
    /// <summary>kumi 是讓作業系統配暫時的連接埠，各平台配的範圍都在這個數字以上；常見的開發用連接埠就不會被拿來當導回的目標。</summary>
    public const int MinPort = 32768;

    /// <summary>來源鍵的長度上限，也是資料欄位的長度。</summary>
    public const int MaxOriginLength = 64;

    /// <summary>本機代理的權杖固定的有效天數，不跟使用者在同意頁選的走：這一把在背景自己用、使用者不會每天看到它，所以不讓它不過期；也不跟命令列那把的天數，背景的代理不該因為命令列的權杖選得短就跟著停。</summary>
    public const int AgentTokenLifetimeDays = 90;

    public const int MaxPort = 65535;

    public const int MinStateLength = 16;

    public const int MaxStateLength = 128;

    public const int MaxMachineNameLength = 60;

    /// <summary>按下「允許」到 kumi 換走之間通常不到一秒；短一點，沒被換走的碼就不會留著。</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(1);

    /// <summary>kumi 在 127.0.0.1 接回呼的路徑。</summary>
    public const string CallbackPath = "/kumi-login";

    public const int MaxReplaceTokens = 2;

    /// <summary>本機代理的權杖：<c>agent:run</c> 領工作，其他三個給引擎的 MCP 讀設定、提出變更集、讀項目（和網頁的「建立本機代理用的權杖」同一組）。</summary>
    public static readonly IReadOnlyList<AccessTokenScope> AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite, AccessTokenScope.ItemsRead];

    /// <summary>命令列的權杖的名稱，例如「kumi（MacBook-Pro）」。</summary>
    public static string CliTokenName(string machineName) => $"kumi（{machineName}）";

    /// <summary>本機代理的權杖的名稱，例如「本機代理（MacBook-Pro）」。</summary>
    public static string AgentTokenName(string machineName) => $"本機代理（{machineName}）";
}
