using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>改密碼與「登出其他裝置」把目前的工作階段換成新的；改密碼可以一起撤銷存取權杖；兩者都寫稽核紀錄。</summary>
[TestFixture]
public sealed class SessionRotationTests
{
    private const string NewPassword = "brand-new-password";
    private static readonly AccessTokenScope[] Read = [AccessTokenScope.ItemsRead];

    private readonly ManualTimeProvider _time = new();
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await SessionTestSupport.StartWithClockAsync(_time);
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [SetUp]
    public void ClearRequest() => _host.Request.Clear();

    [Test]
    public async Task ChangeOwnPassword_ReturnsANewSessionAndTheOldTokenStopsWorking()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var old = await _host.StartSessionAsync(user);
        await _host.FindAsync(old);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(old.Token, TestData.Password, NewPassword, false, CancellationToken.None);

        // Assert
        var fresh = result.Value.Session;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fresh.Token, Is.Not.EqualTo(old.Token));
            Assert.That(fresh.SessionId, Is.Not.EqualTo(old.SessionId));
            Assert.That(await _host.FindAsync(old), Is.Null);
            Assert.That((await _host.FindAsync(fresh))?.User.PersonId, Is.EqualTo(user.PersonId));
        }
    }

    [Test]
    public async Task ChangeOwnPassword_NewSessionGetsAFullAbsoluteLimitFromNow()
    {
        // Arrange：舊的工作階段已經用了五天；密碼剛驗過，等於重新登入。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var old = await _host.StartSessionAsync(user);
        await KeepActiveForAsync(old, TimeSpan.FromDays(5));

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(old.Token, TestData.Password, NewPassword, false, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Session.ExpiresAt, Is.EqualTo(_time.GetUtcNow().AddDays(IdentityRules.SessionAbsoluteDays)));
        Assert.That(result.Value.Session.ExpiresAt, Is.GreaterThan(old.ExpiresAt));
    }

    [Test]
    public async Task ChangeOwnPassword_RevokeAccessTokens_RevokesEveryPersonalTokenAndCountsThem()
    {
        // Arrange：命令列、本機代理（瀏覽器登入發的）與自己建的 CI
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var (code, verifier) = await CliLogin.CliLoginTestSupport.ApproveAsync(_host, user);
        var login = (await CliLogin.CliLoginTestSupport.RedeemAsync(_host, code, verifier)).Value;
        var ci = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("CI", Read, 30), CancellationToken.None)).Value;
        await _host.AccessTokens.FindAsync(ci.Secret, CancellationToken.None);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, NewPassword, true, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.RevokedAccessTokens, Is.EqualTo(3));
            Assert.That(await _host.AccessTokens.FindAsync(login.Cli!.Secret, CancellationToken.None), Is.Null);
            Assert.That(await _host.AccessTokens.FindAsync(login.Agent!.Secret, CancellationToken.None), Is.Null);
            Assert.That(await _host.AccessTokens.FindAsync(ci.Secret, CancellationToken.None), Is.Null);
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task ChangeOwnPassword_KeepAccessTokens_LeavesThemWorking()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var ci = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("CI", Read, 30), CancellationToken.None)).Value;

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, NewPassword, false, CancellationToken.None);

        // Assert
        Assert.That(result.Value.RevokedAccessTokens, Is.Zero);
        Assert.That((await _host.AccessTokens.FindAsync(ci.Secret, CancellationToken.None))?.TokenId, Is.EqualTo(ci.Token.TokenId));
    }

    [Test]
    public async Task ChangeOwnPassword_RevokeAccessTokens_DoesNotTouchAnotherAccountsTokens()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var other = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var othersToken = (await _host.AccessTokens.CreateAsync(other.ToActor(), new NewAccessToken("別人的", Read, 30), CancellationToken.None)).Value;

        // Act
        await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, NewPassword, true, CancellationToken.None);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(othersToken.Secret, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task ChangeOwnPassword_WritesAnAuditEntryWithTheCountsAndNoSecrets()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        await _host.StartSessionAsync(user);
        await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("CI", Read, 30), CancellationToken.None);
        _host.Request.Set("203.0.113.40", "browser-test");

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, NewPassword, true, CancellationToken.None);

        // Assert
        var entry = (await ListAuditAsync(user.PersonId)).Single(e => e.Action == AuditActions.PasswordChanged);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
            Assert.That(entry.ActorPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entry.TargetPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.40"));
            Assert.That(entry.Details["signedOutSessions"], Is.EqualTo("1"));
            Assert.That(entry.Details["revokedAccessTokens"], Is.EqualTo("1"));
            Assert.That(entry.Details.Values, Has.None.Contains(NewPassword).And.None.Contains(session.Token));
        }
    }

    [Test]
    public async Task ChangeOwnPassword_WrongCurrentPassword_WritesAFailedAuditEntryAndKeepsTheSession()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        _host.Request.Set("203.0.113.42");

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, "wrong-password", NewPassword, true, CancellationToken.None);

        // Assert
        var entries = (await ListAuditAsync(user.PersonId)).Where(e => e.Action == AuditActions.PasswordChanged).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(await _host.FindAsync(session), Is.Not.Null);
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Outcome, Is.EqualTo(AuditOutcomes.Failed));
            Assert.That(entries[0].ActorPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entries[0].SourceAddress, Is.EqualTo("203.0.113.42"));
            Assert.That(entries[0].Details["reason"], Is.EqualTo("incorrect_password"));
            Assert.That(entries[0].Details.Values, Has.None.Contains("wrong-password"));
        }
    }

    [Test]
    public async Task ChangeOwnPassword_CorrectCurrentPasswordButTooShortNewPassword_WritesNoAuditEntry()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, "short", true, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.False);
        Assert.That(await ListAuditAsync(user.PersonId), Has.None.Matches<AuditEntry>(e => e.Action == AuditActions.PasswordChanged));
    }

    [Test]
    public async Task EndOthers_NewSessionKeepsTheOldAbsoluteExpiry()
    {
        // Arrange：不用密碼的按鈕不能延長絕對期限
        var user = await _host.AddUserAsync(_owner.ToActor());
        var current = await _host.StartSessionAsync(user);
        await KeepActiveForAsync(current, TimeSpan.FromDays(3));

        // Act
        var rotation = (await _host.Sessions.EndOthersAsync(current.Token, TestData.Password, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rotation.Session.ExpiresAt, Is.EqualTo(current.ExpiresAt));
            Assert.That(rotation.Session.Token, Is.Not.EqualTo(current.Token));
            Assert.That(await _host.FindAsync(current), Is.Null);
            Assert.That(await _host.FindAsync(rotation.Session), Is.Not.Null);
        }
    }

    [Test]
    public async Task EndOthers_DoesNotRevokeAccessTokens()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var current = await _host.StartSessionAsync(user);
        var ci = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("CI", Read, 30), CancellationToken.None)).Value;

        // Act
        await _host.Sessions.EndOthersAsync(current.Token, TestData.Password, CancellationToken.None);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(ci.Secret, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task EndOthers_WritesAnAuditEntry()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var current = await _host.StartSessionAsync(user);
        await _host.StartSessionAsync(user);
        _host.Request.Set("203.0.113.41");

        // Act
        await _host.Sessions.EndOthersAsync(current.Token, TestData.Password, CancellationToken.None);

        // Assert
        var entry = (await ListAuditAsync(user.PersonId)).Single(e => e.Action == AuditActions.OtherSessionsSignedOut);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ActorPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.41"));
            Assert.That(entry.Details["signedOutSessions"], Is.EqualTo("1"));
        }
    }

    [Test]
    public async Task EndOthers_FromAnEndedSession_WritesNoAuditEntry()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var ended = await _host.StartSessionAsync(user);
        await _host.Sessions.EndAsync(ended.Token, CancellationToken.None);

        // Act
        var result = await _host.Sessions.EndOthersAsync(ended.Token, TestData.Password, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.SessionEnded));
        Assert.That(await ListAuditAsync(user.PersonId), Has.None.Matches<AuditEntry>(e => e.Action == AuditActions.OtherSessionsSignedOut));
    }

    [Test]
    public async Task EndOthers_WrongCurrentPassword_IsRejectedWithThePathAndEndsNothing()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var current = await _host.StartSessionAsync(user);
        var other = await _host.StartSessionAsync(user);

        // Act
        var result = await _host.Sessions.EndOthersAsync(current.Token, "wrong-password", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure?.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (IdentityErrorCodes.PasswordIncorrect, "currentPassword") }));
            Assert.That(await _host.AreActiveAsync(current, other), Is.All.True);
        }
    }

    [Test]
    public async Task EndOthers_WrongCurrentPassword_WritesAFailedAuditEntryWithTheReason()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var current = await _host.StartSessionAsync(user);
        _host.Request.Set("203.0.113.43");

        // Act
        await _host.Sessions.EndOthersAsync(current.Token, "wrong-password", CancellationToken.None);

        // Assert
        var entries = (await ListAuditAsync(user.PersonId)).Where(e => e.Action == AuditActions.OtherSessionsSignedOut).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Outcome, Is.EqualTo(AuditOutcomes.Failed));
            Assert.That(entries[0].SourceAddress, Is.EqualTo("203.0.113.43"));
            Assert.That(entries[0].Details["reason"], Is.EqualTo("incorrect_password"));
        }
    }

    [Test]
    public async Task EndOthers_TwoSessionsOfTheSameAccountAtTheSameTime_NeitherThrowsAndExactlyOneSessionIsLeft()
    {
        // Arrange：兩個裝置同時按「登出其他裝置」；先前會各握著對方要作廢的那一筆而死結
        for (var round = 0; round < 6; round++)
        {
            var user = await _host.AddUserAsync(_owner.ToActor());
            var first = await _host.StartSessionAsync(user);
            var second = await _host.StartSessionAsync(user);
            using var barrier = new Barrier(2);

            // Act
            var one = Task.Run(() => RotateAtTheSameTimeAsync(barrier, first));
            var two = Task.Run(() => RotateAtTheSameTimeAsync(barrier, second));
            var results = await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(60));

            // Assert：先到的成功，後到的看到自己的工作階段已經被作廢；留下的只有先到的那個新的
            var winners = results.Where(r => r.Succeeded).ToList();
            var losers = results.Where(r => !r.Succeeded).ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(winners, Has.Count.EqualTo(1));
                Assert.That(losers.Select(l => l.Failure?.Code), Is.EqualTo(new[] { IdentityErrorCodes.SessionEnded }));
                Assert.That(await _host.AreActiveAsync(first, second, winners[0].Value.Session), Is.EqualTo(new[] { false, false, true }));
            }
        }
    }

    private Task<Result<SessionRotation>> RotateAtTheSameTimeAsync(Barrier barrier, StartedSession session)
    {
        barrier.SignalAndWait();
        return _host.Sessions.EndOthersAsync(session.Token, TestData.Password, CancellationToken.None);
    }

    /// <summary>每隔幾個小時用一次，閒置期限才不會先到。</summary>
    private async Task KeepActiveForAsync(StartedSession session, TimeSpan duration)
    {
        var step = TimeSpan.FromHours(6);
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += step)
        {
            _time.Advance(step);
            await _host.FindAsync(session);
        }
    }

    private async Task<IReadOnlyList<AuditEntry>> ListAuditAsync(Guid personId)
    {
        var page = await _host.Audit.ListAsync(_owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit, PersonId: personId), CancellationToken.None);
        Assert.That(page.Succeeded, Is.True, page.Failure?.Message);
        return page.Value.Items;
    }
}
