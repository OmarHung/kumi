using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 測試用的登入服務。帳號放在記憶體裡，測試可以中途停用帳號或改角色，模擬別的地方改了資料；
/// <see cref="FakeSessionService"/> 從這裡拿帳號現在的資料。
/// </summary>
internal sealed class FakeAuthenticationService : IAuthenticationService
{
    private readonly ConcurrentDictionary<Guid, AuthenticatedUser> _accounts = new();
    private readonly ConcurrentDictionary<string, string> _passwords = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Email, string Password)> _signInCalls = new();

    public FakeAuthenticationService()
    {
        foreach (var user in TestUsers.All)
        {
            Add(user, TestUsers.Password);
        }
    }

    public IReadOnlyList<(string Email, string Password)> SignInCalls => _signInCalls.ToArray();

    public void Add(AuthenticatedUser user, string password)
    {
        _accounts[user.AccountId] = user;
        _passwords[user.Email] = password;
    }

    /// <summary>用新的資料取代同一個帳號（例如改了角色或名稱）。</summary>
    public void Replace(AuthenticatedUser user) => _accounts[user.AccountId] = user;

    /// <summary>停用帳號：之後登入會失敗，工作階段也查不到。</summary>
    public void Disable(Guid accountId) => _accounts.TryRemove(accountId, out _);

    public void SetPassword(string email, string password) => _passwords[email] = password;

    public bool HasPassword(string email, string password) => _passwords.TryGetValue(email, out var expected) && expected == password;

    /// <summary>帳號現在的資料；停用了是 null。</summary>
    public AuthenticatedUser? Current(Guid accountId) => _accounts.GetValueOrDefault(accountId);

    public Task<AuthenticatedUser?> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        _signInCalls.Enqueue((email, password));
        var user = _accounts.Values.FirstOrDefault(account => account.Email == email);
        var matches = user is not null && _passwords.TryGetValue(email, out var expected) && expected == password;
        return Task.FromResult(matches ? user : null);
    }
}

internal sealed record FindManyCall(Guid OrganizationId, IReadOnlyList<Guid> PersonIds);

internal sealed record SetDisabledCall(Actor Actor, Guid PersonId, bool Disabled);

internal sealed record SetRoleCall(Actor Actor, Guid PersonId, OrganizationRole Role);

internal sealed record ResetPasswordCall(Actor Actor, Guid PersonId, string NewPassword);

/// <summary>測試用的人員目錄。預設有 <see cref="TestUsers"/> 的三個人，都在同一個組織。</summary>
internal sealed class FakePeopleDirectory : IPeopleDirectory
{
    private readonly ConcurrentDictionary<Guid, (Guid OrganizationId, PersonSummary Person)> _people = new();
    private readonly ConcurrentQueue<FindManyCall> _findManyCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, NewPerson Person)> _createCalls = new();
    private readonly ConcurrentQueue<SetDisabledCall> _setDisabledCalls = new();
    private readonly ConcurrentQueue<ResetPasswordCall> _resetPasswordCalls = new();
    private readonly ConcurrentQueue<SetRoleCall> _setRoleCalls = new();

    public FakePeopleDirectory()
    {
        foreach (var user in TestUsers.All)
        {
            Add(user.OrganizationId, TestUsers.ToPerson(user));
        }
    }

    public Func<Actor, NewPerson, Result<PersonSummary>> OnCreate { get; set; } =
        (_, person) => new PersonSummary(Guid.CreateVersion7(), person.DisplayName, person.Email, person.Role, IsDisabled: false);

    /// <summary>預設：同一個組織裡找得到人就成功，回傳改過的資料；找不到是 person.not_found。</summary>
    public Func<SetDisabledCall, Result<PersonSummary>>? OnSetDisabled { get; set; }

    /// <summary>預設：同一個組織裡找得到人就成功；找不到是 person.not_found。</summary>
    public Func<ResetPasswordCall, Result<PersonSummary>>? OnResetPassword { get; set; }

    /// <summary>預設：同一個組織裡找得到人就成功，回傳改過角色的資料；找不到是 person.not_found。</summary>
    public Func<SetRoleCall, Result<PersonSummary>>? OnSetRole { get; set; }

    public IReadOnlyList<SetRoleCall> SetRoleCalls => _setRoleCalls.ToArray();

    public IReadOnlyList<SetDisabledCall> SetDisabledCalls => _setDisabledCalls.ToArray();

    public IReadOnlyList<ResetPasswordCall> ResetPasswordCalls => _resetPasswordCalls.ToArray();

    public IReadOnlyList<FindManyCall> FindManyCalls => _findManyCalls.ToArray();

    public IReadOnlyList<(Actor Actor, NewPerson Person)> CreateCalls => _createCalls.ToArray();

    public void Add(Guid organizationId, PersonSummary person) => _people[person.PersonId] = (organizationId, person);

    public void Remove(Guid personId) => _people.TryRemove(personId, out _);

    public Task<IReadOnlyList<PersonSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<PersonSummary> people = _people.Values
            .Where(entry => entry.OrganizationId == organizationId)
            .Select(entry => entry.Person)
            .OrderBy(person => person.DisplayName, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(people);
    }

    public Task<IReadOnlyDictionary<Guid, PersonSummary>> FindManyAsync(
        Guid organizationId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        _findManyCalls.Enqueue(new FindManyCall(organizationId, personIds.ToArray()));
        IReadOnlyDictionary<Guid, PersonSummary> found = personIds
            .Distinct()
            .Where(id => _people.TryGetValue(id, out var entry) && entry.OrganizationId == organizationId)
            .ToDictionary(id => id, id => _people[id].Person);
        return Task.FromResult(found);
    }

    public Task<Result<PersonSummary>> CreateAsync(Actor actor, NewPerson person, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, person));
        return Task.FromResult(OnCreate(actor, person));
    }

    public Task<Result<PersonSummary>> SetDisabledAsync(Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken)
    {
        var call = new SetDisabledCall(actor, personId, disabled);
        _setDisabledCalls.Enqueue(call);
        return Task.FromResult(OnSetDisabled?.Invoke(call) ?? Update(actor, personId, person => person with { IsDisabled = disabled }));
    }

    public Task<Result<PersonSummary>> ResetPasswordAsync(Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken)
    {
        var call = new ResetPasswordCall(actor, personId, newPassword);
        _resetPasswordCalls.Enqueue(call);
        return Task.FromResult(OnResetPassword?.Invoke(call) ?? Update(actor, personId, person => person));
    }

    public Task<Result<PersonSummary>> SetRoleAsync(Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken)
    {
        var call = new SetRoleCall(actor, personId, role);
        _setRoleCalls.Enqueue(call);
        return Task.FromResult(OnSetRole?.Invoke(call) ?? Update(actor, personId, person => person with { Role = role }));
    }

    private Result<PersonSummary> Update(Actor actor, Guid personId, Func<PersonSummary, PersonSummary> change)
    {
        if (!_people.TryGetValue(personId, out var entry) || entry.OrganizationId != actor.OrganizationId)
        {
            return new Failure(FailureKind.NotFound, IdentityErrorCodes.NotFound, "組織裡找不到這個人。");
        }

        var changed = change(entry.Person);
        _people[personId] = (entry.OrganizationId, changed);
        return changed;
    }
}

internal sealed class FakeIdentityBootstrapper : IIdentityBootstrapper
{
    private readonly ConcurrentQueue<FirstOwner> _calls = new();

    public Exception? Failure { get; set; }

    /// <summary>和初始化共用的紀錄，用來看出誰先誰後。</summary>
    public ConcurrentQueue<string>? Log { get; set; }

    public IReadOnlyList<FirstOwner> Calls => _calls.ToArray();

    public Task<bool> EnsureFirstOwnerAsync(FirstOwner owner, CancellationToken cancellationToken)
    {
        _calls.Enqueue(owner);
        Log?.Enqueue("bootstrap");
        return Failure is null ? Task.FromResult(true) : Task.FromException<bool>(Failure);
    }
}
