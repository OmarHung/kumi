using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 給別的模組（通知）用的存取查詢。工作引擎決定誰看得到哪個專案，別的模組不自己判斷。
/// 「看得到」和讀專案的權限一樣：專案成員，或組織的擁有者與管理員。
/// </summary>
public interface IWorkAccessQueries
{
    /// <summary>依編號找項目；看不到的專案、不存在或已刪除的項目都回 <see cref="WorkErrorCodes.ItemNotFound"/>。</summary>
    Task<Result<ItemReference>> ResolveItemAsync(Actor actor, string itemKey, CancellationToken cancellationToken);

    /// <summary>這些人裡面，現在看得到這個專案的有誰。給事件的讀者用（沒有做事的人，所以不收 Actor）。</summary>
    Task<IReadOnlySet<Guid>> FilterReadersAsync(
        Guid organizationId, Guid projectId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken);

    /// <summary>這個人現在看得到的專案。</summary>
    Task<IReadOnlySet<Guid>> ReadableProjectIdsAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 這些人裡面，現在看得到這個項目的有誰：看得到專案，而且角色看得到全部項目、或只看自己的（<see cref="WorkPermissions.ItemReadOwn"/>）
    /// 而這個項目是他負責或建立的。給事件的讀者用：通知、自動關注只給看得到項目的人。
    /// </summary>
    Task<IReadOnlySet<Guid>> FilterItemReadersAsync(
        Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken);

    /// <summary>這些項目裡，這個人現在看得到的（收件匣用：負責人換掉之後，只看自己的人就看不到那則通知的項目了）。不存在、已刪除的不算。</summary>
    Task<IReadOnlySet<Guid>> ReadableItemIdsAsync(Actor actor, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);

    /// <summary>
    /// 這個人只看得到自己項目的專案（角色有 <see cref="WorkPermissions.ItemReadOwn"/>、沒有 <see cref="WorkPermissions.ItemRead"/>）。
    /// 收件匣先問這個：空的（大多數人）就不必逐項確認看不看得到，已刪除項目的通知也照舊留著；
    /// 只有這些專案的通知才用 <see cref="ReadableItemIdsAsync"/> 確認。
    /// </summary>
    Task<IReadOnlySet<Guid>> OwnOnlyProjectIdsAsync(Actor actor, CancellationToken cancellationToken);
}

/// <summary>判斷「只看自己的」要的兩個人：建立的人與現在的負責人。事件裡就有（<see cref="ItemEventData.CreatedBy"/>、<see cref="ItemEventData.AssigneeId"/>）。</summary>
public sealed record ItemOwnership(Guid CreatedBy, Guid? AssigneeId);

/// <summary>找到的項目。</summary>
public sealed record ItemReference(Guid ProjectId, string ProjectKey, Guid ItemId, string ItemKey, string Title);
