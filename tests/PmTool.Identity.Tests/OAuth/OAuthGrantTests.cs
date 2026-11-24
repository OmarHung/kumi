using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>授權清單、撤銷（同一個程序裡立刻失效）、與個人存取權杖的分開（<c>internal/HANDOFF-mcp.md</c> 4.1、4.3）。</summary>
[TestFixture]
public sealed class OAuthGrantTests
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
    public async Task ListGrants_ShowsTheClientScopesAndWhenItWasLastUsed()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user, "代理甲");
        Assert.That((await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single().LastUsedAt, Is.EqualTo(_time.GetUtcNow()));
        _time.Advance(TimeSpan.FromMinutes(10));

        // Act
        await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None);
        var grant = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single();

        // Assert
        Assert.That(grant.ClientName, Is.EqualTo("代理甲"));
        Assert.That(grant.ClientHost, Is.Null);
        Assert.That(grant.Scopes, Is.EqualTo(AccessTokenScopes.Grantable));
        Assert.That(grant.LastUsedAt, Is.EqualTo(_time.GetUtcNow()), "權杖被用到時，授權的最後使用時刻跟著更新");
        Assert.That(client.ClientId, Does.StartWith("pmc_"));
    }

    [Test]
    public async Task ListGrants_MostRecentlyUsedFirst_AndOnlyTheOwn()
    {
        // Arrange
        var other = await _host.AddUserAsync(_owner.ToActor());
        await _host.ConnectAsync(_user, "舊的");
        _time.Advance(TimeSpan.FromMinutes(1));
        await _host.ConnectAsync(_user, "新的");
        await _host.ConnectAsync(other, "別人的");

        // Act
        var grants = await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None);

        // Assert
        Assert.That(grants.Select(g => g.ClientName), Is.EqualTo(new[] { "新的", "舊的" }));
    }

    [Test]
    public async Task RevokeGrant_EndsTheTokensAtOnceEvenWhenTheLookupWasCached()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Not.Null, "先讓查詢結果進快取");
        var grantId = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single().GrantId;

        // Act
        var result = await _host.OAuth.RevokeGrantAsync(_user.ToActor(), grantId, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Null);
        Assert.That((await _host.RefreshAsync(client.ClientId, tokens.RefreshToken)).Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task RevokeGrant_LeavesTheOtherGrantsAndThePersonalTokensAlone()
    {
        // Arrange
        var (_, mine) = await _host.ConnectAsync(_user, "要撤銷的");
        var (_, kept) = await _host.ConnectAsync(_user, "留著的");
        var personal = (await _host.AccessTokens.CreateAsync(
            _user.ToActor(), new NewAccessToken("腳本", [AccessTokenScope.ItemsRead], 30), CancellationToken.None)).Value;
        var grants = await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None);

        // Act
        await _host.OAuth.RevokeGrantAsync(_user.ToActor(), grants.Single(g => g.ClientName == "要撤銷的").GrantId, CancellationToken.None);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(mine.AccessToken, CancellationToken.None), Is.Null);
        Assert.That(await _host.AccessTokens.FindAsync(kept.AccessToken, CancellationToken.None), Is.Not.Null);
        Assert.That(await _host.AccessTokens.FindAsync(personal.Secret, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task RevokeGrant_SecondTime_IsNotFound()
    {
        // Arrange
        await _host.ConnectAsync(_user);
        var grantId = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single().GrantId;
        await _host.OAuth.RevokeGrantAsync(_user.ToActor(), grantId, CancellationToken.None);

        // Act
        var again = await _host.OAuth.RevokeGrantAsync(_user.ToActor(), grantId, CancellationToken.None);

        // Assert
        Assert.That(again.Failure?.Code, Is.EqualTo(IdentityErrorCodes.OAuthGrantNotFound));
        Assert.That(again.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
    }

    [Test]
    public async Task RevokeGrant_OfAnotherPersonOrOrganization_IsNotFoundAndChangesNothing()
    {
        // Arrange
        var (_, tokens) = await _host.ConnectAsync(_user);
        var grantId = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single().GrantId;
        var colleague = await _host.AddUserAsync(_owner.ToActor());
        var otherOrganization = _user.ToActor() with { OrganizationId = Guid.CreateVersion7() };

        // Act
        var byColleague = await _host.OAuth.RevokeGrantAsync(colleague.ToActor(), grantId, CancellationToken.None);
        var byOtherOrganization = await _host.OAuth.RevokeGrantAsync(otherOrganization, grantId, CancellationToken.None);

        // Assert
        Assert.That(byColleague.Failure?.Code, Is.EqualTo(IdentityErrorCodes.OAuthGrantNotFound));
        Assert.That(byOtherOrganization.Failure?.Code, Is.EqualTo(IdentityErrorCodes.OAuthGrantNotFound));
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task ListGrants_NeverShowsAnotherOrganization()
    {
        // Arrange
        await _host.ConnectAsync(_user);
        var otherOrganization = _user.ToActor() with { OrganizationId = Guid.CreateVersion7() };

        // Act
        var grants = await _host.OAuth.ListGrantsAsync(otherOrganization, CancellationToken.None);

        // Assert
        Assert.That(grants, Is.Empty);
    }

    [Test]
    public async Task OAuthTokens_AreNotCountedInThePersonalTokenLimitOrList()
    {
        // Arrange
        for (var i = 0; i < 3; i++)
        {
            await _host.ConnectAsync(_user, $"代理 {i}");
        }

        // Act
        for (var i = 0; i < AccessTokenRules.MaxActiveTokensPerAccount; i++)
        {
            var created = await _host.AccessTokens.CreateAsync(
                _user.ToActor(), new NewAccessToken($"個人 {i}", [AccessTokenScope.ItemsRead], 30), CancellationToken.None);
            Assert.That(created.Succeeded, Is.True, $"第 {i + 1} 把");
        }

        var over = await _host.AccessTokens.CreateAsync(
            _user.ToActor(), new NewAccessToken("多一把", [AccessTokenScope.ItemsRead], 30), CancellationToken.None);
        var listed = await _host.AccessTokens.ListAsync(_user.ToActor(), CancellationToken.None);

        // Assert
        Assert.That(over.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenLimitReached));
        Assert.That(listed, Has.Count.EqualTo(AccessTokenRules.MaxActiveTokensPerAccount));
        Assert.That(listed.Select(t => t.Name), Has.None.StartWith("代理"));
    }

    [Test]
    public async Task PersonalRevoke_CannotEndAnOAuthToken()
    {
        // Arrange
        var (_, tokens) = await _host.ConnectAsync(_user);
        await using var db = await _host.CreateDbContextAsync();
        var tokenId = (await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None))!.TokenId;

        // Act
        var result = await _host.AccessTokens.RevokeAsync(_user.ToActor(), tokenId, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenNotFound));
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task DisablingTheAccount_EndsTheOAuthTokensToo()
    {
        // Arrange
        var person = await _host.AddUserAsync(_owner.ToActor());
        var (_, tokens) = await _host.ConnectAsync(person);

        // Act
        await _host.DisableAccountAsync(person.AccountId);
        _time.Advance(TimeSpan.FromMinutes(1));

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Reauthorizing_WithTheSameScopes_KeepsTheEarlierTokensValid()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);
        var first = (await _host.ExchangeAsync(client.ClientId, code, verifier)).Value;

        // Act
        await _host.ApproveAsync(_user, client.ClientId);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(first.AccessToken, CancellationToken.None), Is.Not.Null);
        Assert.That(await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AfterRevoking_TheClientCanAuthorizeAgainAsANewGrant()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        await _host.ApproveAsync(_user, client.ClientId);
        var first = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single();
        await _host.OAuth.RevokeGrantAsync(_user.ToActor(), first.GrantId, CancellationToken.None);

        // Act
        await _host.ApproveAsync(_user, client.ClientId);

        // Assert
        var grants = await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None);
        Assert.That(grants.Single().GrantId, Is.Not.EqualTo(first.GrantId));
        await using var db = await _host.CreateDbContextAsync();
        Assert.That(await db.OAuthGrants.CountAsync(g => g.ClientId == client.ClientId && g.AccountId == _user.AccountId), Is.EqualTo(2));
    }

    [Test]
    public async Task TokenRowsOfAGrant_CarryTheOrganizationAndTheGrant()
    {
        // Arrange
        var (_, tokens) = await _host.ConnectAsync(_user);

        // Act
        var active = await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None);
        var row = await _host.ReadAccessTokenRowAsync(active!.TokenId);

        // Assert
        Assert.That(row.GrantId, Is.EqualTo(active.GrantId));
        Assert.That(row.OrganizationId, Is.EqualTo(_user.OrganizationId));
        Assert.That(row.ExpiresAt, Is.EqualTo(_time.GetUtcNow() + OAuthRules.AccessTokenLifetime));
        Assert.That(row.Name, Is.EqualTo("範例代理"));
    }
}
