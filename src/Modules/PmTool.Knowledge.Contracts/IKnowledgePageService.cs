using PmTool.Abstractions;

namespace PmTool.Knowledge;

/// <summary>
/// 知識庫的頁面（internal/HANDOFF-knowledge-base.md 第 4 節）。
/// <para>
/// 看得到一頁 = 看得到它的空間，而且是組織管理員，或者這一頁與它每一層上層裡自己設了限制的，每一份名單都有他（限制往下繼承，只能更嚴，不能放寬）。
/// 規則只有模組裡的一份（<c>Access/PageScope</c>），樹、單頁、垃圾桶、附件、版本、留言、搜尋都用它；看不到的頁面一律當作不存在
/// （<see cref="KnowledgeErrorCodes.PageNotFound"/>）。垃圾桶裡的頁面照它原本的位置算（上層在垃圾桶裡也照算）。
/// </para>
/// <para>
/// 寫入（新增、修改、搬移、刪除、復原、改限制、上傳附件）要看得到那一頁，加上空間的 <see cref="SpacePermissions.CanEdit"/>：
/// 看得到卻不能改是 <see cref="KnowledgeErrorCodes.Forbidden"/>；空間或它的專案封存了是 <see cref="KnowledgeErrorCodes.SpaceArchived"/>（先檢查這個）。
/// 同一個空間的樹狀修改（新增、搬移、刪除、復原）在空間的 advisory lock 裡排隊做；改內容鎖住那一頁。
/// </para>
/// </summary>
public interface IKnowledgePageService
{
    /// <summary>空間裡看得到的頁面（不含垃圾桶裡的），先序排列（見 <see cref="PageTree"/>）。</summary>
    Task<Result<PageTree>> GetTreeAsync(Actor actor, string spaceKey, CancellationToken cancellationToken);

    /// <summary>讀一頁。在垃圾桶裡的也是 <see cref="KnowledgeErrorCodes.PageNotFound"/>（垃圾桶另外列）。</summary>
    Task<Result<PageDetail>> GetAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken);

    /// <summary>
    /// 新增一頁，回傳新的頁面（<see cref="PageDetail.Revision"/> 是 1，同時寫下第 1 版）。編號是空間裡的下一號。
    /// 上層（<see cref="NewPage.ParentNumber"/>）要看得到、不在垃圾桶裡（不然 422 <see cref="KnowledgeErrorCodes.PagePositionInvalid"/>，路徑 parentNumber）；
    /// 放下去超過 <see cref="KnowledgeRules.MaxDepth"/> 層是 <see cref="KnowledgeErrorCodes.PageTooDeep"/>；
    /// <see cref="NewPage.BeforeNumber"/> 要是那個上層底下看得到的頁面（路徑 beforeNumber）。
    /// 標題與內文的問題一次回齊（422）。空間裡已經有 <see cref="KnowledgeRules.MaxPagesPerSpace"/> 頁是 <see cref="KnowledgeErrorCodes.PageLimitReached"/>。
    /// 新的頁面沒有自己的限制，繼承上層的。
    /// </summary>
    Task<Result<PageDetail>> CreateAsync(Actor actor, string spaceKey, NewPage page, CancellationToken cancellationToken);

    /// <summary>
    /// 改標題或內文。先比對 <see cref="PageChanges.ExpectedRevision"/>，不同是 <see cref="KnowledgeErrorCodes.PageConflict"/>，什麼都不改。
    /// 內容和現在一樣時什麼都不做（版本、更新的時刻與人都不變）。有改時 revision 加一、更新的時刻與人換成這次的，並寫版本：
    /// 最新的一版是同一個人在 <see cref="KnowledgeRules.VersionMergeWindow"/> 內存的（而且不是還原出來的）就更新那一版，不然新增一版；
    /// 超過 <see cref="KnowledgeRules.MaxVersionsPerPage"/> 版時刪掉多出來的：第 1 版、最新的一版、組織時區每一天最早與最晚的一版最後才動，
    /// 一天裡被大量存檔洗掉的只有中間的版本。
    /// 內文換行統一成 \n，不去掉頭尾空白（縮排是內容）。
    /// </summary>
    Task<Result<PageDetail>> UpdateAsync(Actor actor, string spaceKey, int pageNumber, PageChanges changes, CancellationToken cancellationToken);

    /// <summary>
    /// 搬到別的位置（同一個空間裡），連同底下的頁面。新的上層不能是自己或自己底下的（<see cref="KnowledgeErrorCodes.PagePositionInvalid"/>），
    /// 其他位置的規則同新增；搬了之後最深的子頁面超過 <see cref="KnowledgeRules.MaxDepth"/> 層是 <see cref="KnowledgeErrorCodes.PageTooDeep"/>。
    /// 底下有呼叫者看不到的頁面（垃圾桶裡的也算）是 <see cref="KnowledgeErrorCodes.PageHasHiddenDescendants"/>，什麼都不搬（搬出去會改變那些頁面誰看得到）；組織管理員不受限。搬到原本的位置什麼都不做。revision 不變。回傳搬完的樹。
    /// </summary>
    Task<Result<PageTree>> MoveAsync(Actor actor, string spaceKey, int pageNumber, PageMove move, CancellationToken cancellationToken);

    /// <summary>
    /// 把這一頁連同底下的頁面放進垃圾桶，同一批（<see cref="PageDeletion.DeletionId"/>）。之前已經在垃圾桶裡的子頁面維持它們自己那一批。
    /// 底下有看不到的頁面是 <see cref="KnowledgeErrorCodes.PageHasHiddenDescendants"/>，什麼都不刪。
    /// 垃圾桶已經有 <see cref="KnowledgeRules.MaxTrashBatchesPerSpace"/> 批、或頁數會超過 <see cref="KnowledgeRules.MaxTrashPagesPerSpace"/> 頁是 <see cref="KnowledgeErrorCodes.TrashFull"/>，要先永久刪除（<see cref="PurgeTrashAsync"/>）。
    /// </summary>
    Task<Result<PageDeletion>> DeleteAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken);

    /// <summary>
    /// 把這一頁所在的那一批從垃圾桶拿回來，回傳這一頁。放回原本的上層的最後面；原本的上層不在了（在垃圾桶裡）就放到最上層的最後面。
    /// 頁面不在垃圾桶裡時直接回傳它（重送不會失敗）。拿回來會超過 <see cref="KnowledgeRules.MaxPagesPerSpace"/> 是 <see cref="KnowledgeErrorCodes.PageLimitReached"/>。
    /// 看不到這一頁（照原本的位置算）是 NotFound。
    /// 放不回原本的上層時，原本上層鏈上不再是祖先的那幾層的限制，取交集變成這一頁自己的限制（交集是空的只留執行的人），復原不會讓更多人看到。
    /// </summary>
    Task<Result<PageDetail>> RestoreAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken);

    /// <summary>
    /// 垃圾桶：看得到的每一批，新刪的在前，分頁（只讀每一批最上面那一頁，不把整個垃圾桶讀進來）。
    /// <see cref="TrashQuery.Cursor"/> 格式不對、或 <see cref="TrashQuery.Limit"/> 不在 1～<see cref="KnowledgeRules.MaxTrashPageSize"/>，是 422 <c>query.invalid</c>。
    /// 批數與上限（<see cref="TrashPage.Capacity"/>）只有空間的管理者看得到。
    /// </summary>
    Task<Result<TrashPage>> ListTrashAsync(Actor actor, string spaceKey, TrashQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 永久刪除垃圾桶裡的頁面（含版本、限制名單、附件的資料與檔案），不能復原。只有空間的管理者能做（<see cref="KnowledgeErrorCodes.Forbidden"/>），
    /// 空間唯讀時是 <see cref="KnowledgeErrorCodes.SpaceArchived"/>。
    /// <see cref="TrashPurgeRequest.Batch"/> 只刪那一批（不在垃圾桶裡是 <see cref="KnowledgeErrorCodes.PageNotFound"/>）；
    /// <see cref="TrashPurgeRequest.Expired"/> 清掉刪除超過 <see cref="KnowledgeRules.TrashPurgeAge"/> 的批，最舊的在前，
    /// 一次最多 <see cref="KnowledgeRules.MaxBatchesPerPurge"/> 批、累計超過 <see cref="KnowledgeRules.MaxPagesPerPurge"/> 頁就停（至少清一批，一批不拆）；
    /// 還有到期而清得掉的批時 <see cref="TrashPurge.HasMoreExpired"/> 是 true，可以再清一次。
    /// 刪了之後會讓還留著的頁面找不到原本上層的批這次不刪（指定的那一批底下還有留著的頁面時，結果是什麼都沒刪、計數是 0）。
    /// </summary>
    Task<Result<TrashPurge>> PurgeTrashAsync(Actor actor, string spaceKey, TrashPurgeRequest request, CancellationToken cancellationToken);

    /// <summary>讀一頁的限制。看得到這一頁就能讀。</summary>
    Task<Result<PageRestriction>> GetRestrictionAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken);

    /// <summary>
    /// 設定這一頁自己的限制（整份取代）。null 或空的是拿掉。有名單時一定包含設定的人（沒列也自動加在最前面），重複的去掉；
    /// 底下有呼叫者看不到的頁面（垃圾桶裡的也算，它們沿現在的上層算誰看得到）是 <see cref="KnowledgeErrorCodes.PageHasHiddenDescendants"/>，什麼都不改（組織管理員不受限）。
    /// 人數（含自動加的）超過 <see cref="KnowledgeRules.MaxRestrictedPeople"/>、或有人不在這個組織，是 422 <see cref="KnowledgeErrorCodes.RestrictionInvalid"/>
    /// （路徑 people 或 people[i]）。名單上的人還要看得到空間才真的看得到（不另外檢查）。和現在一樣時什麼都不做。revision 不變。
    /// 放寬（拿掉限制、名單裡加了原本沒有的人）只有設定這個限制的人（<see cref="PageRestriction.RestrictedBy"/>）與空間的管理者能做，
    /// 其他人是 403 <see cref="KnowledgeErrorCodes.RestrictionOwnerRequired"/>；收緊（只刪人）名單上的人都能做，而且不換擁有者。
    /// 搬移也一樣：搬出有限制的範圍、讓更多人看得到時，不是設定那些限制的人（或管理者）是 403 <see cref="KnowledgeErrorCodes.RestrictionOwnerRequired"/>。
    /// </summary>
    Task<Result<PageRestriction>> SetRestrictionAsync(
        Actor actor, string spaceKey, int pageNumber, IReadOnlyList<Guid>? people, CancellationToken cancellationToken);
}
