using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications;

/// <summary>
/// 收件匣。哪些專案、哪些項目看得到由工作引擎決定（<see cref="IWorkAccessQueries"/>），這裡只列看得到的專案、看得到的項目的通知：
/// 被移出專案之後，舊的通知不再出現、也不算未讀；只看自己的角色在負責人換掉之後，那個項目的通知也一樣。
/// </summary>
internal sealed class NotificationInbox(
    IDbContextFactory<NotificationsDbContext> contexts, NotificationVisibility visibility, TimeProvider time) : INotificationInbox
{
    public async Task<Result<NotificationPage>> ListAsync(Actor actor, NotificationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        if (Validate(query) is { } invalid)
        {
            return invalid;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var notifications = await visibility.VisibleToAsync(db, actor, cancellationToken);
        if (query.UnreadOnly)
        {
            notifications = notifications.Where(n => n.ReadAt == null);
        }

        if (query.Cursor is not null)
        {
            NotificationCursor.TryDecode(query.Cursor, out var after);
            notifications = notifications.Where(n =>
                n.CreatedAt < after.CreatedAt || (n.CreatedAt == after.CreatedAt && n.Id.CompareTo(after.Id) < 0));
        }

        // 多讀一筆，才知道後面還有沒有。
        var rows = await notifications
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(query.Limit).ToList();
        var next = rows.Count > query.Limit ? new NotificationCursor(page[^1].CreatedAt, page[^1].Id).Encode() : null;
        return new NotificationPage([.. page.Select(ToItem)], next);
    }

    public async Task<int> CountUnreadAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await (await visibility.VisibleToAsync(db, actor, cancellationToken)).CountAsync(n => n.ReadAt == null, cancellationToken);
    }

    public async Task<int> MarkReadAsync(Actor actor, IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(notificationIds);

        Guid[] ids = [.. notificationIds.Distinct()];
        if (ids.Length == 0)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 條件裡有收件人：別人的編號、不存在的編號都不會被改到，也不會說出它們存不存在。
        return await db.Notifications
            .Where(n => n.OrganizationId == actor.OrganizationId && n.RecipientId == actor.PersonId
                && n.ReadAt == null && ids.Contains(n.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    public async Task<int> MarkAllReadAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await (await visibility.VisibleToAsync(db, actor, cancellationToken))
            .Where(n => n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    private static Failure? Validate(NotificationQuery query)
    {
        List<FailureDetail> problems = [];
        if (query.Limit is < 1 or > NotificationRules.MaxPageSize)
        {
            problems.Add(new FailureDetail(
                NotificationErrorCodes.QueryInvalid, "limit", $"limit 要在 1 到 {NotificationRules.MaxPageSize} 之間。"));
        }

        if (query.Cursor is not null && !NotificationCursor.TryDecode(query.Cursor, out _))
        {
            problems.Add(new FailureDetail(NotificationErrorCodes.QueryInvalid, "cursor", "cursor 不正確。"));
        }

        return problems.Count == 0
            ? null
            : new Failure(FailureKind.Invalid, NotificationErrorCodes.QueryInvalid, "查詢參數不正確。") { Details = problems };
    }

    private static NotificationItem ToItem(NotificationRow row) => new(
        row.Id, row.Kind, row.ProjectKey, row.ItemKey, row.ItemTitle, row.ActorPersonId,
        row.FromStateName, row.ToStateName, row.Excerpt, row.CreatedAt, row.ReadAt, row.CommentId)
    {
        Account = row.AccountNotice,
        Edited = row.ExcerptEditedAt is not null,
    };
}
