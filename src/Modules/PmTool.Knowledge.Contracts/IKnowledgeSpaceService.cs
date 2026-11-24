using PmTool.Abstractions;

namespace PmTool.Knowledge;

/// <summary>
/// 知識庫的空間（internal/HANDOFF-knowledge-base.md 第 3 節）。誰看得到、能做什麼見 <see cref="SpaceSummary"/> 與 <see cref="SpacePermissions"/>；
/// 規則只有模組裡的一份（<c>Access/SpaceAccess</c>），所有讀寫空間與頁面的路都用它。看不到的空間一律當作不存在（<see cref="KnowledgeErrorCodes.SpaceNotFound"/>）。
/// 組織的角色每次問身分模組（<c>IPeopleDirectory</c>），專案的權限每次問工作引擎（<c>IWorkAccessQueries.VisibleProjectsAsync</c>），不快取。
/// </summary>
public interface IKnowledgeSpaceService
{
    /// <summary>
    /// 看得到的空間：沒封存的在前，再依名稱（不分大小寫）、代號。綁的專案封存了的空間算在封存的那一邊。
    /// </summary>
    Task<IReadOnlyList<SpaceSummary>> ListAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>依代號（不分大小寫）讀一個空間。看不到或不存在是 <see cref="KnowledgeErrorCodes.SpaceNotFound"/>。</summary>
    Task<Result<SpaceSummary>> GetAsync(Actor actor, string spaceKey, CancellationToken cancellationToken);

    /// <summary>
    /// 建立空間，回傳新的空間。
    /// 組織空間（<see cref="NewSpace.ProjectKey"/> 是 null）只有組織管理員能建，其他人 <see cref="KnowledgeErrorCodes.Forbidden"/>。
    /// 專案空間要看得到那個專案（不然 422 <see cref="KnowledgeErrorCodes.ProjectNotFound"/>，路徑 projectKey）、
    /// 有 project.manage（組織管理員一定有；不然 Forbidden）、專案沒封存（<see cref="KnowledgeErrorCodes.SpaceArchived"/>）、
    /// 專案還沒有空間（<see cref="KnowledgeErrorCodes.SpaceProjectTaken"/>）。
    /// 權限先於內容的檢查；代號、名稱、簡介的問題一次回齊（422）。代號已經被用了（含封存的）是 <see cref="KnowledgeErrorCodes.SpaceKeyTaken"/>；
    /// 組織已經有 <see cref="KnowledgeRules.MaxSpacesPerOrganization"/> 個是 <see cref="KnowledgeErrorCodes.SpaceLimitReached"/>。
    /// 同一個組織同時建立時排隊做（組織的 advisory lock），上限、代號與「一個專案一個空間」不會被同時的寫入繞過。
    /// </summary>
    Task<Result<SpaceSummary>> CreateAsync(Actor actor, NewSpace space, CancellationToken cancellationToken);

    /// <summary>
    /// 改名稱與簡介（整份取代）。要 <see cref="SpacePermissions.CanManage"/>（Forbidden）；封存的空間也能改。
    /// 和現在一樣時什麼都不做。名稱、簡介的檢查同建立。
    /// </summary>
    Task<Result<SpaceSummary>> UpdateAsync(Actor actor, string spaceKey, SpaceSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// 封存或取消封存。要 <see cref="SpacePermissions.CanManage"/>。已經是那個狀態時什麼都不做（重送不會失敗）。
    /// 綁的專案封存時空間本來就唯讀，取消封存空間也還是唯讀（專案取消封存才能寫）。
    /// </summary>
    Task<Result<SpaceSummary>> SetArchivedAsync(Actor actor, string spaceKey, bool archived, CancellationToken cancellationToken);
}
