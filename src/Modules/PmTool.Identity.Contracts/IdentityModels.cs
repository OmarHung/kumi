using PmTool.Abstractions;

namespace PmTool.Identity;

/// <summary>在組織裡的角色。訪客是客戶或外包，只看得到被分享的專案。</summary>
public enum OrganizationRole
{
    Owner,
    Admin,
    Member,
    Guest,
}

/// <summary>已經確認身分的使用者。「人」和「帳號」是分開存的：人員記錄這個人，帳號記錄他怎麼登入。</summary>
public sealed record AuthenticatedUser(
    Guid AccountId,
    Guid PersonId,
    Guid OrganizationId,
    string OrganizationName,
    string DisplayName,
    string Email,
    OrganizationRole Role)
{
    public bool IsOrganizationAdmin => Role is OrganizationRole.Owner or OrganizationRole.Admin;

    public Actor ToActor() => new(OrganizationId, PersonId, IsOrganizationAdmin);
}

/// <param name="IsDisabled">帳號被停用了：不能登入，但工作項目、留言與活動還是顯示他。沒有帳號的人是 false。</param>
public sealed record PersonSummary(Guid PersonId, string DisplayName, string? Email, OrganizationRole Role, bool IsDisabled)
{
    /// <summary>職稱（例如「前端工程師」），組織管理員在人員頁設；沒有是 null。AI 助理靠它和 <see cref="Responsibilities"/> 判斷該指派給誰。</summary>
    public string? JobTitle { get; init; }

    /// <summary>負責範圍，一行字（例如「WordPress 維運、客戶 A 的網站」）；沒有是 null。</summary>
    public string? Responsibilities { get; init; }
}

/// <summary>
/// 人員的職稱與負責範圍（<see cref="IPersonProfileService.UpdateProfileAsync"/>）。兩個都是一行字：去掉前後空白，空字串當作 null；
/// 擋控制字元與看不見的字元（<c>InputText.HasInvisibleCharacters</c>）。長度上限見 <see cref="IdentityRules"/>。
/// </summary>
public sealed record PersonProfile(string? JobTitle, string? Responsibilities);

/// <summary>新增一個人，同時建立他用 Email 加密碼登入的帳號。</summary>
public sealed record NewPerson(string DisplayName, string Email, string Password, OrganizationRole Role);

/// <summary>第一個組織與它的擁有者，用在全新的資料庫。</summary>
public sealed record FirstOwner(string OrganizationName, string DisplayName, string Email, string Password);

public static class IdentityRules
{
    /// <summary>不要求大小寫或符號的組合，只要求長度；組合規則只會讓人選出好猜的密碼。</summary>
    public const int MinPasswordLength = 10;

    public const int MaxPasswordLength = 128;

    public const int MaxDisplayNameLength = 100;

    /// <summary>職稱最長幾個字（去掉前後空白之後）。</summary>
    public const int MaxJobTitleLength = 60;

    /// <summary>負責範圍最長幾個字（去掉前後空白之後）。</summary>
    public const int MaxResponsibilitiesLength = 200;

    public const int MaxEmailLength = 254;

    /// <summary>閒置這麼久工作階段就失效；有在用就會一直延長。</summary>
    public const int SessionIdleHours = 8;

    /// <summary>
    /// 從登入算起，工作階段最多有效這麼久，不管中間有沒有在用。閒置期限會隨著使用一直延長，
    /// 沒有這個上限的話，一份被複製走的 Cookie 只要持續使用就永遠有效。
    /// </summary>
    public const int SessionAbsoluteDays = 7;

    /// <summary>
    /// 查工作階段的結果在程序裡保留這麼久，每個請求才不必都查資料庫。同一個程序裡作廢的會立刻清掉；
    /// 之後跑多個執行個體時，別的執行個體作廢的最多晚這麼久生效。
    /// </summary>
    public const int SessionCacheSeconds = 30;

    /// <summary>
    /// 密碼雜湊（PBKDF2）的次數。比 ASP.NET Core Identity 預設的 10 萬高；舊的雜湊在登入時換成這個次數。
    /// </summary>
    public const int PasswordHashIterations = 210_000;
}
