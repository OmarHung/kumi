namespace PmTool.Identity.Persistence;

/// <summary><c>identity.accounts</c> 的一列：一個人用 Email 加密碼登入的方式。</summary>
internal sealed record AccountRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>正規化過的 Email（去頭尾空白、小寫），全域唯一。</summary>
    public required string Email { get; init; }

    public required string PasswordHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DisabledAt { get; init; }
}
