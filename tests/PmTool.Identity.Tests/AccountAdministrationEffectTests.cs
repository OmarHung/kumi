using Npgsql;
using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>停用、重新啟用、重設密碼做了之後：工作階段、登入、人員清單上看到的結果。</summary>
[TestFixture]
public sealed class AccountAdministrationEffectTests
{
    private const string ResetTo = AccountAdministrationRulesTests.ResetTo;

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

    [Test]
    public async Task Disable_EndsTheirSessionsRightAwayAndTheyCannotSignIn()
    {
        // Arrange：他的兩個工作階段都先查過一次，結果進了快取。
        var target = await _host.AddUserAsync(_owner.ToActor());
        var first = await _host.StartSessionAsync(target);
        var second = await _host.StartSessionAsync(target);
        var mine = await _host.StartSessionAsync(_owner);
        await _host.AreActiveAsync(first, second);

        // Act
        var result = await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new PersonSummary(target.PersonId, target.DisplayName, target.Email, target.Role, IsDisabled: true)));
        Assert.That(await _host.AreActiveAsync(first, second, mine), Is.EqualTo(new[] { false, false, true }));
        Assert.That(await _host.Authentication.SignInAsync(target.Email, TestData.Password, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Disable_TheyStayInThePeopleListMarkedDisabled()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);

        // Assert
        var listed = await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None);
        var found = await _host.People.FindManyAsync(_owner.OrganizationId, [target.PersonId], CancellationToken.None);
        Assert.That(listed.Single(p => p.PersonId == target.PersonId).IsDisabled, Is.True);
        Assert.That(found[target.PersonId].IsDisabled, Is.True);
    }

    [Test]
    public async Task Disable_AlreadyDisabled_SucceedsAndStaysDisabled()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);

        // Act
        var again = await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);

        // Assert
        Assert.That(again.Value.IsDisabled, Is.True);
        Assert.That(await _host.Authentication.SignInAsync(target.Email, TestData.Password, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Enable_AfterDisable_TheyCanSignInAgainButTheOldSessionsStayEnded()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var old = await _host.StartSessionAsync(target);
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);

        // Act
        var result = await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, false, CancellationToken.None);

        // Assert
        Assert.That(result.Value.IsDisabled, Is.False);
        Assert.That(await _host.Authentication.SignInAsync(target.Email, TestData.Password, CancellationToken.None), Is.EqualTo(target));
        Assert.That(await _host.FindAsync(old), Is.Null);
        var listed = await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None);
        Assert.That(listed.Single(p => p.PersonId == target.PersonId).IsDisabled, Is.False);
    }

    [Test]
    public async Task Enable_NeverDisabled_SucceedsAndChangesNothing()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(target);

        // Act
        var result = await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, false, CancellationToken.None);

        // Assert
        Assert.That(result.Value.IsDisabled, Is.False);
        Assert.That(await _host.FindAsync(session), Is.Not.Null);
    }

    [Test]
    public async Task ResetPassword_EndsTheirSessionsAndOnlyTheNewPasswordSignsIn()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(target);
        var mine = await _host.StartSessionAsync(_owner);
        await _host.FindAsync(session);

        // Act
        var result = await _host.People.ResetPasswordAsync(_owner.ToActor(), target.PersonId, ResetTo, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new PersonSummary(target.PersonId, target.DisplayName, target.Email, target.Role, IsDisabled: false)));
        Assert.That(await _host.AreActiveAsync(session, mine), Is.EqualTo(new[] { false, true }));
        Assert.That(await _host.Authentication.SignInAsync(target.Email, ResetTo, CancellationToken.None), Is.EqualTo(target));
        Assert.That(await _host.Authentication.SignInAsync(target.Email, TestData.Password, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ResetPassword_DisabledAccount_ResetsItAndItStaysDisabled()
    {
        // Arrange：先重設好密碼再重新啟用，是很自然的順序。
        var target = await _host.AddUserAsync(_owner.ToActor());
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);

        // Act
        var result = await _host.People.ResetPasswordAsync(_owner.ToActor(), target.PersonId, ResetTo, CancellationToken.None);
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, false, CancellationToken.None);

        // Assert
        Assert.That(result.Value.IsDisabled, Is.True);
        Assert.That(await _host.Authentication.SignInAsync(target.Email, ResetTo, CancellationToken.None), Is.Not.Null);
    }

    [TestCase(IdentityRules.MinPasswordLength - 1, IdentityErrorCodes.PasswordTooShort, TestName = "ResetPassword_TooShort_ReturnsTooShortOnNewPassword")]
    [TestCase(IdentityRules.MaxPasswordLength + 1, IdentityErrorCodes.PasswordTooLong, TestName = "ResetPassword_TooLong_ReturnsTooLongOnNewPassword")]
    public async Task ResetPassword_WrongLength(int length, string code)
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(target);

        // Act
        var result = await _host.People.ResetPasswordAsync(_owner.ToActor(), target.PersonId, new string('p', length), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
        Assert.That(result.Failure?.Code, Is.EqualTo(code));
        Assert.That(result.Failure?.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (code, "newPassword") }));
        Assert.That(await _host.FindAsync(session), Is.Not.Null, "沒有重設，工作階段也不動");
        Assert.That(await _host.Authentication.SignInAsync(target.Email, TestData.Password, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task PersonWithoutAnAccount_IsListedAsNotDisabled()
    {
        // Arrange
        var personId = await _host.AddPersonWithoutAccountAsync(_owner.OrganizationId, "沒有帳號的範例使用者", OrganizationRole.Member);

        // Act
        var listed = await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None);
        var found = await _host.People.FindManyAsync(_owner.OrganizationId, [personId], CancellationToken.None);

        // Assert
        var expected = new PersonSummary(personId, "沒有帳號的範例使用者", null, OrganizationRole.Member, IsDisabled: false);
        Assert.That(listed, Does.Contain(expected));
        Assert.That(found[personId], Is.EqualTo(expected));
    }

    [Test]
    public async Task Disable_TwoOwnersDisableEachOtherAtTheSameTime_AtLeastOneCanStillSignIn()
    {
        // Arrange：不排隊的話，兩邊都看到自己還能用，結果一位能登入的擁有者都不剩。
        // 從外面先拿住組織的鎖，等兩個請求都排在它後面才放開：兩個請求確定是同時進行的，
        // 而且有人拿掉程式裡的鎖時，「等兩個都排隊」這一步就等不到，測試會失敗。
        await using var host = await IdentityTestHost.StartAsync();
        var first = await host.StartWithOwnerAsync();
        var second = await host.AddUserAsync(first.ToActor(), OrganizationRole.Owner);
        await using var holder = new NpgsqlConnection(host.ConnectionString);
        await holder.OpenAsync();
        var key = AccountAdministration.LockKeyOf(first.OrganizationId);
        await ExecuteAsync(holder, $"SELECT pg_advisory_lock({AccountAdministration.AccountAdministrationLock}, {key})");
        var disabling = new[]
        {
            Task.Run(() => host.People.SetDisabledAsync(first.ToActor(), second.PersonId, true, CancellationToken.None)),
            Task.Run(() => host.People.SetDisabledAsync(second.ToActor(), first.PersonId, true, CancellationToken.None)),
        };
        await WaitForAdvisoryLockWaitersAsync(holder, expected: 2);

        // Act
        await ExecuteAsync(holder, $"SELECT pg_advisory_unlock({AccountAdministration.AccountAdministrationLock}, {key})");
        var results = await Task.WhenAll(disabling).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        var canSignIn = new[]
        {
            await host.Authentication.SignInAsync(first.Email, TestData.Password, CancellationToken.None) is not null,
            await host.Authentication.SignInAsync(second.Email, TestData.Password, CancellationToken.None) is not null,
        };
        Assert.That(canSignIn, Has.Some.True);
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(results.Single(r => !r.Succeeded).Failure?.Code, Is.EqualTo(IdentityErrorCodes.Forbidden));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>等到這個資料庫裡有這麼多個請求在等 advisory lock。每個測試各自一個資料庫，數到的只會是這個測試的。</summary>
    private static async Task WaitForAdvisoryLockWaitersAsync(NpgsqlConnection connection, int expected)
    {
        const string sql = """
            SELECT count(*)::int FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            if ((int)(await command.ExecuteScalarAsync() ?? 0) >= expected)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Assert.Fail($"等不到 {expected} 個請求排在組織的鎖後面：停用帳號沒有用這把鎖排隊？");
    }
}
