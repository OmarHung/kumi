using PmTool.Abstractions;

namespace PmTool.Identity;

public interface IPeopleDirectory
{
    /// <summary>組織裡的所有人（包含帳號停用的），依顯示名稱排序。</summary>
    Task<IReadOnlyList<PersonSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>依編號查人（包含帳號停用的：工作項目與活動還要顯示他們）。不在這個組織裡的編號不會出現在結果裡。</summary>
    Task<IReadOnlyDictionary<Guid, PersonSummary>> FindManyAsync(
        Guid organizationId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken);

    /// <summary>新增一個人與他的登入帳號。只有組織的擁有者或管理員可以做。</summary>
    Task<Result<PersonSummary>> CreateAsync(Actor actor, NewPerson person, CancellationToken cancellationToken);

    /// <summary>
    /// 停用或重新啟用一個人的帳號。停用時他所有的工作階段立刻作廢。已經是那個狀態時什麼都不做，照樣成功。
    /// 規則：只有擁有者或管理員可以做；擁有者與管理員的帳號只有擁有者能動；不能動自己的帳號
    /// （所以永遠至少留著一位能登入的擁有者）。失敗代碼見 <see cref="IdentityErrorCodes"/>。
    /// </summary>
    Task<Result<PersonSummary>> SetDisabledAsync(Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken);

    /// <summary>
    /// 替別人設一組新密碼（沒有寄信的服務時，忘記密碼只能這樣救）。成功後他所有的工作階段立刻作廢。
    /// 權限規則和 <see cref="SetDisabledAsync"/> 一樣；自己的密碼要用 <see cref="IAccountService.ChangeOwnPasswordAsync"/>，
    /// 才會先確認目前的密碼。密碼長度的問題，細項路徑是 <c>newPassword</c>。
    /// </summary>
    Task<Result<PersonSummary>> ResetPasswordAsync(Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken);

    /// <summary>
    /// 改一個人在組織裡的角色。已經是那個角色時什麼都不做，照樣成功。他的工作階段不作廢，但快取立刻清掉，下一個請求就是新的角色。
    /// 規則：只有擁有者或管理員可以做；目前或要改成擁有者、管理員的，只有擁有者能動；不能改自己的角色
    /// （所以永遠至少留著一位擁有者：要換人當擁有者，先把別人升成擁有者，再由他把你降下來）。帳號停用的人也可以改。
    /// 失敗代碼見 <see cref="IdentityErrorCodes"/>（<c>person.forbidden</c>、<c>person.not_found</c>、<c>person.is_self</c>）。
    /// </summary>
    Task<Result<PersonSummary>> SetRoleAsync(Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken);
}
