using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的人與帳號。全是假資料，Email 一律用 example.com。</summary>
internal static class TestUsers
{
    public const string Password = "correct-horse-battery";

    public static readonly Guid OrganizationId = Guid.Parse("0190a000-0000-7000-8000-000000000001");

    public const string OrganizationName = "Example 組織";

    public static readonly AuthenticatedUser Owner = new(
        Guid.Parse("0190a000-0000-7000-8000-0000000000a1"),
        Guid.Parse("0190a000-0000-7000-8000-0000000000b1"),
        OrganizationId,
        OrganizationName,
        "Owner Example",
        "owner@example.com",
        OrganizationRole.Owner);

    public static readonly AuthenticatedUser Member = new(
        Guid.Parse("0190a000-0000-7000-8000-0000000000a2"),
        Guid.Parse("0190a000-0000-7000-8000-0000000000b2"),
        OrganizationId,
        OrganizationName,
        "Member Example",
        "member@example.com",
        OrganizationRole.Member);

    public static readonly AuthenticatedUser Guest = new(
        Guid.Parse("0190a000-0000-7000-8000-0000000000a3"),
        Guid.Parse("0190a000-0000-7000-8000-0000000000b3"),
        OrganizationId,
        OrganizationName,
        "Guest Example",
        "guest@example.com",
        OrganizationRole.Guest);

    public static IReadOnlyList<AuthenticatedUser> All => [Owner, Member, Guest];

    public static PersonSummary ToPerson(AuthenticatedUser user) => new(user.PersonId, user.DisplayName, user.Email, user.Role, IsDisabled: false);
}
