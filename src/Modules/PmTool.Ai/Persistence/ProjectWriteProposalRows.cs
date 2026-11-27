namespace PmTool.Ai.Persistence;

/// <summary><c>ai.planning_proposals</c> 的一列：助理提出的週期與里程碑建議（<see cref="IPlanningProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record PlanningProposalRow
{
    public const string PendingStatus = "pending";
    public const string ApplyingStatus = "applying";
    public const string AppliedStatus = "applied";
    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>內建助理建立的才有；本機、主機代理經 MCP 建立的是 null。</summary>
    public Guid? ConversationId { get; init; }

    public required string ProjectKey { get; init; }

    /// <summary>
    /// jsonb：每一筆的 <c>{target, action, id, before, after, itemCount, error}</c>（<see cref="PlanningChange"/> 的形狀，
    /// 日期 yyyy-MM-dd），依套用的順序；新增的 <c>id</c> 套用成功後才有，<c>error</c> 套用後才有。
    /// </summary>
    public required string Changes { get; init; }

    public string? Reason { get; init; }

    /// <summary>pending、applying、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>套用、作廢的時間；建立中時是最後一次心跳。</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}

/// <summary><c>ai.project_change_proposals</c> 的一列：助理提出的改專案建議（<see cref="IProjectChangeProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record ProjectChangeProposalRow
{
    public const string PendingStatus = "pending";
    public const string AppliedStatus = "applied";
    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    public Guid? ConversationId { get; init; }

    public required string ProjectKey { get; init; }

    /// <summary>改成的名稱；不改是 null。</summary>
    public string? Name { get; init; }

    /// <summary>改成的封存狀態；不改是 null。</summary>
    public bool? Archived { get; init; }

    public required bool ChangesDescription { get; init; }

    /// <summary>改成的說明；<see cref="ChangesDescription"/> 是 true 時 null 是清掉。</summary>
    public string? Description { get; init; }

    /// <summary>jsonb：建立建議時的 <c>{name, archived, description, descriptionUpdatedAt}</c>（<see cref="ProjectSnapshot"/>）。</summary>
    public required string Before { get; init; }

    public string? Reason { get; init; }

    /// <summary>pending、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}

/// <summary><c>ai.comment_proposals</c> 的一列：助理提出的留言建議（<see cref="ICommentProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record CommentProposalRow
{
    public const string PendingStatus = "pending";
    public const string AppliedStatus = "applied";
    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    public Guid? ConversationId { get; init; }

    public required string ProjectKey { get; init; }

    /// <summary>jsonb：每一筆的 <c>{itemKey, commentId, action, authorName, commentedAt, excerpt, body, error}</c>（<see cref="CommentChange"/>），依套用的順序。</summary>
    public required string Changes { get; init; }

    /// <summary>pending、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}

/// <summary><c>ai.item_restore_proposals</c> 的一列：助理提出的復原項目建議（<see cref="IItemRestoreProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record ItemRestoreProposalRow
{
    public const string PendingStatus = "pending";
    public const string AppliedStatus = "applied";
    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    public Guid? ConversationId { get; init; }

    public required string ProjectKey { get; init; }

    /// <summary>jsonb：<c>[{"itemKey","title","deletedAt"}]</c>，依建議的順序；標題是建立當時的。</summary>
    public required string Items { get; init; }

    public string? Reason { get; init; }

    /// <summary>jsonb：套用之後真的復原的編號（字串陣列）；還沒套用是 <c>[]</c>。</summary>
    public string RestoredKeys { get; init; } = "[]";

    /// <summary>pending、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}
