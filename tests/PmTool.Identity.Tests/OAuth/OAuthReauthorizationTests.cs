using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>同一個用戶端再授權一次、範圍縮小時，超過新範圍的權杖當場失效（<c>internal/HANDOFF-mcp.md</c> 4.2）。</summary>
[TestFixture]
public sealed class OAuthReauthorizationTests
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
    public async Task Approve_NarrowerScopes_RevokesTheTokensBeyondTheNewScopeAndKeepsTheOthers()
    {
        // Arrange：一把全範圍的、一把只有讀的；兩把都先查過，結果進了快取。
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);
        var broad = (await _host.ExchangeAsync(client.ClientId, code, verifier)).Value;
        var narrow = (await _host.RefreshAsync(client.ClientId, broad.RefreshToken, "items:read")).Value;
        Assert.That(await _host.AccessTokens.FindAsync(broad.AccessToken, CancellationToken.None), Is.Not.Null);
        Assert.That(await _host.AccessTokens.FindAsync(narrow.AccessToken, CancellationToken.None), Is.Not.Null);

        // Act
        await _host.ApproveAsync(_user, client.ClientId, scopes: [AccessTokenScope.ItemsRead]);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(broad.AccessToken, CancellationToken.None), Is.Null, "範圍超過新範圍的權杖失效");
        Assert.That(await _host.AccessTokens.FindAsync(narrow.AccessToken, CancellationToken.None), Is.Not.Null, "範圍在新範圍之內的照舊有效");
    }

    [Test]
    public async Task Approve_NarrowerScopes_RefreshTokenAfterwardsIssuesOnlyTheNewScopes()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);

        // Act
        await _host.ApproveAsync(_user, client.ClientId, scopes: [AccessTokenScope.ItemsRead]);
        var refreshed = await _host.RefreshAsync(client.ClientId, tokens.RefreshToken);

        // Assert
        Assert.That(refreshed.Value.Scope, Is.EqualTo("items:read"));
        var active = await _host.AccessTokens.FindAsync(refreshed.Value.AccessToken, CancellationToken.None);
        Assert.That(active?.Scopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead }));
    }

    [Test]
    public async Task Approve_NarrowerScopes_ACodeIssuedBeforeCannotBeExchangedForTheOldScopes()
    {
        // Arrange：先拿到一個全範圍的授權碼，沒兌換；再用縮小的範圍授權一次。
        var client = await _host.RegisterAsync();
        var (oldCode, oldVerifier) = await _host.ApproveAsync(_user, client.ClientId);
        await _host.ApproveAsync(_user, client.ClientId, scopes: [AccessTokenScope.ItemsRead]);

        // Act
        var tokens = await _host.ExchangeAsync(client.ClientId, oldCode, oldVerifier);

        // Assert
        Assert.That(tokens.Value.Scope, Is.EqualTo("items:read"));
    }

    [Test]
    public async Task Approve_SameOrWiderScopes_KeepsTheExistingTokens()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId, scopes: [AccessTokenScope.ItemsRead]);
        var tokens = (await _host.ExchangeAsync(client.ClientId, code, verifier)).Value;

        // Act
        await _host.ApproveAsync(_user, client.ClientId, scopes: [AccessTokenScope.ItemsRead]);
        await _host.ApproveAsync(_user, client.ClientId);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(tokens.AccessToken, CancellationToken.None), Is.Not.Null);
        Assert.That((await _host.RefreshAsync(client.ClientId, tokens.RefreshToken)).Succeeded, Is.True);
    }
}
