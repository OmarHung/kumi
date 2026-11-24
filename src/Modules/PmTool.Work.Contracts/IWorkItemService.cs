using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 工作項目的所有操作。每個方法都自己做權限檢查（看 actor 在專案裡的角色），
/// 不管是從哪個入口進來的都一樣。看不到的專案或項目一律回 NotFound。
/// </summary>
public interface IWorkItemService
{
    Task<Result<WorkItem>> CreateAsync(Actor actor, string projectKey, NewWorkItem item, CancellationToken cancellationToken);

    Task<Result<WorkItem>> GetAsync(Actor actor, string itemKey, CancellationToken cancellationToken);

    Task<Result<WorkItem>> UpdateAsync(Actor actor, string itemKey, WorkItemPatch patch, CancellationToken cancellationToken);

    /// <summary>
    /// 改狀態。會檢查：流程有沒有這條轉換、呼叫者的專案角色在不在轉換允許的角色裡、
    /// 轉換要求先填的欄位是不是都有值。不通過時說明原因，並在 Details 列出是哪些欄位或角色。
    /// </summary>
    Task<Result<WorkItem>> TransitionAsync(Actor actor, string itemKey, string toState, CancellationToken cancellationToken);

    /// <summary>
    /// 刪除項目。資料不真的刪掉，而是封存：記下刪除的人與時刻，之後所有的讀取、清單、「我的工作」、計數、
    /// 變更集的影響與搬移都當它不存在（讀它一律 NotFound）；活動紀錄留著。編號不會再發給別的項目。
    /// 還有沒刪除的下層項目時擋下（<see cref="WorkErrorCodes.ItemHasChildren"/>），不連帶刪除。需要 item.delete。
    /// </summary>
    Task<Result<DeletedWorkItem>> DeleteAsync(Actor actor, string itemKey, CancellationToken cancellationToken);

    /// <summary>
    /// 復原刪除的項目，需要 item.delete（能刪除的人才能復原）。項目沒有刪除時什麼都不做，回傳它（不留活動），重送不會出錯。
    /// 編號、建立時間、說明、留言與活動照舊；週期或里程碑在這段期間被刪掉的，已經是沒有。
    /// 上層也是刪除的時候擋下（<see cref="WorkErrorCodes.ItemParentDeleted"/>，先復原上層）；藍圖已經沒有它的類型時擋下（<see cref="WorkErrorCodes.ItemTypeRemoved"/>）。
    /// 刪除之後藍圖改過的部分照 <see cref="ActivityKind.Restored"/> 的說明搬移，留一筆 Restored 活動、寫一筆 <see cref="WorkEventKind.ItemRestored"/> 事件。
    /// 找不到、看不到、或從來沒有這個編號都是 NotFound。
    /// </summary>
    Task<Result<WorkItem>> RestoreAsync(Actor actor, string itemKey, CancellationToken cancellationToken);

    /// <summary>
    /// 專案裡已刪除的項目，最近刪除的在前（同一時刻依編號遞減），需要 item.read。不含說明與自訂欄位。
    /// <paramref name="query"/> 的 Limit 是 1 到 <see cref="DeletedItemQuery.MaxLimit"/>；游標不合格是驗證錯誤，路徑是 cursor。
    /// </summary>
    Task<Result<DeletedItemPage>> ListDeletedAsync(Actor actor, string projectKey, DeletedItemQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 改類型。需要 item.update；類型沒變時什麼都不做，也不留活動。
    /// 狀態由伺服器決定，呼叫端不能指定，改類型才不會變成繞過轉換限制的捷徑：新流程有同 key、同類別的狀態就留著；
    /// 否則新流程沒有限制轉換、而且呼叫者有 item.transition 時，搬到新流程裡第一個同類別的狀態（沒有同類別就是第一個狀態）；
    /// 其他情況一律搬到新流程的第一個狀態（新項目的起始狀態），不能借改類型跳過轉換的角色限制與必填欄位。
    /// 新類型沒有的自訂欄位，值封存起來（和變更集拿掉欄位一樣）。上層的類型要在新類型允許的上層裡，
    /// 每個沒刪除的下層的類型都要允許掛在新類型底下，否則擋下。留一筆 <see cref="ActivityKind.TypeChanged"/>。
    /// </summary>
    Task<Result<WorkItem>> ChangeTypeAsync(Actor actor, string itemKey, string type, CancellationToken cancellationToken);

    Task<Result<WorkItemPage>> ListAsync(Actor actor, string projectKey, WorkItemQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 「我的工作」：呼叫者負責、還沒結束（狀態類別不是 done 或 canceled）的項目，跨所有他看得到的專案。
    /// 依到期日排序，沒有到期日的排後面；最多回傳 <see cref="WorkItemQuery.MaxLimit"/> 個。
    /// </summary>
    Task<IReadOnlyList<WorkItem>> ListAssignedToMeAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 跨專案的項目清單：呼叫者看得到（組織管理員是全部；其他人是自己是成員、角色讀得到項目的）、沒有封存的專案裡，
    /// 符合條件的項目（不含已刪除的）。條件、分頁、驗證錯誤的路徑都和 <see cref="ListAsync"/> 一樣，差別只在排序：
    /// <list type="bullet">
    /// <item><c>Number</c>：依專案（建立的先後），再依編號。</item>
    /// <item><c>State</c>：依狀態類別的順序（backlog、not_started、in_progress、done、canceled），再依狀態 key。各專案的流程不同，沒有共同的「流程裡的位置」。</item>
    /// <item>其他欄位：和 <see cref="ListAsync"/> 一樣，空值排最後；同值再依專案、編號，分頁才穩定。</item>
    /// </list>
    /// 條件裡的週期、里程碑是編號，跨專案照樣可以用；上層（ParentKey）也一樣。
    /// <paramref name="scope"/> 的專案超過 <see cref="CrossProjectScope.MaxProjects"/> 個、或代號有控制字元，回 <see cref="WorkErrorCodes.QueryInvalid"/>（路徑 project）。
    /// </summary>
    Task<Result<WorkItemPage>> ListAcrossProjectsAsync(
        Actor actor, CrossProjectScope scope, WorkItemQuery query, CancellationToken cancellationToken);

    /// <summary>呼叫者看得到、沒有封存的專案用到的項目類型，依 key 合併（見 <see cref="ItemTypeSummary"/>），照 key 排。跨專案的「類型」篩選的選項。</summary>
    Task<IReadOnlyList<ItemTypeSummary>> ListItemTypesAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 在呼叫者看得到的所有專案裡，找標題包含這段文字、或編號就是它的項目（不含已刪除的）。
    /// 排序：編號完全符合的在最前面，再來是標題以它開頭的，再來是標題包含它的；同一層裡最近更新的在前。
    /// 放在項目服務而不是另開介面：「看得到」的判斷與項目的讀取模型都在這裡，不必再拆一份。
    /// <paramref name="text"/> 去掉前後空白後要有 1 到 200 個字；<paramref name="limit"/> 是 1 到 20；不合格的驗證錯誤路徑是 q 或 limit。
    /// </summary>
    Task<Result<IReadOnlyList<WorkItem>>> SearchAsync(Actor actor, string text, int limit, CancellationToken cancellationToken);
}
