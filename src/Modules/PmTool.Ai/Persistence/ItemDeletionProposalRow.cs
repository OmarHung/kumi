namespace PmTool.Ai.Persistence;

/// <summary><c>ai.item_deletion_proposals</c> 的一列：助理提出的刪除項目建議（<see cref="IItemDeletionProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record ItemDeletionProposalRow
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

    /// <summary>jsonb：<c>[{"itemKey","title"}]</c>，依建議的順序；標題是建立當時的。</summary>
    public required string Items { get; init; }

    public string? Reason { get; init; }

    /// <summary>jsonb：套用之後真的刪除的編號（字串陣列）；還沒套用是 <c>[]</c>。</summary>
    public string DeletedKeys { get; init; } = "[]";

    /// <summary>pending、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}
