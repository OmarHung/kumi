namespace PmTool.Ai.Persistence;

/// <summary><c>ai.action_proposals</c> 的一列：通用的動作建議（<see cref="IActionProposalService"/>）。只有建立的人讀得到。</summary>
internal sealed record ActionProposalRow
{
    public const string PendingStatus = "pending";
    public const string ApplyingStatus = "applying";
    public const string AppliedStatus = "applied";
    public const string DismissedStatus = "dismissed";

    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>內建助理建立的才有；本機、主機代理經 MCP 建立的可能是 null。</summary>
    public Guid? ConversationId { get; init; }

    /// <summary><see cref="ActionKinds"/> 之一。</summary>
    public required string Kind { get; init; }

    /// <summary>屬於哪個專案；不屬於專案的是 null。讀的時候專案看不到了就當作不存在。</summary>
    public string? ProjectKey { get; init; }

    public required string Title { get; init; }

    /// <summary>jsonb：給卡片看的步驟（<see cref="ActionStep"/> 的形狀，含套用後的 done、error、link），依套用的順序。</summary>
    public required string Steps { get; init; }

    /// <summary>jsonb：處理器自己的、整理過的每一步（套用時讀它，不讀卡片的文字）。不放使用者在卡片上填的值。</summary>
    public required string State { get; init; }

    /// <summary>jsonb：套用時要使用者填的值的宣告（<see cref="ActionInput"/>），沒有是 <c>[]</c>。</summary>
    public string Inputs { get; init; } = "[]";

    public string? Reason { get; init; }

    /// <summary>pending、applying、applied、dismissed。</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>套用、作廢的時間；建立中時是最後一次心跳。</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    public string? Error { get; init; }
}
