namespace PmTool.Ai.Persistence;

/// <summary><c>ai.item_proposals</c> 的一列：助理對一個項目提出的修改建議（<see cref="IItemProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record ItemProposalRow
{
    public const string PendingStatus = "pending";

    public const string AppliedStatus = "applied";

    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>內建助理建立的才有；本機代理經 MCP 建立的是 null。</summary>
    public Guid? ConversationId { get; init; }

    public required string ProjectKey { get; init; }

    public required string ItemKey { get; init; }

    /// <summary>建立當時的標題。</summary>
    public required string ItemTitle { get; init; }

    /// <summary>jsonb：<c>[{"field","value"}]</c>，依建議的順序。</summary>
    public required string Changes { get; init; }

    public string? Comment { get; init; }

    /// <summary>有改說明時，建立當下的說明版本（<c>DescriptionRevision</c>）；套用時用來偵測別人改過。</summary>
    public string? DescriptionRevision { get; init; }

    /// <summary>jsonb：留言是回覆哪一則的 <c>{commentId, authorName, excerpt}</c>（<see cref="CommentReplyTarget"/>）；新的一則是 null。</summary>
    public string? ReplyTo { get; init; }

    /// <summary>pending、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}
