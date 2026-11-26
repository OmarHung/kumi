namespace PmTool.Ai.Persistence;

/// <summary><c>ai.new_item_proposals</c> 的一列：助理提出的新增項目建議（<see cref="INewItemProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record NewItemProposalRow
{
    public const string PendingStatus = "pending";

    /// <summary>使用者按了「全部建立」、正在建立；建立不是冪等的，所以要先標記並提交，才不會被建立兩次。</summary>
    public const string ApplyingStatus = "applying";

    public const string AppliedStatus = "applied";

    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>內建助理建立的才有；本機、主機代理經 MCP 建立的是 null。</summary>
    public Guid? ConversationId { get; init; }

    public required string ProjectKey { get; init; }

    /// <summary>jsonb：檢查過的 <see cref="NewItemDraft"/> 陣列，依建立的順序。</summary>
    public required string Items { get; init; }

    /// <summary>jsonb：和 <see cref="Items"/> 一樣長，已建立的是項目編號、其他是 null。</summary>
    public required string CreatedKeys { get; init; }

    /// <summary>pending、applying、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}
