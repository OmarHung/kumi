using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>改密碼、停用、管理員重設密碼：這個帳號所有的 OAuth 授權（含更新權杖）一起結束，不會因為之後啟用而復活。</summary>
[TestFixture]
public sealed class OAuthAccountSecurityTests
{
    private const string NewPassword = "brand-new-password";

    private readonly ManualTimeProvider _time = new();
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;
    private AuthenticatedUser _user = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await OAuthTestSupport.StartAsync(_time);
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [SetUp]
    public async Task AddUserAsync() => _user = await _host.AddUserAsync(_owner.ToActor());

    [Test]
    public async Task ResetPasswordByAdmin_EndsTheGrantsSoARefreshTokenCannotBeExchanged()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Not.Null);

        // Act
        await _host.People.ResetPasswordAsync(_owner.ToActor(), _user.PersonId, NewPassword, CancellationToken.None);

        // Assert
        Assert.That((await _host.RefreshAsync(client.ClientId, tokens.RefreshToken)).Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Null);
        Assert.That(await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task ChangeOwnPassword_EndsTheGrantsEvenThoughPersonalTokensKeepWorking()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);
        var personal = (await _host.AccessTokens.CreateAsync(
            _user.ToActor(), new NewAccessToken("腳本", [AccessTokenScope.ItemsRead], 30), CancellationToken.None)).Value;
        var session = await _host.StartSessionAsync(_user);
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Not.Null);

        // Act
        var changed = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, NewPassword, CancellationToken.None);

        // Assert
        Assert.That(changed.Succeeded, Is.True, changed.Failure?.Message);
        Assert.That((await _host.RefreshAsync(client.ClientId, tokens.RefreshToken)).Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Null);
        Assert.That(await _host.AccessTokens.FindAsync(personal.Secret, CancellationToken.None), Is.Not.Null, "個人存取權杖照舊不撤銷");
    }

    [Test]
    public async Task DisableThenEnable_DoesNotBringTheOldRefreshTokenBack()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);

        // Act
        await _host.People.SetDisabledAsync(_owner.ToActor(), _user.PersonId, true, CancellationToken.None);
        await _host.People.SetDisabledAsync(_owner.ToActor(), _user.PersonId, false, CancellationToken.None);

        // Assert
        Assert.That((await _host.RefreshAsync(client.ClientId, tokens.RefreshToken)).Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ResetPassword_LeavesOtherPeoplesGrantsAlone()
    {
        // Arrange
        var other = await _host.AddUserAsync(_owner.ToActor());
        var (_, mine) = await _host.ConnectAsync(_user);
        var (otherClient, theirs) = await _host.ConnectAsync(other);

        // Act
        await _host.People.ResetPasswordAsync(_owner.ToActor(), _user.PersonId, NewPassword, CancellationToken.None);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(mine.AccessToken, CancellationToken.None), Is.Null);
        Assert.That(await _host.AccessTokens.FindAsync(theirs.AccessToken, CancellationToken.None), Is.Not.Null);
        Assert.That((await _host.RefreshAsync(otherClient.ClientId, theirs.RefreshToken)).Succeeded, Is.True);
    }
}
