using PmTool.Abstractions;

namespace PmTool.Identity;

/// <summary>
/// pm-tool 自己的 OAuth 2.1 授權伺服器：讓使用者自己電腦上的 AI 代理（Claude Code、Codex 這類 MCP 用戶端）
/// 用 pm-tool 的帳號登入授權，拿到只能用在 MCP 端點的權杖。規格見 <c>internal/HANDOFF-mcp.md</c> 第 3、4 節。
/// 發出去的存取權杖就是一把個人存取權杖（<c>pmt_…</c>，同一張表、同一套範圍與驗證），多帶一個授權的編號
/// （<see cref="ActiveAccessToken.GrantId"/>），只能用在 MCP。
/// 預期中的失敗用 <see cref="OAuthError"/> 的代碼（OAuth 的錯誤格式），不用 IdentityErrorCodes：
/// 這些端點給 MCP 用戶端讀，用戶端只認 RFC 6749 的錯誤。
/// </summary>
public interface IOAuthService
{
    /// <summary>
    /// 檢查一個授權請求（使用者打開同意頁時）。用戶端、導回網址、PKCE、resource、範圍任何一項不對都回失敗；
    /// 用戶端與導回網址驗過之後的失敗，<see cref="OAuthFailure.RedirectTo"/> 帶著要把錯誤帶回去的網址（RFC 6749 4.1.2.1）。
    /// 用戶端是 Client ID Metadata Document（<c>client_id</c> 是 https 網址）時，會去抓那份文件（有快取；
    /// 抓不到時，過期的快取最多沿用到抓到的那一刻起 <see cref="OAuthRules.MetadataCacheMax"/>，再久就回 <see cref="OAuthError.TemporarilyUnavailable"/>）。
    /// 動態註冊的用戶端沒有人驗證過，只能導回這台電腦（127.0.0.1、[::1]、localhost）；外部的 https 導回網址只給 Client ID Metadata Document 的用戶端。
    /// 回傳的主機（<see cref="OAuthAuthorizationRequest.ClientHost"/>、<see cref="OAuthAuthorizationRequest.RedirectHost"/>）一律是 punycode（<c>xn--…</c>），
    /// 長得像別人網域的 Unicode 字（例如西里爾字母的 а）才看得出來。
    /// </summary>
    Task<OAuthResult<OAuthAuthorizationRequest>> CheckAuthorizationAsync(
        OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken);

    /// <summary>
    /// 使用者按「允許」：重新檢查請求，發一次性的授權碼，回傳要導回去的網址（帶 code、state、iss）。
    /// <paramref name="grantedScopes"/> 是使用者勾的範圍，要是請求的範圍的子集、至少一個。
    /// 同一個人、同一個用戶端再授權一次時，更新那一筆授權（範圍換成這次的）：範圍沒縮小，之前發的權杖照舊有效；
    /// 縮小了，這筆授權裡範圍超過新範圍的存取權杖當場撤銷（更新權杖留著，它之後換出的權杖只有新的範圍，
    /// 還沒兌換的舊授權碼也只換得到新範圍裡的）。
    /// </summary>
    Task<OAuthResult<string>> ApproveAsync(
        OAuthServer server,
        Actor actor,
        OAuthAuthorizeParameters parameters,
        IReadOnlyList<AccessTokenScope> grantedScopes,
        CancellationToken cancellationToken);

    /// <summary>使用者按「拒絕」：回傳帶 <c>error=access_denied</c> 的導回網址。請求本身不對時照 <see cref="CheckAuthorizationAsync"/> 失敗。</summary>
    Task<OAuthResult<string>> DenyAsync(
        OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken);

    /// <summary>Token 端點：用授權碼換權杖（<c>grant_type=authorization_code</c>）。授權碼只能用一次，第二次用時撤銷它發出的權杖。</summary>
    Task<OAuthResult<OAuthTokens>> ExchangeCodeAsync(
        OAuthServer server, OAuthCodeExchange request, CancellationToken cancellationToken);

    /// <summary>
    /// Token 端點：用更新權杖換新的一組（<c>grant_type=refresh_token</c>）。更新權杖每次都換新的（公開用戶端一定要輪替）；
    /// 已經用過的再被拿來用，當作外流，撤銷整筆授權。
    /// </summary>
    Task<OAuthResult<OAuthTokens>> RefreshAsync(
        OAuthServer server, OAuthRefreshRequest request, CancellationToken cancellationToken);

    /// <summary>RFC 7009 撤銷：存取權杖或更新權杖都可以；不認得的也當作成功（不透露存不存在）。</summary>
    Task RevokeTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// RFC 7591 動態註冊（MCP 規格已經標成淘汰，留給還不支援 Client ID Metadata Document 的用戶端）。只收公開用戶端，
    /// 而且導回網址只收這台電腦的（127.0.0.1、[::1]、localhost），其他回 <see cref="OAuthError.InvalidRedirectUri"/>：沒有經過驗證的應用不能導回外部網址。
    /// </summary>
    Task<OAuthResult<OAuthRegisteredClient>> RegisterClientAsync(OAuthClientRegistration registration, CancellationToken cancellationToken);

    /// <summary>自己授權過、還沒撤銷的應用（「我的帳號」用），最近用過的在前。</summary>
    Task<IReadOnlyList<OAuthGrantSummary>> ListGrantsAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>撤銷自己的一筆授權：它的存取權杖與更新權杖立刻失效（同一個程序裡）。別人的、不存在的回 <see cref="IdentityErrorCodes.OAuthGrantNotFound"/>。</summary>
    Task<Result<Unit>> RevokeGrantAsync(Actor actor, Guid grantId, CancellationToken cancellationToken);
}

/// <summary>
/// 這個部署對外的身分。由 API 依設定（<c>Security:PublicHost</c>）或請求組出來，每個呼叫都帶著，模組不碰 HTTP。
/// </summary>
/// <param name="Issuer">授權伺服器的 issuer，例如 <c>https://pm.example.com</c>（沒有結尾的斜線）。</param>
/// <param name="Resource">MCP 端點的正式網址，例如 <c>https://pm.example.com/mcp</c>。只發給這個 resource 的權杖。</param>
public sealed record OAuthServer(string Issuer, string Resource);

/// <summary>授權請求的參數（同意頁從網址列讀來、原樣送給 API）。</summary>
/// <param name="ResponseType">一定要是 <c>code</c>。</param>
/// <param name="CodeChallengeMethod">一定要是 <c>S256</c>。</param>
/// <param name="Resource">要等於 <see cref="OAuthServer.Resource"/>（RFC 8707）。</param>
/// <param name="Scope">空白分開的範圍；null 或空的是要全部（<see cref="AccessTokenScopes.All"/>）。不認得的範圍略過。</param>
public sealed record OAuthAuthorizeParameters(
    string? ResponseType,
    string? ClientId,
    string? RedirectUri,
    string? Scope,
    string? State,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? Resource);

/// <summary>檢查過的授權請求，同意頁照它畫。</summary>
/// <param name="ClientName">用戶端自己報的名稱（Client ID Metadata Document 或動態註冊時填的），沒有驗證過，畫面要說清楚。</param>
/// <param name="ClientHost">
/// Client ID Metadata Document 的網域（punycode 形式，例如 <c>claude.ai</c>）：這個名稱是那個網域的人寫的。
/// 動態註冊的用戶端是 null（畫面寫「沒有經過驗證的應用」）。
/// </param>
/// <param name="RedirectHost">授權之後導回的主機（MCP 規格要求清楚顯示），例如 <c>127.0.0.1:53682</c>。</param>
/// <param name="RedirectIsLoopback">導回到這台電腦上的程式（127.0.0.1、localhost、::1）。畫面要提醒：只在你自己電腦上的程式要求時才允許。</param>
/// <param name="RequestedScopes">請求的範圍（去掉不認得的之後）；同意頁預設全部勾選。</param>
public sealed record OAuthAuthorizationRequest(
    string ClientId,
    string ClientName,
    string? ClientHost,
    string RedirectHost,
    bool RedirectIsLoopback,
    IReadOnlyList<AccessTokenScope> RequestedScopes);

/// <param name="RedirectUri">要和授權請求時一模一樣。</param>
public sealed record OAuthCodeExchange(string? Code, string? RedirectUri, string? ClientId, string? CodeVerifier, string? Resource);

/// <param name="Scope">要縮小範圍時給（空白分開、要是原本的子集）；null 是照原本的。</param>
public sealed record OAuthRefreshRequest(string? RefreshToken, string? ClientId, string? Resource, string? Scope);

/// <summary>Token 端點的成功回應（RFC 6749 5.1）。</summary>
/// <param name="AccessToken">存取權杖的明文（<c>pmt_…</c>），只在這裡出現一次。</param>
/// <param name="ExpiresIn">存取權杖幾秒後過期（<see cref="OAuthRules.AccessTokenLifetime"/>）。</param>
/// <param name="RefreshToken">更新權杖的明文（<c>pmr_…</c>）。</param>
/// <param name="Scope">實際給的範圍，空白分開。</param>
public sealed record OAuthTokens(string AccessToken, int ExpiresIn, string RefreshToken, string Scope)
{
    public override string ToString() => $"OAuthTokens {{ AccessToken = ***, ExpiresIn = {ExpiresIn}, RefreshToken = ***, Scope = {Scope} }}";
}

/// <summary>動態註冊的請求（RFC 7591 2，只認這幾個欄位，其餘略過）。</summary>
/// <param name="TokenEndpointAuthMethod">
/// 一律註冊成公開用戶端（<c>none</c>）。null、<c>none</c>、<c>client_secret_post</c>、<c>client_secret_basic</c> 都收（後兩個換成 none，
/// RFC 7591 允許；MCP 官方的 C# SDK 一律要求 client_secret_post），其他的（private_key_jwt 這類）不收。
/// </param>
public sealed record OAuthClientRegistration(
    string? ClientName,
    IReadOnlyList<string>? RedirectUris,
    string? TokenEndpointAuthMethod,
    IReadOnlyList<string>? GrantTypes,
    IReadOnlyList<string>? ResponseTypes);

/// <summary>動態註冊的結果（RFC 7591 3.2.1）。沒有 client_secret。</summary>
public sealed record OAuthRegisteredClient(string ClientId, string ClientName, IReadOnlyList<string> RedirectUris, DateTimeOffset IssuedAt);

/// <summary>「我的帳號」裡的一筆授權。</summary>
/// <param name="ClientHost">同 <see cref="OAuthAuthorizationRequest.ClientHost"/>。</param>
public sealed record OAuthGrantSummary(
    Guid GrantId,
    string ClientName,
    string? ClientHost,
    IReadOnlyList<AccessTokenScope> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>OAuth 的成功或失敗（和 <see cref="Result{T}"/> 同一個用法，失敗的形狀是 OAuth 的）。</summary>
public readonly record struct OAuthResult<T>
{
    private readonly T? _value;

    private OAuthResult(T value)
    {
        _value = value;
        Failure = null;
    }

    private OAuthResult(OAuthFailure failure)
    {
        _value = default;
        Failure = failure;
    }

    public OAuthFailure? Failure { get; }

    public bool Succeeded => Failure is null;

    public T Value => Failure is null
        ? _value!
        : throw new InvalidOperationException($"OAuth 失敗了（{Failure.Error}），沒有值可以取。");

    public static implicit operator OAuthResult<T>(T value) => new(value);

    public static implicit operator OAuthResult<T>(OAuthFailure failure) => new(failure);
}

/// <summary>OAuth 的錯誤（RFC 6749 4.1.2.1、5.2，RFC 8707）。</summary>
/// <param name="Error"><see cref="OAuthError"/> 的代碼。</param>
/// <param name="Description">給人看的說明，繁體中文（同意頁直接顯示；也放進 <c>error_description</c>）。</param>
/// <param name="RedirectTo">
/// 用戶端與導回網址都驗過時，帶著錯誤導回去的完整網址（含 error、error_description、state、iss）；
/// 沒驗過時是 null：絕對不能導到沒驗證過的網址，同意頁直接顯示錯誤。
/// </param>
public sealed record OAuthFailure(string Error, string Description, string? RedirectTo = null);

/// <summary>OAuth 的錯誤代碼。是 RFC 定義的值，不能改。</summary>
public static class OAuthError
{
    public const string InvalidRequest = "invalid_request";
    public const string InvalidClient = "invalid_client";
    public const string InvalidGrant = "invalid_grant";
    public const string UnauthorizedClient = "unauthorized_client";
    public const string UnsupportedGrantType = "unsupported_grant_type";
    public const string UnsupportedResponseType = "unsupported_response_type";
    public const string InvalidScope = "invalid_scope";
    public const string AccessDenied = "access_denied";

    /// <summary>RFC 8707：resource 不是這個部署的 MCP 端點。</summary>
    public const string InvalidTarget = "invalid_target";

    /// <summary>RFC 7591：動態註冊的內容不對。</summary>
    public const string InvalidClientMetadata = "invalid_client_metadata";

    /// <summary>RFC 7591：導回網址不合規定。</summary>
    public const string InvalidRedirectUri = "invalid_redirect_uri";

    /// <summary>伺服器自己的問題（例如抓 Client ID Metadata Document 時對方逾時）。</summary>
    public const string TemporarilyUnavailable = "temporarily_unavailable";
}

public static class OAuthRules
{
    /// <summary>更新權杖明文的前綴，和存取權杖（<see cref="AccessTokenRules.Prefix"/>）分開，認得出是哪一種。</summary>
    public const string RefreshTokenPrefix = "pmr_";

    /// <summary>動態註冊的 client_id 的前綴。</summary>
    public const string RegisteredClientPrefix = "pmc_";

    /// <summary>授權碼的有效時間；只能用一次。</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(1);

    /// <summary>存取權杖的有效時間（MCP 規格：發短效的存取權杖）。</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);

    /// <summary>更新權杖的有效時間；每次換新的都重新算，所以一直在用就一直有效，30 天沒用要重新授權。</summary>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// Client ID Metadata Document 的快取：照對方的 Cache-Control，但不短於、不長於這兩個值。
    /// 重新抓不到時，過期的快取最多沿用到「抓到的那一刻」起 <see cref="MetadataCacheMax"/>，再久就不信了。
    /// </summary>
    public static readonly TimeSpan MetadataCacheMin = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MetadataCacheMax = TimeSpan.FromHours(24);

    /// <summary>抓 Client ID Metadata Document 的逾時與大小上限。</summary>
    public static readonly TimeSpan MetadataFetchTimeout = TimeSpan.FromSeconds(5);

    public const int MaxMetadataBytes = 64 * 1024;

    public const int MaxClientNameLength = 100;

    public const int MaxRedirectUris = 10;

    public const int MaxRedirectUriLength = 500;

    public const int MaxStateLength = 500;

    /// <summary>一個人同時最多幾筆還沒撤銷的授權。</summary>
    public const int MaxGrantsPerAccount = 20;
}
