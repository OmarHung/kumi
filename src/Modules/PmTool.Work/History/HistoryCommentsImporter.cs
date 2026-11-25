using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Activities;
using PmTool.Work.Common;
using PmTool.Work.Events;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.History;

/// <summary>
/// 一批歷史留言的處理。檢查與建立都在記憶體裡做，沒有問題才一起存：留言之間只靠外部編號對應（回覆指到同一批前面的那一則），
/// 不必像項目那樣先存進資料庫。留言的內容規則就是一般留言的（<see cref="CommentRules"/>）。
/// </summary>
internal sealed class HistoryCommentsImporter(
    WorkDbContext db, ProjectAccess access, Actor actor, HistoryComments import, IReadOnlySet<Guid> readable, TimeProvider time)
{
    private const string Collection = "comments";

    /// <summary>一則已經存在（之前匯入或這一批前面）的留言：回覆它時要知道它在哪個項目、最上層是誰。</summary>
    private sealed record KnownComment(Guid ActivityId, Guid ItemId, Guid TopId, Guid TopAuthorId);

    private readonly List<FailureDetail> _problems = [];
    private readonly List<HistoryCommentRef> _created = [];
    private readonly List<HistoryCommentRef> _skipped = [];
    private readonly Dictionary<string, KnownComment> _known = new(StringComparer.Ordinal);
    private readonly HashSet<string> _existing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly HistoryActivityIds _ids = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inBatch = [.. import.Comments.Select(c => c?.ExternalId).OfType<string>()];
    private Dictionary<string, HistoryRefRow> _items = [];
    private Dictionary<Guid, ItemEntity> _itemRows = [];
    private HashSet<Guid> _members = [];

    public IReadOnlyList<FailureDetail> Problems => _problems;

    public IReadOnlyList<HistoryCommentRef> Created => _created;

    public IReadOnlyList<HistoryCommentRef> Skipped => _skipped;

    /// <summary>檢查每一則並把要寫的東西加進變更追蹤（不儲存）。</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        for (var index = 0; index < import.Comments.Count; index++)
        {
            ImportComment(index, import.Comments[index]);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var comments = import.Comments.Where(c => c is not null).ToArray();
        _items = await HistoryRefs.FindAsync(
            db, actor.OrganizationId, import.Source, HistoryRefKind.Item, comments.Select(c => c.ItemExternalId), cancellationToken);
        var itemIds = _items.Values.Where(r => r.ProjectId == access.Project.Id).Select(r => r.ItemId).ToArray();

        // 範圍外的項目（只看自己項目的角色）當作不存在，不因為它有沒有被匯入過、有沒有刪除而有不同的回答；
        // 刪除的也讀進來，才分得出「看得到、但已經刪除」。
        _itemRows = await access.Items.Apply(db.Items.AsNoTracking())
            .Where(i => i.OrganizationId == actor.OrganizationId && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        _members = await HistoryRefs.MembersAmongAsync(db, access, comments.Select(c => c.AuthorId), cancellationToken);

        // 之前匯入過的留言（這一批裡的，與被回覆的）：略過的認得出來，回覆的對象知道最上層是誰。
        var refs = await HistoryRefs.FindAsync(
            db, actor.OrganizationId, import.Source, HistoryRefKind.Comment,
            [.. comments.Select(c => c.ExternalId), .. comments.Select(c => c.ReplyToExternalId)], cancellationToken);
        var activities = await LoadActivitiesAsync(refs.Values.Select(r => r.TargetId), cancellationToken);
        foreach (var (externalId, reference) in refs)
        {
            if (activities.TryGetValue(reference.TargetId, out var activity))
            {
                _existing.Add(externalId);
                if (!readable.Contains(reference.ProjectId))
                {
                    _hidden.Add(externalId);
                }

                _known[externalId] = KnownOf(activity, activities);
            }
        }
    }

    /// <summary>活動與它們的最上層留言（回覆的最上層可能不在要找的編號裡）。</summary>
    private async Task<Dictionary<Guid, ItemActivityEntity>> LoadActivitiesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.ToArray();
        var found = await FetchAsync(wanted, cancellationToken);
        var tops = found.Values.Where(a => a.ReplyToId is not null).Select(a => a.ReplyToId!.Value).Where(id => !found.ContainsKey(id)).ToArray();
        foreach (var (id, activity) in await FetchAsync(tops, cancellationToken))
        {
            found[id] = activity;
        }

        return found;
    }

    private async Task<Dictionary<Guid, ItemActivityEntity>> FetchAsync(Guid[] ids, CancellationToken cancellationToken) =>
        ids.Length == 0
            ? []
            : await db.ItemActivities.AsNoTracking()
                .Where(a => a.OrganizationId == actor.OrganizationId && ids.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken);

    private static KnownComment KnownOf(ItemActivityEntity activity, IReadOnlyDictionary<Guid, ItemActivityEntity> activities)
    {
        var top = activity.ReplyToId is { } topId && activities.TryGetValue(topId, out var found) ? found : activity;
        return new KnownComment(activity.Id, activity.ItemId, top.Id, top.ActorId);
    }

    private void ImportComment(int index, HistoryComment? comment)
    {
        if (comment is null)
        {
            Add(index, null, "這一則是空的。");
            return;
        }

        if (HistoryRules.ExternalIdProblem(comment.ExternalId) is { } idProblem)
        {
            Add(index, "external_id", idProblem);
            return;
        }

        if (!_seen.Add(comment.ExternalId))
        {
            Add(index, "external_id", $"外部編號{TextRules.Quote(comment.ExternalId)}在這一批裡出現不只一次。");
            return;
        }

        if (_hidden.Contains(comment.ExternalId))
        {
            Add(index, "external_id", HistoryRules.ImportedElsewhere(comment.ExternalId));
            return;
        }

        if (_existing.Contains(comment.ExternalId))
        {
            _skipped.Add(new HistoryCommentRef(comment.ExternalId, _known[comment.ExternalId].ActivityId));
            return;
        }

        var before = _problems.Count;
        var item = ItemOf(index, comment);
        var body = CommentRules.Check(comment.Body);
        if (body.Failure is { } invalidBody)
        {
            _problems.AddRange(invalidBody.Details.Select(d => HistoryRules.Problem(Collection, index, d.Path, d.Message)));
        }

        var at = HistoryRules.Normalize(comment.At);
        if (HistoryRules.TimeProblem(at, time) is { } timeProblem)
        {
            Add(index, "at", timeProblem);
        }

        if (!_members.Contains(comment.AuthorId))
        {
            Add(index, "author_id", "留言的作者要是這個專案的成員，請先把這個人加進專案。");
        }

        var thread = ThreadOf(index, comment, item);
        if (_problems.Count == before && item is not null)
        {
            Insert(comment, item, at, body.Value, thread);
        }
    }

    /// <summary>留言所在的項目：要是之前匯入過、在這個專案、呼叫者看得到、沒刪除的。看不到的和沒匯入過的是同一個回答。</summary>
    private ItemEntity? ItemOf(int index, HistoryComment comment)
    {
        if (!_items.TryGetValue(comment.ItemExternalId ?? string.Empty, out var reference))
        {
            return NotImported(index, comment);
        }

        if (reference.ProjectId != access.Project.Id)
        {
            // 看不到的專案不透露它的專案代號。
            var message = readable.Contains(reference.ProjectId)
                ? $"項目{TextRules.Quote(comment.ItemExternalId)}匯入到別的專案（{reference.ProjectKey}），留言要匯到那個專案。"
                : $"項目{TextRules.Quote(comment.ItemExternalId)}已經匯入到別的專案，留言要匯到那個專案。";
            Add(index, "item_external_id", message);
            return null;
        }

        if (!_itemRows.TryGetValue(reference.ItemId, out var item))
        {
            return NotImported(index, comment);
        }

        if (item.DeletedAt is not null)
        {
            Add(index, "item_external_id", $"項目{reference.ItemKey}已經刪除，不能再加留言。");
            return null;
        }

        return item;
    }

    private ItemEntity? NotImported(int index, HistoryComment comment)
    {
        Add(index, "item_external_id", $"找不到項目{TextRules.Quote(comment.ItemExternalId)}：項目要先匯入。");
        return null;
    }

    /// <summary>回覆的對象；最上層的留言是 null。指到回覆時換成它的最上層（回覆只有一層）。</summary>
    private KnownComment? ThreadOf(int index, HistoryComment comment, ItemEntity? item)
    {
        if (comment.ReplyToExternalId is not { } replyTo)
        {
            return null;
        }

        if (!_known.TryGetValue(replyTo, out var target))
        {
            var message = _inBatch.Contains(replyTo)
                ? $"回覆的留言{TextRules.Quote(replyTo)}要排在這一則的前面。"
                : $"找不到要回覆的留言{TextRules.Quote(replyTo)}：它要在這一批的前面，或之前已經匯入過。";
            Add(index, "reply_to_external_id", message);
            return null;
        }

        if (item is not null && target.ItemId != item.Id)
        {
            Add(index, "reply_to_external_id", $"留言{TextRules.Quote(replyTo)}不在同一個項目裡，不能回覆。");
            return null;
        }

        return target;
    }

    private void Insert(HistoryComment comment, ItemEntity item, DateTimeOffset at, string body, KnownComment? thread)
    {
        var author = actor with { PersonId = comment.AuthorId };
        var activity = _ids.Assign(ActivityRecords.Commented(author, item, at, body, thread?.TopId, import.Source));

        // 歷史不通知、不提及任何人，所以提及的人是空的。
        db.ItemActivities.Add(activity);
        db.Events.Add(ItemEvents.Commented(
            author, access, item, at, body, [], activity.Id, thread?.TopId, thread?.TopAuthorId, history: true));
        db.HistoryRefs.Add(HistoryRefs.New(
            actor, import.Source, HistoryRefKind.Comment, comment.ExternalId, access.Project.Id, item.Id, activity.Id, WorkClock.Now(time)));
        _known[comment.ExternalId] = new KnownComment(activity.Id, item.Id, thread?.TopId ?? activity.Id, thread?.TopAuthorId ?? author.PersonId);
        _created.Add(new HistoryCommentRef(comment.ExternalId, activity.Id));
    }

    private void Add(int index, string? field, string message) => _problems.Add(HistoryRules.Problem(Collection, index, field, message));
}
