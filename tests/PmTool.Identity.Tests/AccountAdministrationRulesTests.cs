using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>管理別人帳號的三種操作。權限規則一樣，所以每條規則三種都跑一次。</summary>
public enum AccountOperation
{
    Disable,
    Enable,
    ResetPassword,
}

/// <summary>
/// 停用、重新啟用、重設密碼的權限：只有擁有者或管理員可以做；擁有者與管理員的帳號只有擁有者能動；
/// 不能動自己的帳號；找不到人、沒有帳號各有自己的代碼。
/// </summary>
[TestFixture]
public sealed class AccountAdministrationRulesTests
{
    public const string ResetTo = "reset-password-0000";

    private static readonly OrganizationRole[] AllRoles =
        [OrganizationRole.Owner, OrganizationRole.Admin, OrganizationRole.Member, OrganizationRole.Guest];

    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await IdentityTestHost.StartAsync();
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCaseSource(nameof(PermissionCases))]
    public async Task Operation_FollowsTheRoleRules(AccountOperation operation, OrganizationRole actorRole, OrganizationRole targetRole, bool allowed)
    {
        // Arrange
        var actor = await _host.AddUserAsync(_owner.ToActor(), actorRole);
        var target = await _host.AddUserAsync(_owner.ToActor(), targetRole);

        // Act
        var result = await RunAsync(_host, operation, actor.ToActor(), target.PersonId);

        // Assert
        if (allowed)
        {
            Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
            Assert.That(result.Value.PersonId, Is.EqualTo(target.PersonId));
        }
        else
        {
            AssertFailure(result, FailureKind.Forbidden, IdentityErrorCodes.Forbidden);
        }
    }

    [TestCaseSource(nameof(SelfCases))]
    public async Task Operation_OnTheirOwnAccount_ReturnsIsSelf(AccountOperation operation, OrganizationRole actorRole)
    {
        // Arrange
        var actor = await _host.AddUserAsync(_owner.ToActor(), actorRole);

        // Act
        var result = await RunAsync(_host, operation, actor.ToActor(), actor.PersonId);

        // Assert
        AssertFailure(result, FailureKind.Conflict, IdentityErrorCodes.IsSelf);
        Assert.That(await _host.Authentication.SignInAsync(actor.Email, TestData.Password, CancellationToken.None), Is.Not.Null);
    }

    [TestCase(AccountOperation.Disable)]
    [TestCase(AccountOperation.Enable)]
    [TestCase(AccountOperation.ResetPassword)]
    public async Task Operation_UnknownPersonOrAnotherOrganizationsPerson_ReturnsNotFound(AccountOperation operation)
    {
        // Arrange
        var otherOrganization = await _host.AddOrganizationAsync("別的範例組織");
        var theirs = await _host.People.CreateAsync(
            otherOrganization, new NewPerson("別的組織的範例使用者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member),
            CancellationToken.None);

        // Act
        var unknown = await RunAsync(_host, operation, _owner.ToActor(), Guid.CreateVersion7());
        var elsewhere = await RunAsync(_host, operation, _owner.ToActor(), theirs.Value.PersonId);

        // Assert
        AssertFailure(unknown, FailureKind.NotFound, IdentityErrorCodes.NotFound);
        AssertFailure(elsewhere, FailureKind.NotFound, IdentityErrorCodes.NotFound);
    }

    [TestCase(AccountOperation.Disable)]
    [TestCase(AccountOperation.Enable)]
    [TestCase(AccountOperation.ResetPassword)]
    public async Task Operation_PersonWithoutAnAccount_ReturnsNoAccount(AccountOperation operation)
    {
        // Arrange
        var personId = await _host.AddPersonWithoutAccountAsync(_owner.OrganizationId, "沒有帳號的範例使用者", OrganizationRole.Member);

        // Act
        var result = await RunAsync(_host, operation, _owner.ToActor(), personId);

        // Assert
        AssertFailure(result, FailureKind.Conflict, IdentityErrorCodes.NoAccount);
    }

    [TestCase(AccountOperation.Disable)]
    [TestCase(AccountOperation.Enable)]
    [TestCase(AccountOperation.ResetPassword)]
    public async Task Operation_ByAnAdminWhoseAccountWasJustDisabled_ReturnsForbidden(AccountOperation operation)
    {
        // Arrange：他的工作階段可能是在別的程序裡作廢的，還沒傳到這裡。
        var admin = await _host.AddUserAsync(_owner.ToActor(), OrganizationRole.Admin);
        var target = await _host.AddUserAsync(_owner.ToActor());
        await _host.DisableAccountAsync(admin.AccountId);

        // Act
        var result = await RunAsync(_host, operation, admin.ToActor(), target.PersonId);

        // Assert
        AssertFailure(result, FailureKind.Forbidden, IdentityErrorCodes.Forbidden);
    }

    [Test]
    public async Task ResetPassword_ByAMemberWithAPasswordThatIsTooShort_ReturnsForbiddenFirst()
    {
        // Arrange：先看權限再看資料，和新增人員一樣。
        var member = await _host.AddUserAsync(_owner.ToActor());
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.People.ResetPasswordAsync(member.ToActor(), target.PersonId, "short", CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Forbidden, IdentityErrorCodes.Forbidden);
    }

    internal static Task<Result<PersonSummary>> RunAsync(IdentityTestHost host, AccountOperation operation, Actor actor, Guid personId) =>
        operation switch
        {
            AccountOperation.Disable => host.People.SetDisabledAsync(actor, personId, true, CancellationToken.None),
            AccountOperation.Enable => host.People.SetDisabledAsync(actor, personId, false, CancellationToken.None),
            _ => host.People.ResetPasswordAsync(actor, personId, ResetTo, CancellationToken.None),
        };

    private static void AssertFailure(Result<PersonSummary> result, FailureKind kind, string code)
    {
        Assert.That(result.Failure, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(kind));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
            Assert.That(result.Failure.Message, Is.Not.Empty);
        }
    }

    private static IEnumerable<TestCaseData> PermissionCases()
    {
        foreach (var operation in Enum.GetValues<AccountOperation>())
        {
            foreach (var actor in new[] { OrganizationRole.Member, OrganizationRole.Guest })
            {
                yield return Case(operation, actor, OrganizationRole.Member, allowed: false);
            }

            foreach (var target in AllRoles)
            {
                yield return Case(operation, OrganizationRole.Admin, target, allowed: target is OrganizationRole.Member or OrganizationRole.Guest);
                yield return Case(operation, OrganizationRole.Owner, target, allowed: true);
            }
        }
    }

    private static TestCaseData Case(AccountOperation operation, OrganizationRole actor, OrganizationRole target, bool allowed) =>
        new TestCaseData(operation, actor, target, allowed).SetArgDisplayNames($"{operation}", $"{actor} 對 {target}", allowed ? "可以" : "不行");

    private static IEnumerable<TestCaseData> SelfCases() =>
        from operation in Enum.GetValues<AccountOperation>()
        from role in new[] { OrganizationRole.Owner, OrganizationRole.Admin }
        select new TestCaseData(operation, role).SetArgDisplayNames($"{operation}", $"{role}");
}
