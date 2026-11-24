using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Notifications.Tests;

/// <summary>測試用的人員目錄：通知模組只用它查收件人的 Email、角色、停用了沒，與做事的人的名稱。</summary>
internal sealed class FakePeopleDirectory : IPeopleDirectory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid Organization, Guid Person), PersonSummary> _people = [];

    public void Add(
        Guid organizationId, Guid personId, string displayName, string? email, OrganizationRole role = OrganizationRole.Member, bool disabled = false)
    {
        lock (_gate)
        {
            _people[(organizationId, personId)] = new PersonSummary(personId, displayName, email, role, disabled);
        }
    }

    public string EmailOf(Guid organizationId, Guid personId)
    {
        lock (_gate)
        {
            return _people[(organizationId, personId)].Email!;
        }
    }

    public void Remove(Guid organizationId, Guid personId)
    {
        lock (_gate)
        {
            _people.Remove((organizationId, personId));
        }
    }

    public Task<IReadOnlyList<PersonSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<PersonSummary>>(
                [.. _people.Where(pair => pair.Key.Organization == organizationId).Select(pair => pair.Value)]);
        }
    }

    public Task<IReadOnlyDictionary<Guid, PersonSummary>> FindManyAsync(
        Guid organizationId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, PersonSummary>>(personIds
                .Distinct()
                .Where(id => _people.ContainsKey((organizationId, id)))
                .ToDictionary(id => id, id => _people[(organizationId, id)]));
        }
    }

    public Task<Result<PersonSummary>> CreateAsync(Actor actor, NewPerson person, CancellationToken cancellationToken) =>
        throw new NotSupportedException("通知模組不新增人員。");

    public Task<Result<PersonSummary>> SetDisabledAsync(Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken) =>
        throw new NotSupportedException("通知模組不停用人員。");

    public Task<Result<PersonSummary>> ResetPasswordAsync(Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException("通知模組不重設密碼。");

    public Task<Result<PersonSummary>> SetRoleAsync(Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken) =>
        throw new NotSupportedException("通知模組不改角色。");
}

/// <summary>測試用的組織時鐘：固定一個時區，預設是台北。</summary>
internal sealed class FakeOrganizationClock(string zoneId = OrganizationTime.DefaultTimeZoneId) : IOrganizationClock
{
    /// <summary>回傳例外就讓這個組織的查詢失敗，模擬寄送中途出意外。</summary>
    public Func<Guid, Exception?> OnZone { get; set; } = _ => null;

    public Task<TimeZoneInfo> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
        OnZone(organizationId) is { } failure
            ? Task.FromException<TimeZoneInfo>(failure)
            : Task.FromResult(TimeZoneInfo.FindSystemTimeZoneById(zoneId));
}
