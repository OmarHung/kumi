using Microsoft.EntityFrameworkCore;
using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>Token 端點：授權碼換權杖、更新權杖輪替、重複使用的偵測（<c>internal/HANDOFF-mcp.md</c> 4.3）。</summary>
[TestFixture]
public sealed class OAuthTokenTests
{
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
    public async Task ExchangeCode_Succeeds_WithATokenThatIdentifiesTheUserAndTheGrant()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId, scopes: [AccessTokenScope.ItemsRead]);

        // Act
        var tokens = (await _host.ExchangeAsync(client.ClientId, code, verifier)).Value;

        // Assert
        Assert.That(tokens.AccessToken, Does.StartWith(AccessTokenRules.Prefix));
        Assert.That(tokens.RefreshToken, Does.StartWith(OAuthRules.RefreshTokenPrefix));
        Assert.That(tokens.ExpiresIn, Is.EqualTo(3600));
        Assert.That(tokens.Scope, Is.EqualTo("items:read"));
        var active = await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None);
        var grant = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single();
        Assert.That(active?.User.PersonId, Is.EqualTo(_user.PersonId));
        Assert.That(active?.Scopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead }));
        Assert.That(active?.GrantId, Is.EqualTo(grant.GrantId));
    }

    [Test]
    public async Task ExchangeCode_TokenStringsNeverAppearInToString()
    {
        // Arrange
        var (_, tokens) = await _host.ConnectAsync(_user);

        // Act
        var text = tokens.ToString();

        // Assert
        Assert.That(text, Does.Not.Contain(tokens.AccessToken).And.Not.Contain(tokens.RefreshToken));
    }

    [Test]
    public async Task ExchangeCode_PersonalTokensHaveNoGrantId()
    {
        // Arrange
        var created = (await _host.AccessTokens.CreateAsync(
            _user.ToActor(), new NewAccessToken("腳本", [AccessTokenScope.ItemsRead], 30), CancellationToken.None)).Value;

        // Act
        var active = await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);

        // Assert
        Assert.That(active?.GrantId, Is.Null);
    }

    [Test]
    public async Task ExchangeCode_WrongVerifier_IsInvalidGrantAndDoesNotBurnTheCode()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);

        // Act
        var wrong = await _host.ExchangeAsync(client.ClientId, code, NewPkce().Verifier);
        var right = await _host.ExchangeAsync(client.ClientId, code, verifier);

        // Assert
        Assert.That(wrong.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(right.Succeeded, Is.True);
    }

    [TestCase("short")]
    [TestCase("has space in the verifier that is long enough to pass the length check ok")]
    public async Task ExchangeCode_MalformedVerifier_IsInvalidGrant(string verifier)
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, _) = await _host.ApproveAsync(_user, client.ClientId);

        // Act
        var result = await _host.ExchangeAsync(client.ClientId, code, verifier);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task ExchangeCode_OtherClient_IsInvalidGrant()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var other = await _host.RegisterAsync("另一個");
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);

        // Act
        var result = await _host.ExchangeAsync(other.ClientId, code, verifier);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task ExchangeCode_OtherRedirectUri_IsInvalidGrant()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId, "http://127.0.0.1:5000/callback");

        // Act
        var result = await _host.ExchangeAsync(client.ClientId, code, verifier, "http://127.0.0.1:5001/callback");

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant), "換權杖時的導回網址要和授權時的完全一樣，連埠也是");
    }

    [Test]
    public async Task ExchangeCode_OtherResource_IsInvalidTarget()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);
        var request = new OAuthCodeExchange(code, Loopback, client.ClientId, verifier, "https://other.example.com/mcp");

        // Act
        var result = await _host.OAuth.ExchangeCodeAsync(Server, request, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidTarget));
    }

    [Test]
    public async Task ExchangeCode_WithoutResource_UsesTheOneFromTheAuthorization()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);
        var request = new OAuthCodeExchange(code, Loopback, client.ClientId, verifier, null);

        // Act
        var result = await _host.OAuth.ExchangeCodeAsync(Server, request, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task ExchangeCode_MissingParameters_IsInvalidRequest()
    {
        // Act
        var result = await _host.OAuth.ExchangeCodeAsync(Server, new OAuthCodeExchange(null, null, null, null, null), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
    }

    [TestCase("")]
    [TestCase("not-a-code")]
    public async Task ExchangeCode_CodeThatWeNeverIssued_IsInvalidGrant(string code)
    {
        // Arrange
        var client = await _host.RegisterAsync();

        // Act
        var result = await _host.ExchangeAsync(client.ClientId, code.Length == 0 ? "x" : code, NewPkce().Verifier);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task ExchangeCode_CodeWithControlCharacters_IsInvalidRequestWithoutTouchingTheDatabase()
    {
        // Arrange
        var client = await _host.RegisterAsync();

        // Act
        var result = await _host.ExchangeAsync(client.ClientId, "a\0b", NewPkce().Verifier);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
    }

    [Test]
    public async Task ExchangeCode_Expired_IsInvalidGrant()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);
        _time.Advance(OAuthRules.CodeLifetime + TimeSpan.FromSeconds(1));

        // Act
        var result = await _host.ExchangeAsync(client.ClientId, code, verifier);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task ExchangeCode_Reused_IsInvalidGrantAndRevokesTheTokensItIssued()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);
        var first = (await _host.ExchangeAsync(client.ClientId, code, verifier)).Value;
        var refreshed = (await _host.RefreshAsync(client.ClientId, first.RefreshToken)).Value;
        Assert.That(await _host.AccessTokens.FindAsync(first.AccessToken, CancellationToken.None), Is.Not.Null);

        // Act
        var second = await _host.ExchangeAsync(client.ClientId, code, verifier);

        // Assert
        Assert.That(second.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await _host.AccessTokens.FindAsync(first.AccessToken, CancellationToken.None), Is.Null);
        Assert.That(await _host.AccessTokens.FindAsync(refreshed.AccessToken, CancellationToken.None), Is.Null, "換新之後發的也在同一串裡");
        Assert.That((await _host.RefreshAsync(client.ClientId, refreshed.RefreshToken)).Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task ExchangeCode_TwoRequestsAtOnce_OnlyOneGetsTokens()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _host.ExchangeAsync(client.ClientId, code, verifier)));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public async Task ExchangeCode_AccessTokenExpiresAfterAnHour()
    {
        // Arrange
        var (_, tokens) = await _host.ConnectAsync(_user);
        _time.Advance(OAuthRules.AccessTokenLifetime - TimeSpan.FromMinutes(1));
        var before = await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None);

        // Act
        _time.Advance(TimeSpan.FromMinutes(2));
        var after = await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None);

        // Assert
        Assert.That(before, Is.Not.Null);
        Assert.That(after, Is.Null);
    }

    [Test]
    public async Task Refresh_RotatesTheRefreshTokenAndKeepsTheScopes()
    {
        // Arrange
        var (client, first) = await _host.ConnectAsync(_user);

        // Act
        var second = (await _host.RefreshAsync(client.ClientId, first.RefreshToken)).Value;

        // Assert
        Assert.That(second.RefreshToken, Is.Not.EqualTo(first.RefreshToken));
        Assert.That(second.AccessToken, Is.Not.EqualTo(first.AccessToken));
        Assert.That(second.Scope, Is.EqualTo(first.Scope));
        Assert.That(await _host.AccessTokens.FindAsync(second.AccessToken, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task Refresh_UsedTokenAgain_RevokesTheWholeGrant()
    {
        // Arrange
        var (client, first) = await _host.ConnectAsync(_user);
        var second = (await _host.RefreshAsync(client.ClientId, first.RefreshToken)).Value;

        // Act
        var replay = await _host.RefreshAsync(client.ClientId, first.RefreshToken);

        // Assert
        Assert.That(replay.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await _host.AccessTokens.FindAsync(second.AccessToken, CancellationToken.None), Is.Null);
        Assert.That((await _host.RefreshAsync(client.ClientId, second.RefreshToken)).Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task Refresh_NarrowerScope_OnlyAffectsThisIssue()
    {
        // Arrange
        var (client, first) = await _host.ConnectAsync(_user);

        // Act
        var narrowed = (await _host.RefreshAsync(client.ClientId, first.RefreshToken, "items:read")).Value;
        var full = (await _host.RefreshAsync(client.ClientId, narrowed.RefreshToken)).Value;

        // Assert
        Assert.That(narrowed.Scope, Is.EqualTo("items:read"));
        Assert.That(full.Scope, Is.EqualTo(first.Scope));
    }

    [TestCase("items:read nonsense")]
    [TestCase("nonsense")]
    public async Task Refresh_UnknownScope_IsInvalidScope(string scope)
    {
        // Arrange
        var (client, first) = await _host.ConnectAsync(_user);

        // Act
        var result = await _host.RefreshAsync(client.ClientId, first.RefreshToken, scope);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidScope));
    }

    [Test]
    public async Task Refresh_ScopeBeyondTheGrant_IsInvalidScope()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId, scopes: [AccessTokenScope.ItemsRead]);
        var tokens = (await _host.ExchangeAsync(client.ClientId, code, verifier)).Value;

        // Act
        var result = await _host.RefreshAsync(client.ClientId, tokens.RefreshToken, "items:read items:write");

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidScope));
    }

    [Test]
    public async Task Refresh_OtherClient_IsInvalidGrant()
    {
        // Arrange
        var (_, tokens) = await _host.ConnectAsync(_user);
        var other = await _host.RegisterAsync("另一個");

        // Act
        var result = await _host.RefreshAsync(other.ClientId, tokens.RefreshToken);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task Refresh_OtherResource_IsInvalidTarget()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);

        // Act
        var result = await _host.OAuth.RefreshAsync(
            Server, new OAuthRefreshRequest(tokens.RefreshToken, client.ClientId, "https://other.example.com/mcp", null), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidTarget));
    }

    [TestCase(null, "client")]
    [TestCase("pmr_x", null)]
    public async Task Refresh_MissingParameters_IsInvalidRequest(string? refreshToken, string? clientId)
    {
        // Act
        var result = await _host.OAuth.RefreshAsync(Server, new OAuthRefreshRequest(refreshToken, clientId, null, null), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
    }

    [TestCase("pmr_nonsense")]
    [TestCase("pmt_nonsense")]
    [TestCase("pmr_a\0b")]
    public async Task Refresh_TokenThatWeNeverIssued_IsInvalidGrant(string token)
    {
        // Arrange
        var client = await _host.RegisterAsync();

        // Act
        var result = await _host.RefreshAsync(client.ClientId, token);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task Refresh_AfterThirtyDaysWithoutUse_IsInvalidGrant()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);
        _time.Advance(OAuthRules.RefreshTokenLifetime + TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.RefreshAsync(client.ClientId, tokens.RefreshToken);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task Refresh_KeepsWorkingAsLongAsItIsUsed()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);

        // Act
        for (var i = 0; i < 3; i++)
        {
            _time.Advance(TimeSpan.FromDays(20));
            tokens = (await _host.RefreshAsync(client.ClientId, tokens.RefreshToken)).Value;
        }

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task Refresh_DisabledAccount_IsInvalidGrant()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);
        await _host.DisableAccountAsync(_user.AccountId);

        // Act
        var result = await _host.RefreshAsync(client.ClientId, tokens.RefreshToken);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
    }

    [Test]
    public async Task Refresh_TwoRequestsAtOnce_AtMostOneSucceeds()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _host.RefreshAsync(client.ClientId, tokens.RefreshToken)));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public async Task RevokeToken_AccessToken_EndsOnlyThatToken()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);

        // Act
        await _host.OAuth.RevokeTokenAsync(tokens.AccessToken, CancellationToken.None);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Null);
        Assert.That((await _host.RefreshAsync(client.ClientId, tokens.RefreshToken)).Succeeded, Is.True);
    }

    [Test]
    public async Task RevokeToken_RefreshToken_EndsTheWholeGrant()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);

        // Act
        await _host.OAuth.RevokeTokenAsync(tokens.RefreshToken, CancellationToken.None);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Null);
        Assert.That((await _host.RefreshAsync(client.ClientId, tokens.RefreshToken)).Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task RevokeToken_PersonalToken_IsIgnored()
    {
        // Arrange
        var created = (await _host.AccessTokens.CreateAsync(
            _user.ToActor(), new NewAccessToken("腳本", [AccessTokenScope.ItemsRead], 30), CancellationToken.None)).Value;

        // Act
        await _host.OAuth.RevokeTokenAsync(created.Secret, CancellationToken.None);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Not.Null, "個人存取權杖只能在網頁撤銷");
    }

    [TestCase("")]
    [TestCase("garbage")]
    [TestCase("pmt_garbage")]
    [TestCase("pmr_garbage")]
    [TestCase("pmr_a\0b")]
    public async Task RevokeToken_UnknownToken_StillSucceeds(string token)
    {
        // Act & Assert
        Assert.That(async () => await _host.OAuth.RevokeTokenAsync(token, CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task IssuingTokens_CleansUpOldData()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (client, first) = await _host.ConnectAsync(user);
        await _host.RefreshAsync(client.ClientId, first.RefreshToken);
        _time.Advance(OAuthRules.RefreshTokenLifetime + TimeSpan.FromDays(2));
        var fresh = await _host.RegisterAsync("新的");

        // Act
        await _host.ConnectAfterAsync(user, fresh);

        // Assert
        await using var db = await _host.CreateDbContextAsync();
        var org = user.OrganizationId;
        Assert.That(await db.OAuthCodes.CountAsync(c => c.OrganizationId == org && c.ExpiresAt < _time.GetUtcNow().AddDays(-1)), Is.Zero);
        Assert.That(await db.OAuthRefreshTokens.CountAsync(t => t.OrganizationId == org && t.ExpiresAt < _time.GetUtcNow()), Is.Zero);
        Assert.That(
            await db.AccessTokens.CountAsync(t => t.OrganizationId == org && t.GrantId != null && t.ExpiresAt < _time.GetUtcNow().AddDays(-1)),
            Is.Zero);
    }
}

internal static class OAuthTokenTestExtensions
{
    /// <summary>已經有註冊好的用戶端時，授權並換權杖。</summary>
    public static async Task ConnectAfterAsync(this IdentityTestHost host, AuthenticatedUser user, OAuthRegisteredClient client)
    {
        var (code, verifier) = await host.ApproveAsync(user, client.ClientId);
        var tokens = await host.ExchangeAsync(client.ClientId, code, verifier);
        Assert.That(tokens.Succeeded, Is.True);
    }
}
