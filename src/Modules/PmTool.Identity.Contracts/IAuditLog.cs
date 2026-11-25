using PmTool.Abstractions;

namespace PmTool.Identity;

/// <summary>
/// 帳號與權限相關的稽核紀錄。只能新增，保存 <see cref="AuditRules.RetentionDays"/> 天；
/// 寫入由身分模組在各個操作的交易裡做，這裡只有讀取，加上登入這種不能在請求路上寫的紀錄。
/// </summary>
public interface IAuditLog
{
    /// <summary>
    /// 新的在前。只有組織的擁有者能讀（每次查資料庫，不看 <see cref="Actor.IsOrganizationAdmin"/>），
    /// 其他人回 <see cref="IdentityErrorCodes.Forbidden"/>；只看得到自己的組織。
    /// 參數不合回 <see cref="IdentityErrorCodes.AuditQueryInvalid"/>（細項路徑 <c>limit</c> 或 <c>cursor</c>）。
    /// </summary>
    Task<Result<AuditPage>> ListAsync(Actor actor, AuditQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 記一次登入的結果。要在回應送完之後才呼叫：只替存在的帳號寫一列，回應時間就會說出這個 Email 有沒有帳號。
    /// 依正規化後的 Email 找帳號，找不到就不寫（沒有組織可以記）。來源由呼叫端明確帶進來。
    /// </summary>
    Task RecordSignInAsync(SignInAttempt attempt, CancellationToken cancellationToken);
}

/// <param name="Cursor">上一頁回的 <see cref="AuditPage.NextCursor"/>；第一頁是 null。</param>
/// <param name="Limit">1～<see cref="AuditRules.MaxLimit"/>。</param>
/// <param name="PersonId">只看這個人做的、或對這個人做的。</param>
public sealed record AuditQuery(string? Cursor = null, int Limit = AuditRules.DefaultLimit, Guid? PersonId = null);

/// <param name="Details">值都是字串，數字與清單也是（清單用空白隔開）。</param>
public sealed record AuditEntry(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string Action,
    string Outcome,
    Guid? ActorPersonId,
    Guid? TargetPersonId,
    string? SourceAddress,
    IReadOnlyDictionary<string, string> Details);

/// <param name="NextCursor">沒有下一頁是 null。</param>
public sealed record AuditPage(IReadOnlyList<AuditEntry> Items, string? NextCursor);

public sealed record SignInAttempt(string Email, bool Succeeded, string? SourceAddress, string? UserAgent);

/// <summary><c>action</c> 的值。對外合約，不能改名。</summary>
public static class AuditActions
{
    public const string SignIn = "sign_in";
    public const string PasswordChanged = "password_changed";
    public const string OtherSessionsSignedOut = "other_sessions_signed_out";
    public const string PasswordReset = "password_reset";
    public const string AccountDisabled = "account_disabled";
    public const string AccountEnabled = "account_enabled";
    public const string RoleChanged = "role_changed";
    public const string PersonCreated = "person_created";
    public const string AccessTokenCreated = "access_token_created";
    public const string AccessTokenRevoked = "access_token_revoked";
    public const string CliLoginApproved = "cli_login_approved";
    public const string CliLoginRedeemed = "cli_login_redeemed";
    public const string CliLoginCodeReused = "cli_login_code_reused";
    public const string OAuthAuthorized = "oauth_authorized";
    public const string OAuthGrantRevoked = "oauth_grant_revoked";
}

/// <summary><c>outcome</c> 的值；資料庫的檢查限制只收這兩個。</summary>
public static class AuditOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public static class AuditRules
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 100;

    /// <summary>超過這麼久的紀錄在每次遷移時刪掉。</summary>
    public const int RetentionDays = 400;

    /// <summary>每個程序每分鐘最多寫這麼多筆登入失敗，超過的只記一筆警告：免得被拿來塞爆資料表。</summary>
    public const int MaxSignInFailuresPerMinute = 600;
}
