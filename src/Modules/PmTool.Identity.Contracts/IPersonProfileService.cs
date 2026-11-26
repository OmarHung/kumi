using PmTool.Abstractions;

namespace PmTool.Identity;

/// <summary>
/// 人員的職稱與負責範圍。讀的地方是 <see cref="IPeopleDirectory"/>（<see cref="PersonSummary.JobTitle"/>、<see cref="PersonSummary.Responsibilities"/>）；
/// 這裡只有寫入。和 <see cref="IPeopleDirectory"/> 分開放，是為了不讓每個模組測試裡的假人員目錄都得跟著多實作一個方法。
/// 規格見 <c>internal/HANDOFF-ai-assign.md</c>。
/// </summary>
public interface IPersonProfileService
{
    /// <summary>
    /// 設定一個人的職稱與負責範圍（兩個都整個換掉；要清掉就給 null）。只有組織的擁有者或管理員能做，可以改任何人（包括自己、擁有者、帳號停用的）：
    /// 這不是安全性的設定，不像角色、密碼那樣分擁有者與管理員。
    /// 失敗：<see cref="IdentityErrorCodes.Forbidden"/>（403）、<see cref="IdentityErrorCodes.NotFound"/>（不在這個組織）、
    /// <see cref="IdentityErrorCodes.ProfileInvalid"/>（422，細項路徑 <c>jobTitle</c>、<c>responsibilities</c>）。沒有變動時什麼都不做，照樣成功。
    /// </summary>
    Task<Result<PersonSummary>> UpdateProfileAsync(Actor actor, Guid personId, PersonProfile profile, CancellationToken cancellationToken);
}
