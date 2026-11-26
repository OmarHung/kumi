namespace PmTool.Identity.Persistence;

/// <summary><c>identity.people</c> 的一列。人和帳號分開存：帳號停用後，人的紀錄還要留著給工作項目顯示。</summary>
internal sealed record PersonRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string DisplayName { get; init; }

    public required OrganizationRole Role { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>職稱；沒有是 null。AI 助理靠它判斷該指派給誰。</summary>
    public string? JobTitle { get; init; }

    /// <summary>負責範圍，一行字；沒有是 null。</summary>
    public string? Responsibilities { get; init; }
}
