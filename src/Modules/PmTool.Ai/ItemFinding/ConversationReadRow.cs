namespace PmTool.Ai.ItemFinding;

/// <summary>
/// <c>ai.conversation_reads</c> 的一列：這則對話讀過這個專案裡「不是他負責也不是他建立」的項目。
/// 之後他在那個專案只剩看自己的、或看不到時，這則對話整則刪掉（歷史裡可能有他現在看不到的內容）。
/// 對話刪掉時一起刪（外鍵連帶）。
/// </summary>
internal sealed record ConversationReadRow
{
    public required Guid ConversationId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    public required Guid ProjectId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
