namespace PmTool.Ai.Persistence;

/// <summary><c>ai.member_proposals</c> 的一列：助理提出的成員建議（<see cref="IMemberProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record MemberProposalRow
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

    /// <summary>jsonb：<see cref="MemberChange"/> 陣列，依套用的順序。</summary>
    public required string Changes { get; init; }

    /// <summary>pending、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}
