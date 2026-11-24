using PmTool.Abstractions;

namespace PmTool.Identity.Persistence;

/// <summary><c>identity.organizations</c> 的一列。</summary>
internal sealed record OrganizationRow
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>IANA 的時區名稱。「今天」依它算；找不到這個名稱時讀的人改用預設，所以存壞了也不會讓別的功能失敗。</summary>
    public string TimeZone { get; init; } = OrganizationTime.DefaultTimeZoneId;
}
