namespace PmTool.Ai.Persistence;

/// <summary><c>ai.project_proposals</c> 的一列：助理在不選專案的組織對話裡提出的新專案建議（<see cref="IProjectProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record ProjectProposalRow
{
    public const string PendingStatus = "pending";

    public const string AppliedStatus = "applied";

    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>內建助理與本機、主機的引擎都帶；沒有對話時是 null。</summary>
    public Guid? ConversationId { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Template { get; init; }

    /// <summary>建立建議當時模板的顯示名稱。</summary>
    public required string TemplateName { get; init; }

    public required bool IncludeExamples { get; init; }

    public string? Description { get; init; }

    /// <summary>pending、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? CreatedProjectKey { get; init; }
}
