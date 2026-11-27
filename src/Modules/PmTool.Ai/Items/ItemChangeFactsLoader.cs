using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 查修改建議檢查要用的東西（<see cref="ItemChangeFacts"/>）。一律以建立建議的人的身分查：看不到的上層、讀不到的週期
/// 都和不存在一樣。只查這份建議真的用到的，沒給對應欄位就一個查詢都不送。
/// </summary>
internal sealed class ItemChangeFactsLoader(
    IWorkItemService items, IPlanningService planning, IItemActivityService activity, IPeopleDirectory people)
{
    // 類型最多 20 種、上層關係沒有圈，正常的資料不會超過這個深度；超過就不再往上找。
    private const int MaxChainDepth = 100;

    private const int MaxChildren = 200;

    private const int MaxCommentPages = 10;

    private const string UnknownPerson = "（已不在組織）";

    public async Task<ItemChangeFacts> LoadAsync(
        Actor actor,
        WorkItem item,
        IReadOnlyList<ItemFieldChange> changes,
        string? comment,
        Guid? replyTo,
        CancellationToken cancellationToken)
    {
        var valid = changes.Where(c => c is not null && c.Field is not null).ToList();
        var facts = ItemChangeFacts.None;
        facts = await LoadParentAsync(actor, item, valid, facts, cancellationToken);
        facts = await LoadTypeNeighborsAsync(actor, item, valid, facts, cancellationToken);
        facts = await LoadPlanningAsync(actor, item.ProjectKey, valid, facts, cancellationToken);
        return await LoadReplyAsync(actor, item, comment, replyTo, facts, cancellationToken);
    }

    private async Task<ItemChangeFacts> LoadParentAsync(
        Actor actor, WorkItem item, List<ItemFieldChange> changes, ItemChangeFacts facts, CancellationToken cancellationToken)
    {
        if (StringValue(changes, ItemFieldNames.ParentKey) is not { } key)
        {
            return facts;
        }

        var found = await ItemLookup.FindAsync(items, actor, item.ProjectKey, key, cancellationToken);
        if (found.Failure is not null)
        {
            return facts;
        }

        var parent = found.Value;
        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { parent.Key };
        var next = parent.ParentKey;
        for (var depth = 0; depth < MaxChainDepth && next is not null && chain.Add(next); depth++)
        {
            var above = await items.GetAsync(actor, next, cancellationToken);
            next = above.Failure is null ? above.Value.ParentKey : null;
        }

        return facts with { NewParent = parent, NewParentChain = chain };
    }

    // 改類型要看現在的上層（沒有一起改上層時）與每個下層允不允許，這些在建立建議時就先擋。
    private async Task<ItemChangeFacts> LoadTypeNeighborsAsync(
        Actor actor, WorkItem item, List<ItemFieldChange> changes, ItemChangeFacts facts, CancellationToken cancellationToken)
    {
        if (StringValue(changes, ItemFieldNames.Type) is null)
        {
            return facts;
        }

        WorkItem? currentParent = null;
        if (item.ParentKey is { } parentKey && !changes.Any(c => c.Field == ItemFieldNames.ParentKey))
        {
            var found = await items.GetAsync(actor, parentKey, cancellationToken);
            currentParent = found.Failure is null ? found.Value : null;
        }

        var children = await items.ListAsync(
            actor, item.ProjectKey, new WorkItemQuery { ParentKey = item.Key, Limit = MaxChildren }, cancellationToken);
        return facts with { CurrentParent = currentParent, Children = children.Failure is null ? children.Value.Items : [] };
    }

    private async Task<ItemChangeFacts> LoadPlanningAsync(
        Actor actor, string projectKey, List<ItemFieldChange> changes, ItemChangeFacts facts, CancellationToken cancellationToken)
    {
        if (StringValue(changes, ItemFieldNames.CycleId) is not null)
        {
            var listed = await planning.ListCyclesAsync(actor, projectKey, cancellationToken);
            facts = facts with { Cycles = listed.Failure is null ? listed.Value.Select(c => c.Id).ToHashSet() : null };
        }

        if (StringValue(changes, ItemFieldNames.MilestoneId) is not null)
        {
            var listed = await planning.ListMilestonesAsync(actor, projectKey, cancellationToken);
            facts = facts with { Milestones = listed.Failure is null ? listed.Value.Select(m => m.Id).ToHashSet() : null };
        }

        return facts;
    }

    // 沒有 comment 的 replyTo 由檢查器擋，這裡不查。找不到、別的項目的、不是留言的都是同一種說明。
    private async Task<ItemChangeFacts> LoadReplyAsync(
        Actor actor, WorkItem item, string? comment, Guid? replyTo, ItemChangeFacts facts, CancellationToken cancellationToken)
    {
        if (replyTo is not { } commentId || string.IsNullOrWhiteSpace(comment))
        {
            return facts;
        }

        var found = await FindCommentAsync(actor, item.Key, commentId, cancellationToken);
        if (found is null)
        {
            return facts with { ReplyProblem = "找不到這則留言。replyTo 要是這個項目的留言 id（get_item 或 list_comments 回的）。" };
        }

        if (found.DeletedAt is not null)
        {
            return facts with { ReplyProblem = "這則留言已經刪除，不能回覆；復原之前這一串不能再回覆。" };
        }

        var names = await people.FindManyAsync(actor.OrganizationId, [found.ActorId], cancellationToken);
        var author = names.TryGetValue(found.ActorId, out var person) ? person.DisplayName : UnknownPerson;
        var excerpt = AiText.Cut(found.Comment ?? string.Empty, AiRules.MaxCommentExcerptLength);
        return facts with { Reply = new CommentReplyTarget(commentId, author, excerpt) };
    }

    private async Task<ItemActivity?> FindCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        string? cursor = null;
        for (var page = 0; page < MaxCommentPages; page++)
        {
            var result = await activity.ListAsync(
                actor, itemKey, new ActivityQuery { Kind = ActivityKind.Commented, Limit = ActivityQuery.MaxLimit, Cursor = cursor }, cancellationToken);
            if (result.Failure is not null)
            {
                return null;
            }

            if (result.Value.Items.FirstOrDefault(a => a.Id == commentId && a.Kind == ActivityKind.Commented) is { } match)
            {
                return match;
            }

            cursor = result.Value.NextCursor;
            if (cursor is null)
            {
                return null;
            }
        }

        return null;
    }

    private static string? StringValue(List<ItemFieldChange> changes, string field) =>
        changes.FirstOrDefault(c => c.Field == field) is { Value.ValueKind: JsonValueKind.String } change
            ? change.Value.GetString()
            : null;
}
