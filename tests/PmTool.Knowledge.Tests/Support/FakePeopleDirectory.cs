using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Knowledge.Tests;

/// <summary>測試用的人員目錄：知識庫只用它查人的組織角色（組織空間誰看得到）與限制名單上的人在不在組織裡。</summary>
internal sealed class FakePeopleDirectory : IPeopleDirectory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid Organization, Guid Person), PersonSummary> _people = [];

    public void Add(Guid organizationId, Guid personId, OrganizationRole role)
    {
        lock (_gate)
        {
            _people[(organizationId, personId)] = new PersonSummary(personId, $"人員 {personId:N}"[..8], null, role, false);
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
        throw new NotSupportedException("知識庫不新增人員。");

    public Task<Result<PersonSummary>> SetDisabledAsync(Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken) =>
        throw new NotSupportedException("知識庫不停用人員。");

    public Task<Result<PersonSummary>> ResetPasswordAsync(Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException("知識庫不重設密碼。");

    public Task<Result<PersonSummary>> SetRoleAsync(Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken) =>
        throw new NotSupportedException("知識庫不改角色。");
}
