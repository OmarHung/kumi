using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Ai.Tests.Actions.People;

/// <summary>人員管理建議的測試用：身分模組的假物件，記下每一次寫入（含新增人員收到的密碼）。</summary>
internal sealed class RecordingPeople : IPeopleDirectory, IPersonProfileService
{
    private readonly List<PersonSummary> _people = [];

    public List<NewPerson> Created { get; } = [];

    public List<(Guid Person, bool Disabled)> DisabledCalls { get; } = [];

    public List<(Guid Person, OrganizationRole Role)> RoleCalls { get; } = [];

    public List<(Guid Person, PersonProfile Profile)> ProfileCalls { get; } = [];

    /// <summary>設了就讓下一次寫入回這個失敗（模擬身分模組拒絕）。</summary>
    public Failure? RejectWith { get; set; }

    /// <summary>設了就讓下一次改職稱與負責範圍回這個失敗（建人不受影響）。</summary>
    public Failure? RejectProfileWith { get; set; }

    public Guid Add(
        string name, OrganizationRole role = OrganizationRole.Member, bool disabled = false, string? email = null,
        string? jobTitle = null, string? responsibilities = null, Guid? id = null)
    {
        var personId = id ?? Guid.CreateVersion7();
        _people.Add(new PersonSummary(personId, name, email ?? $"{personId:N}@example.com", role, disabled)
        {
            JobTitle = jobTitle,
            Responsibilities = responsibilities,
        });
        return personId;
    }

    public Task<IReadOnlyList<PersonSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PersonSummary>>([.. _people]);

    public Task<IReadOnlyDictionary<Guid, PersonSummary>> FindManyAsync(
        Guid organizationId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PersonSummary>>(_people.Where(p => personIds.Contains(p.PersonId)).ToDictionary(p => p.PersonId));

    public Task<Result<PersonSummary>> CreateAsync(Actor actor, NewPerson person, CancellationToken cancellationToken)
    {
        if (Reject() is { } failure)
        {
            return Task.FromResult<Result<PersonSummary>>(failure);
        }

        Created.Add(person);
        var id = Add(person.DisplayName, person.Role, email: person.Email);
        return Task.FromResult<Result<PersonSummary>>(_people.Single(p => p.PersonId == id));
    }

    public Task<Result<PersonSummary>> SetDisabledAsync(Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken)
    {
        if (Reject() is { } failure)
        {
            return Task.FromResult<Result<PersonSummary>>(failure);
        }

        DisabledCalls.Add((personId, disabled));
        return Task.FromResult(Replace(personId, p => p with { IsDisabled = disabled }));
    }

    public Task<Result<PersonSummary>> ResetPasswordAsync(Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PersonSummary>> SetRoleAsync(Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken)
    {
        if (Reject() is { } failure)
        {
            return Task.FromResult<Result<PersonSummary>>(failure);
        }

        RoleCalls.Add((personId, role));
        return Task.FromResult(Replace(personId, p => p with { Role = role }));
    }

    public Task<Result<PersonSummary>> UpdateProfileAsync(Actor actor, Guid personId, PersonProfile profile, CancellationToken cancellationToken)
    {
        var profileFailure = RejectProfileWith;
        RejectProfileWith = null;
        if (profileFailure is not null)
        {
            return Task.FromResult<Result<PersonSummary>>(profileFailure);
        }

        if (Reject() is { } failure)
        {
            return Task.FromResult<Result<PersonSummary>>(failure);
        }

        ProfileCalls.Add((personId, profile));
        return Task.FromResult(Replace(personId, p => p with { JobTitle = profile.JobTitle, Responsibilities = profile.Responsibilities }));
    }

    private Failure? Reject()
    {
        var failure = RejectWith;
        RejectWith = null;
        return failure;
    }

    private Result<PersonSummary> Replace(Guid personId, Func<PersonSummary, PersonSummary> change)
    {
        var index = _people.FindIndex(p => p.PersonId == personId);
        _people[index] = change(_people[index]);
        return _people[index];
    }
}
