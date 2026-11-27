namespace PmTool.Ai.Persistence;

/// <summary><c>ai.item_batch_proposals</c> 的一列：助理提出的批次修改建議（<see cref="IItemBatchProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record ItemBatchProposalRow
{
    public const string PendingStatus = "pending";

    public const string AppliedStatus = "applied";

    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>內建助理建立的才有；本機、主機代理經 MCP 建立的是 null。</summary>
    public Guid? ConversationId { get; init; }

    public required string ProjectKey { get; init; }

    /// <summary>
    /// jsonb：每一筆的 <c>{itemKey, title, changes:[{field,value}], comment, replyTo, expectedDescriptionRevision, error}</c>，依套用的順序
    /// （<c>replyTo</c> 是 <see cref="CommentReplyTarget"/> 的形狀，沒有是 null；之前存的沒有這個屬性）；
    /// <c>error</c> 套用後才有。
    /// </summary>
    public required string Items { get; init; }

    /// <summary>pending、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}
