using PmTool.Abstractions;

namespace PmTool.Identity;

/// <summary>
/// 個人存取權杖：給 CLI、腳本與 CI 用，帶在 <c>Authorization: Bearer</c>。權杖代表建立它的人，
/// 能做的事是那個人的權限再加上權杖的範圍（<see cref="AccessTokenScope"/>）兩者都允許的。
/// </summary>
public interface IAccessTokenService
{
    /// <summary>
    /// 替自己建一把權杖。明文只在回傳時出現一次（資料庫只存雜湊）。
    /// 名稱、範圍、期限不合規定與超過 <see cref="AccessTokenRules.MaxActiveTokensPerAccount"/> 把時回失敗，代碼見
    /// <see cref="IdentityErrorCodes"/> 的 access_token.*。
    /// </summary>
    Task<Result<CreatedAccessToken>> CreateAsync(Actor actor, NewAccessToken token, CancellationToken cancellationToken);

    /// <summary>自己還沒撤銷的權杖（包含已經過期的，畫面標示出來），新的在前。</summary>
    Task<IReadOnlyList<AccessTokenSummary>> ListAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>撤銷自己的一把權杖。別人的、不存在的、已經撤銷的都回 <see cref="IdentityErrorCodes.AccessTokenNotFound"/>。同一個程序裡立刻失效。</summary>
    Task<Result<AccessTokenSummary>> RevokeAsync(Actor actor, Guid tokenId, CancellationToken cancellationToken);

    /// <summary>
    /// 依明文找還有效的權杖：沒撤銷、沒過期、帳號沒停用。有效時記下最後使用的時刻，並回傳帳號現在的資料。
    /// 格式不對的直接回 null，不送進資料庫。結果在程序裡保留最多 <see cref="IdentityRules.SessionCacheSeconds"/> 秒，
    /// 同一個程序裡撤銷的立刻失效（和工作階段一樣）。
    /// </summary>
    Task<ActiveAccessToken?> FindAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// 只看程序裡的快取，不查資料庫：有可用的、還有效的結果才回，其他一律 null（包含沒查過的）。
    /// 來源被擋下時，用它放行剛用過的有效權杖，又不讓假權杖每個都查一次資料庫。
    /// </summary>
    ActiveAccessToken? TryFindCached(string token);
}

/// <summary>權杖能做的事。對外的名稱（<c>items:read</c> 這些）見 <see cref="AccessTokenScopes"/>，是合約，不能改。</summary>
public enum AccessTokenScope
{
    /// <summary>讀專案、項目、活動、我的工作、模板、人員清單。</summary>
    ItemsRead,

    /// <summary>新增、修改、刪除項目，改狀態、改類型、留言。</summary>
    ItemsWrite,

    /// <summary>讀專案的藍圖、變更集，預覽變更集。</summary>
    BlueprintRead,

    /// <summary>送出、套用、拒絕變更集（套用仍然要那個人有 blueprint.apply）；專案改名、封存與取消封存（仍然要那個人有 project.manage）。</summary>
    BlueprintWrite,

    /// <summary>
    /// 當本機代理（<c>kumi agent start</c>）：登記這台電腦的 Claude Code／Codex、領網頁的 AI 助理送來的訊息、回報回覆。
    /// 只開放本機代理的端點與 MCP 上給本機代理用的 propose_item_changes（只建立修改建議，使用者在網頁按套用才改）；
    /// 本機的引擎用 MCP 讀設定、提出變更集時另外要 <see cref="BlueprintRead"/>、<see cref="BlueprintWrite"/>，讀項目要 <see cref="ItemsRead"/>。
    /// OAuth 的授權不發這個範圍（MCP 用不到）。
    /// </summary>
    AgentRun,
}

public static class AccessTokenScopes
{
    public const string ItemsRead = "items:read";
    public const string ItemsWrite = "items:write";
    public const string BlueprintRead = "blueprint:read";
    public const string BlueprintWrite = "blueprint:write";
    public const string AgentRun = "agent:run";

    public static IReadOnlyList<AccessTokenScope> All { get; } =
        [AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite, AccessTokenScope.AgentRun];

    /// <summary>
    /// OAuth 授權（MCP 的連線）能要、能發的範圍：不含 <see cref="AgentRun"/>。本機代理用個人存取權杖，
    /// MCP 用不到它；授權時就算要了也不認得（和其他不認得的範圍一樣略過）。
    /// </summary>
    public static IReadOnlyList<AccessTokenScope> Grantable { get; } = [.. All.Where(scope => scope != AccessTokenScope.AgentRun)];

    public static string NameOf(AccessTokenScope scope) => scope switch
    {
        AccessTokenScope.ItemsRead => ItemsRead,
        AccessTokenScope.ItemsWrite => ItemsWrite,
        AccessTokenScope.BlueprintRead => BlueprintRead,
        AccessTokenScope.BlueprintWrite => BlueprintWrite,
        AccessTokenScope.AgentRun => AgentRun,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不認得的權杖範圍。"),
    };

    /// <summary>只認對外的名稱，大小寫要完全一樣。</summary>
    public static bool TryParse(string? name, out AccessTokenScope scope)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(NameOf(candidate), name, StringComparison.Ordinal))
            {
                scope = candidate;
                return true;
            }
        }

        scope = default;
        return false;
    }
}

public static class AccessTokenRules
{
    /// <summary>權杖明文的前綴。看到它就知道是這個產品的權杖（祕密掃描工具也靠它認）。</summary>
    public const string Prefix = "pmt_";

    public const int MaxNameLength = 100;

    /// <summary>一個帳號同時最多幾把還沒撤銷、還沒過期的權杖。</summary>
    public const int MaxActiveTokensPerAccount = 20;

    /// <summary>期限最長幾天；新建的權杖一定要有期限，不收不過期的。</summary>
    public const int MaxExpiresInDays = 365;

    /// <summary>範圍含 <see cref="AccessTokenScope.AgentRun"/>（本機代理）的權杖期限最長幾天，和瀏覽器登入發的本機代理權杖一致。</summary>
    public const int MaxAgentExpiresInDays = 90;
}

/// <param name="Scopes">
/// 範圍；<see cref="AccessTokenScope.AgentRun"/> 不能和 <see cref="AccessTokenScope.ItemsWrite"/> 放在同一把
/// （<see cref="IdentityErrorCodes.AccessTokenScopesConflict"/>），本機代理的權杖會交給使用者電腦上的引擎。
/// </param>
/// <param name="ExpiresInDays">
/// 幾天後過期，1～<see cref="AccessTokenRules.MaxExpiresInDays"/>（含 <see cref="AccessTokenScope.AgentRun"/> 時最長
/// <see cref="AccessTokenRules.MaxAgentExpiresInDays"/>）。型別是可為 null，null 會被拒絕
/// （<see cref="IdentityErrorCodes.AccessTokenExpiryInvalid"/>，路徑 <c>expiresInDays</c>），這樣 JSON 的 null 是有路徑的 422，不是 400。
/// </param>
public sealed record NewAccessToken(string Name, IReadOnlyList<AccessTokenScope> Scopes, int? ExpiresInDays);

/// <param name="DisplayPrefix">明文的開頭幾個字（含 <c>pmt_</c>），畫面上認得出是哪一把，不足以拿來用。</param>
public sealed record AccessTokenSummary(
    Guid TokenId,
    string Name,
    string DisplayPrefix,
    IReadOnlyList<AccessTokenScope> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>
    /// 用瀏覽器登入（<see cref="ICliLoginService"/>）時 kumi 報的電腦名稱；在網頁上自己建的是 null。
    /// 網頁的清單用它寫「從 MacBook-Pro 的 kumi 登入」，設定精靈用它判斷「命令列已經登入過」。
    /// </summary>
    public string? MachineName { get; init; }

    /// <summary>
    /// 建立這把權杖時伺服器看到的位址（走過轉送標頭）。瀏覽器登入是兌換的那台電腦；在網頁上建的是瀏覽器。
    /// 舊的權杖是 null。
    /// </summary>
    public string? CreatedFrom { get; init; }
}

/// <param name="Secret">權杖的明文，只在這裡出現一次。</param>
public sealed record CreatedAccessToken(AccessTokenSummary Token, string Secret);

/// <summary>還有效的權杖與帳號現在的資料。</summary>
public sealed record ActiveAccessToken(Guid TokenId, AuthenticatedUser User, IReadOnlyList<AccessTokenScope> Scopes)
{
    /// <summary>
    /// OAuth 授權發出的權杖（<see cref="IOAuthService"/>）是那筆授權的編號；自己建的個人存取權杖是 null。
    /// 有值的權杖只能用在 MCP 端點（發給它的 resource），API 的其他端點一律不收。
    /// </summary>
    public Guid? GrantId { get; init; }
}
