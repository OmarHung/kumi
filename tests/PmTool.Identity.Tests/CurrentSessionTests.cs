using PmTool.Abstractions;
using PmTool.Identity.Tests.CliLogin;
using PmTool.Identity.Tests.OAuth;
using static PmTool.Identity.Tests.CliLogin.CliLoginTestSupport;

namespace PmTool.Identity.Tests;

/// <summary>
/// 發權杖與授權（建立存取權杖、核准命令列登入、OAuth 允許）要確認發出要求的工作階段還有效，
/// 而且是在拿到帳號那一列的鎖之後確認；改密碼、重設密碼、停用帳號也要讓還沒兌換的登入碼失效。
/// </summary>
[TestFixture]
public sealed class CurrentSessionTests
{
    private const string NewPassword = "brand-new-password";
    private static readonly AccessTokenScope[] Read = [AccessTokenScope.ItemsRead];

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

    [SetUp]
    public void ClearCurrentSession() => _host.CurrentSession.Clear();

    [Test]
    public async Task Redeem_AfterTheOwnChangedThePassword_IsRejectedAndNoTokenExists()
    {
        // Arrange：拿著 Cookie 的人先核准一次登入、不兌換
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act：本人改密碼並撤銷權杖，之後才有人兌換
        var changed = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, NewPassword, true, CancellationToken.None);
        var redeemed = await _host.RedeemAsync(code, verifier);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed.Succeeded, Is.True, changed.Failure?.Message);
            Assert.That(redeemed.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task Redeem_AfterAnAdministratorResetThePassword_IsRejectedAndNoTokenExists()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        var reset = await _host.People.ResetPasswordAsync(_owner.ToActor(), user.PersonId, NewPassword, CancellationToken.None);
        var redeemed = await _host.RedeemAsync(code, verifier);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reset.Succeeded, Is.True, reset.Failure?.Message);
            Assert.That(redeemed.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task Redeem_AfterTheAccountWasDisabledAndEnabledAgain_IsRejectedAndNoTokenExists()
    {
        // Arrange：停用之後再啟用，登入碼不能因此復活
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        await _host.People.SetDisabledAsync(_owner.ToActor(), user.PersonId, true, CancellationToken.None);
        await _host.People.SetDisabledAsync(_owner.ToActor(), user.PersonId, false, CancellationToken.None);
        var redeemed = await _host.RedeemAsync(code, verifier);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(redeemed.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task Redeem_AlreadyWaitingForTheAccountLockWhenThePasswordChanges_IsRejectedAndNoTokenExists()
    {
        // Arrange：兌換已經燒掉碼、正在等帳號的鎖，這時另一個交易改了密碼並讓這個帳號的登入碼失效
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var (code, verifier) = await _host.ApproveAsync(user);
        await using var held = await HeldPasswordChange.BeginAsync(_host.ConnectionString, user.AccountId, session.SessionId);
        var redeeming = Task.Run(() => _host.RedeemAsync(code, verifier));
        await held.WaitUntilSomeoneIsBlockedAsync();

        // Act
        await held.ExecuteAsync(
            "UPDATE identity.cli_login_codes SET used_at = coalesce(used_at, now()), expires_at = 'epoch' WHERE account_id = @id AND issued_token_ids = '{}'",
            ("id", user.AccountId));
        await held.CommitAsync();
        var redeemed = await redeeming.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(redeemed.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task Create_CallerSessionWasEnded_IsRejectedAndNothingIsCreated()
    {
        // Arrange：偷到 Cookie 的人用的工作階段，被本人「登出其他裝置」作廢了
        var user = await _host.AddUserAsync(_owner.ToActor());
        var stolen = await _host.StartSessionAsync(user);
        var mine = await _host.StartSessionAsync(user);
        await _host.Sessions.EndOthersAsync(mine.Token, TestData.Password, CancellationToken.None);
        _host.CurrentSession.Set(stolen.Token);

        // Act
        var result = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("CI", Read, 30), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.SessionEnded));
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task ApproveCliLogin_CallerSessionWasEnded_IsRejectedAndNoCodeIsIssued()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var stolen = await _host.StartSessionAsync(user);
        var mine = await _host.StartSessionAsync(user);
        await _host.Sessions.EndOthersAsync(mine.Token, TestData.Password, CancellationToken.None);
        _host.CurrentSession.Set(stolen.Token);
        var (_, challenge) = OAuthTestSupport.NewPkce();

        // Act
        var result = await _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(challenge), Grant(challenge: challenge), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.SessionEnded));
        Assert.That(await CountCodesAsync(user.AccountId), Is.Zero);
    }

    [Test]
    public async Task ApproveOAuth_CallerSessionWasEnded_IsRejectedAndNoGrantExists()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var stolen = await _host.StartSessionAsync(user);
        var mine = await _host.StartSessionAsync(user);
        await _host.Sessions.EndOthersAsync(mine.Token, TestData.Password, CancellationToken.None);
        var client = await _host.RegisterAsync();
        _host.CurrentSession.Set(stolen.Token);
        var (_, challenge) = OAuthTestSupport.NewPkce();

        // Act
        var result = await _host.OAuth.ApproveAsync(
            OAuthTestSupport.Server, user.ToActor(), OAuthTestSupport.Parameters(client.ClientId, challenge), AccessTokenScopes.Grantable, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.AccessDenied));
            Assert.That(await _host.OAuth.ListGrantsAsync(user.ToActor(), CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task Create_CallerSessionIsStillActive_Succeeds()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        _host.CurrentSession.Set(session.Token);

        // Act
        var result = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("CI", Read, 30), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task Create_NoCurrentSession_StillWorksEvenIfEverySessionEnded()
    {
        // Arrange：沒有請求脈絡（背景工作、測試）時沒有工作階段可檢查
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        await _host.Sessions.EndAsync(session.Token, CancellationToken.None);

        // Act
        var result = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("CI", Read, 30), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task Create_QueuedBehindAPasswordChangeThatEndedTheCallerSession_IsRejected()
    {
        // Arrange：建立權杖的請求卡在改密碼交易的帳號鎖後面
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        _host.CurrentSession.Set(session.Token);
        await using var held = await HeldPasswordChange.BeginAsync(_host.ConnectionString, user.AccountId, session.SessionId);
        var creating = Task.Run(() => _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("CI", Read, 30), CancellationToken.None));
        await held.WaitUntilSomeoneIsBlockedAsync();

        // Act：改密碼的交易提交之後，建立權杖的請求才拿到帳號的鎖
        await held.CommitAsync();
        var result = await creating.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.SessionEnded));
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task ApproveCliLogin_QueuedBehindAPasswordChangeThatEndedTheCallerSession_IsRejected()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        _host.CurrentSession.Set(session.Token);
        var (_, challenge) = OAuthTestSupport.NewPkce();
        await using var held = await HeldPasswordChange.BeginAsync(_host.ConnectionString, user.AccountId, session.SessionId);
        var approving = Task.Run(() => _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(challenge), Grant(challenge: challenge), CancellationToken.None));
        await held.WaitUntilSomeoneIsBlockedAsync();

        // Act
        await held.CommitAsync();
        var result = await approving.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.SessionEnded));
        Assert.That(await CountCodesAsync(user.AccountId), Is.Zero);
    }

    [Test]
    public async Task ApproveOAuth_QueuedBehindAPasswordChangeThatEndedTheCallerSession_IsRejected()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var client = await _host.RegisterAsync();
        _host.CurrentSession.Set(session.Token);
        var (_, challenge) = OAuthTestSupport.NewPkce();
        await using var held = await HeldPasswordChange.BeginAsync(_host.ConnectionString, user.AccountId, session.SessionId);
        var approving = Task.Run(() => _host.OAuth.ApproveAsync(
            OAuthTestSupport.Server, user.ToActor(), OAuthTestSupport.Parameters(client.ClientId, challenge), AccessTokenScopes.Grantable, CancellationToken.None));
        await held.WaitUntilSomeoneIsBlockedAsync();

        // Act
        await held.CommitAsync();
        var result = await approving.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.AccessDenied));
        Assert.That(await _host.OAuth.ListGrantsAsync(user.ToActor(), CancellationToken.None), Is.Empty);
    }

    private async Task<int> CountCodesAsync(Guid accountId)
    {
        await using var db = await _host.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.CliLoginCodes, c => c.AccountId == accountId);
    }
}
