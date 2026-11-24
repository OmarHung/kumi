using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications;

/// <summary>
/// 關注項目。呼叫端（API）已經用工作引擎確認過看得到這個項目，這裡只管自己的那一列，
/// 加上「是不是專案成員、看不看得到這個項目」：只有成員收得到通知（事件的讀者用同一個判斷），不是成員就不讓關注，免得按了卻永遠收不到；
/// 項目看不到（只看自己的角色，項目不是他負責或建立的）也一樣，照不存在處理。
/// </summary>
internal sealed class ItemSubscriptionService(
    IDbContextFactory<NotificationsDbContext> contexts, IWorkAccessQueries access, TimeProvider time)
    : IItemSubscriptionService
{
    private static readonly Failure NotMember = new(
        FailureKind.Conflict, NotificationErrorCodes.NotProjectMember, "要先加入這個專案，才能關注它的項目。");

    private static readonly Failure ItemNotFound = new(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, "找不到這個項目。");

    public async Task<ItemSubscription> GetAsync(Actor actor, Guid projectId, Guid itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!await IsMemberAsync(actor, projectId, cancellationToken) || !await CanSeeItemAsync(actor, itemId, cancellationToken))
        {
            // 以前是成員、後來被移出時可能還留著一列：不算在關注，也不收通知。
            return new ItemSubscription(false, null, CanFollow: false);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await ReadAsync(db, actor, itemId, cancellationToken);
    }

    public async Task<Result<ItemSubscription>> SetAsync(
        Actor actor, Guid projectId, Guid itemId, bool following, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!await IsMemberAsync(actor, projectId, cancellationToken))
        {
            return NotMember;
        }

        if (!await CanSeeItemAsync(actor, itemId, cancellationToken))
        {
            return ItemNotFound;
        }

        var now = time.GetUtcNow();
        var unsubscribed = !following;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 一句 upsert：同時按兩次也不會撞到唯一限制。已經在關注時保留原本的原因（例如「負責人」），
        // 取消後再關注才算自己按的；從沒關注過的人取消也要留一列，之後才不會被自動加回來。
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications.subscriptions
                (organization_id, project_id, item_id, person_id, reason, is_unsubscribed, created_at)
            VALUES ({actor.OrganizationId}, {projectId}, {itemId}, {actor.PersonId}, 'manual', {unsubscribed}, {now})
            ON CONFLICT (item_id, person_id) DO UPDATE SET
                reason = CASE WHEN subscriptions.is_unsubscribed AND NOT EXCLUDED.is_unsubscribed
                              THEN 'manual' ELSE subscriptions.reason END,
                is_unsubscribed = EXCLUDED.is_unsubscribed
            WHERE subscriptions.organization_id = EXCLUDED.organization_id
            """, cancellationToken);
        return await ReadAsync(db, actor, itemId, cancellationToken);
    }

    private static async Task<ItemSubscription> ReadAsync(
        NotificationsDbContext db, Actor actor, Guid itemId, CancellationToken cancellationToken)
    {
        var row = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(
            s => s.ItemId == itemId && s.PersonId == actor.PersonId && s.OrganizationId == actor.OrganizationId,
            cancellationToken);
        return row is null || row.IsUnsubscribed ? new ItemSubscription(false, null, true) : new ItemSubscription(true, row.Reason, true);
    }

    private async Task<bool> CanSeeItemAsync(Actor actor, Guid itemId, CancellationToken cancellationToken) =>
        (await access.ReadableItemIdsAsync(actor, [itemId], cancellationToken)).Contains(itemId);

    private async Task<bool> IsMemberAsync(Actor actor, Guid projectId, CancellationToken cancellationToken) =>
        (await access.FilterReadersAsync(actor.OrganizationId, projectId, [actor.PersonId], cancellationToken)).Contains(actor.PersonId);
}
