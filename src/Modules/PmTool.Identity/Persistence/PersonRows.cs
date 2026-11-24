namespace PmTool.Identity.Persistence;

/// <summary>一個人與他的登入帳號總是一起建立；第一個擁有者和新增人員共用，欄位才不會一邊漏填。</summary>
internal static class PersonRows
{
    public static (PersonRow Person, AccountRow Account) Create(
        Guid organizationId,
        string displayName,
        string normalizedEmail,
        string passwordHash,
        OrganizationRole role,
        DateTimeOffset now)
    {
        var person = new PersonRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            DisplayName = displayName,
            Role = role,
            CreatedAt = now,
        };
        var account = new AccountRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            PersonId = person.Id,
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            CreatedAt = now,
        };
        return (person, account);
    }
}
