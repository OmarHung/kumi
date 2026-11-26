using PmTool.Abstractions;

namespace PmTool.Work;

public interface IProjectService
{
    IReadOnlyList<TemplateSummary> ListTemplates();

    /// <summary>呼叫者看得到的專案：組織管理員看得到全部，其他人只看得到自己是成員的。依建立時間排序。封存的也在裡面（看 ArchivedAt）。</summary>
    Task<IReadOnlyList<ProjectSummary>> ListAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 從模板開一個專案：把模板的藍圖存成第 1 版，建立者成為專案的 admin。只有組織管理員可以做。
    /// </summary>
    Task<Result<ProjectSummary>> CreateAsync(Actor actor, NewProject project, CancellationToken cancellationToken);

    /// <summary>
    /// 檢查一個新專案能不能建，和 <see cref="CreateAsync"/> 同一套規則（只有組織管理員、代號、名稱、模板、說明、代號還沒有人用），
    /// 什麼都不寫。AI 助理的新專案建議在提出時用它先擋；使用者按「建立專案」時 <see cref="CreateAsync"/> 會再檢查一次（中間代號可能被用掉）。
    /// </summary>
    Task<Result<Unit>> CheckNewProjectAsync(Actor actor, NewProject project, CancellationToken cancellationToken);

    /// <summary>專案、它目前的藍圖與成員。看不到的專案和不存在的專案一樣回 NotFound，不透露它存在。</summary>
    Task<Result<ProjectDetail>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    /// <summary>
    /// 把一個人加進專案，或改他的角色。角色必須是內建角色或藍圖裡定義的自訂角色。
    /// 需要 project.manage 權限或組織管理員。這個人是不是組織的成員由呼叫端先確認。
    /// 不是組織管理員的人把專案最後一位 admin 換成別的角色會被擋下（<see cref="WorkErrorCodes.LastAdmin"/>）；
    /// 組織管理員不擋：他們在每個專案都有全部權限，不會因此管不到，單人使用時也要能把自己換成自訂角色去走受限的轉換。
    /// </summary>
    Task<Result<ProjectMember>> SetMemberAsync(
        Actor actor, string projectKey, Guid personId, string role, CancellationToken cancellationToken);

    /// <summary>
    /// 把一個人移出專案，回傳他被移除前的成員資料。需要 project.manage 權限或組織管理員。
    /// 不是成員時回 <see cref="WorkErrorCodes.MemberNotFound"/>；不是組織管理員的人不能移除最後一位 admin（<see cref="WorkErrorCodes.LastAdmin"/>）。
    /// 他負責的項目與成員欄位的值都留著，只是之後不能再把他填進去。
    /// </summary>
    Task<Result<ProjectMember>> RemoveMemberAsync(
        Actor actor, string projectKey, Guid personId, CancellationToken cancellationToken);

    /// <summary>
    /// 改專案的名稱（代號不能改：它是項目編號的前綴，已經寫在連結、提交訊息與別的系統裡）。需要 project.manage 或組織管理員；封存的專案也可以改。
    /// 名稱的規則和建立時一樣（去掉前後空白後 1 到 <see cref="WorkRules.MaxProjectNameLength"/> 個字、不能有控制字元），不合格是驗證錯誤，路徑是 name。
    /// 名稱沒變時什麼都不做。
    /// </summary>
    Task<Result<ProjectSummary>> RenameAsync(Actor actor, string projectKey, string name, CancellationToken cancellationToken);

    /// <summary>
    /// 封存專案：之後只能看（見 <see cref="ProjectSummary.ArchivedAt"/>），「我的工作」不再列它的項目，側欄不再列它。
    /// 需要 project.manage 或組織管理員。已經封存的什麼都不做，回傳它（ArchivedAt 是原本的時刻）。
    /// </summary>
    Task<Result<ProjectSummary>> ArchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    /// <summary>取消封存。需要 project.manage 或組織管理員。沒有封存的什麼都不做，回傳它。</summary>
    Task<Result<ProjectSummary>> UnarchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken);
}
