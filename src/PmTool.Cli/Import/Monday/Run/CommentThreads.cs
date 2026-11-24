namespace PmTool.Cli.Import.Monday.Run;

/// <summary>一則留言或回覆，攤平之後的樣子。<see cref="ReplyToId"/> 是回覆的那一則 update 的編號；最上層的留言是 null。</summary>
internal sealed record FlatComment(
    string Id, string? CreatorId, DateTimeOffset At, string Html, IReadOnlyList<MondayAsset> Assets, string? ReplyToId);

internal static class CommentThreads
{
    /// <summary>
    /// 項目的留言攤平成一串：每一則 update 後面接著它的回覆。回覆一定排在被回覆的那一則之後（伺服器要求），
    /// 不靠兩邊的時鐘一致：整串照 update 的時間、回覆照回覆的時間排。
    /// </summary>
    public static IReadOnlyList<FlatComment> Flatten(MondayItem item) =>
    [
        .. item.Updates.OrderBy(update => update.CreatedAt).SelectMany(update => new[]
            {
                new FlatComment(update.Id, update.CreatorId, update.CreatedAt, update.BodyHtml, update.Assets, null),
            }
            .Concat(update.Replies.OrderBy(reply => reply.CreatedAt)
                .Select(reply => new FlatComment(reply.Id, reply.CreatorId, reply.CreatedAt, reply.BodyHtml, reply.Assets, update.Id)))),
    ];
}
