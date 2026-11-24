using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Events;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Activities;

internal sealed partial class ItemActivityService(
    IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, TimeProvider time) : IItemActivityService
{
    public async Task<Result<ItemActivityPage>> ListAsync(Actor actor, string itemKey, ActivityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemRead, forUpdate: false, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        if (query.Limit is < 1 or > ActivityQuery.MaxLimit)
        {
            return WorkFailures.QueryInvalid(QueryPaths.Limit, $"每頁的數量要在 1 到 {ActivityQuery.MaxLimit} 之間。");
        }

        if (!ActivityCursor.TryDecode(query.Cursor, out var after))
        {
            return WorkFailures.QueryInvalid(QueryPaths.Cursor, "分頁的游標不對，請從第一頁重新讀取。");
        }

        if (query.Kind is { } undefined && !Enum.IsDefined(undefined))
        {
            return WorkFailures.QueryInvalid(QueryPaths.Kind, "不認得的活動種類。");
        }

        var item = target.Value.Item;
        var activities = db.ItemActivities
            .AsNoTracking()
            .Where(a => a.OrganizationId == actor.OrganizationId && a.ItemId == item.Id);

        // 資料庫裡存的是固定的文字；先在這裡換成文字，查詢才翻得成 SQL。
        if (query.Kind is { } kind)
        {
            var kindText = ActivityRecords.KindText(kind);
            activities = activities.Where(a => a.Kind == kindText);
        }

        // 多拿一筆，看得出還有沒有下一頁，不必另外算總數。
        var rows = await After(activities, after)
            .OrderBy(a => a.At)
            .ThenBy(a => a.Id)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(query.Limit).ToArray();
        var next = rows.Count > query.Limit ? ActivityCursor.Encode(page[^1]) : null;
        var key = ItemKeys.Format(target.Value.Access.Project.Key, item.Number);
        return new ItemActivityPage([.. page.Select(a => ActivityRecords.ToActivity(a, key))], next);
    }

    public async Task<Result<ItemActivity>> AddCommentAsync(Actor actor, string itemKey, string body, Guid? replyTo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 和修改一樣先鎖住項目那一列，拿到鎖之後才讀時間：修改是在鎖裡讀時間、之後才提交的，
        // 留言不排隊的話，時間較晚的留言可能先提交，拿著停在留言的游標往下翻的人就看不到那筆修改。
        // 先確認看得到項目：看不到的一律 NotFound，不能讓沒有權限的人從 Forbidden 分辨出項目存在。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemRead, forUpdate: true, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, _) = target.Value;
        if (!access.Can(WorkPermissions.CommentCreate))
        {
            return access.Missing(WorkPermissions.CommentCreate);
        }

        var comment = CommentRules.Check(body);
        if (comment.Failure is { } invalid)
        {
            return invalid;
        }

        var thread = await FindThreadAsync(db, actor, item, replyTo, cancellationToken);
        if (thread.Failure is { } notFound)
        {
            return notFound;
        }

        // 留言只新增一列活動，不動項目本身：updatedAt 是項目的內容最後一次改變的時間。
        var activity = ActivityRecords.Commented(actor, item, WorkClock.Now(time), comment.Value, thread.Value?.Id);
        db.ItemActivities.Add(activity);
        var mentioned = await Mentions.NotifiableAsync(db, access, actor, item, null, comment.Value, cancellationToken);
        db.Events.Add(ItemEvents.Commented(actor, access, item, activity.At, comment.Value, mentioned, activity.Id, thread.Value?.Id, thread.Value?.ActorId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ActivityRecords.ToActivity(activity, ItemKeys.Format(access.Project.Key, item.Number));
    }

    /// <summary>
    /// 回覆的那一串的最上層留言；不是回覆時是 null。對象不存在、不是這個項目的、或不是留言都是同一個答案，
    /// 不讓人從差別猜到別的項目有什麼。回覆一則回覆時改成它的最上層：回覆只有一層。
    /// </summary>
    private static async Task<Result<ItemActivityEntity?>> FindThreadAsync(
        WorkDbContext db, Actor actor, ItemEntity item, Guid? replyTo, CancellationToken cancellationToken)
    {
        if (replyTo is not { } id)
        {
            return (ItemActivityEntity?)null;
        }

        var commented = ActivityRecords.CommentedKind;
        var target = await db.ItemActivities.AsNoTracking().FirstOrDefaultAsync(
            a => a.Id == id && a.OrganizationId == actor.OrganizationId && a.ItemId == item.Id && a.Kind == commented,
            cancellationToken);
        if (target is null)
        {
            return CommentNotFound();
        }

        if (target.DeletedAt is not null)
        {
            return ReplyToDeleted();
        }

        if (target.ReplyToId is not { } topId)
        {
            return target;
        }

        var top = await db.ItemActivities.AsNoTracking().FirstOrDefaultAsync(
            a => a.Id == topId && a.OrganizationId == actor.OrganizationId && a.ItemId == item.Id, cancellationToken);
        return top is null ? CommentNotFound() : top.DeletedAt is not null ? ReplyToDeleted() : top;
    }

    private static Failure CommentNotFound() => new(FailureKind.NotFound, WorkErrorCodes.CommentNotFound, "找不到要回覆的留言。");

    private static Failure ReplyToDeleted() => new(FailureKind.Conflict, WorkErrorCodes.CommentDeleted, "這則留言已經刪除，復原之前不能回覆。");

    /// <summary>從上一頁最後一筆之後開始：時間比它晚，或時間相同、編號比它大。</summary>
    private static IQueryable<ItemActivityEntity> After(IQueryable<ItemActivityEntity> activities, ActivityPosition? after)
    {
        if (after is not { } position)
        {
            return activities;
        }

        var (at, id) = position;
        return activities.Where(a => a.At > at || (a.At == at && a.Id.CompareTo(id) > 0));
    }
}
