using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.CommentProposals;

/// <summary>讀一個項目的全部留言（含已刪除的、回覆），建立建議時用它確認「留言是那個項目的」。</summary>
internal static class CommentLookup
{
    /// <summary>最多翻幾頁：一個項目的留言多到翻不完時，後面的留言當作找不到，不無限讀下去。</summary>
    private const int MaxPages = 25;

    public static async Task<Result<IReadOnlyDictionary<Guid, ItemActivity>>> LoadAsync(
        IItemActivityService activity, Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        var comments = new Dictionary<Guid, ItemActivity>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var read = await activity.ListAsync(
                actor, itemKey, new ActivityQuery { Kind = ActivityKind.Commented, Limit = ActivityQuery.MaxLimit, Cursor = cursor }, cancellationToken);
            if (read.Failure is { } failure)
            {
                return failure;
            }

            foreach (var item in read.Value.Items.Where(i => i.Kind == ActivityKind.Commented))
            {
                comments[item.Id] = item;
            }

            cursor = read.Value.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return comments;
    }
}
