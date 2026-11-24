using Microsoft.EntityFrameworkCore;
using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>動態註冊（RFC 7591，<c>internal/HANDOFF-mcp.md</c> 4.5）。</summary>
[TestFixture]
public sealed class OAuthRegistrationTests
{
    private readonly ManualTimeProvider _time = new();
    private IdentityTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await OAuthTestSupport.StartAsync(_time);

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private Task<OAuthResult<OAuthRegisteredClient>> RegisterAsync(
        string? name = "範例代理",
        IReadOnlyList<string>? redirects = null,
        string? method = "none",
        IReadOnlyList<string>? grants = null,
        IReadOnlyList<string>? responses = null) =>
        _host.OAuth.RegisterClientAsync(
            new OAuthClientRegistration(name, redirects ?? [Loopback], method, grants, responses), CancellationToken.None);

    [Test]
    public async Task Register_Valid_IssuesAClientIdWithoutASecret()
    {
        // Act
        var result = await RegisterAsync("  範例代理  ", [Loopback, "http://localhost:8080/cb"], grants: ["authorization_code", "refresh_token"], responses: ["code"]);

        // Assert
        var client = result.Value;
        Assert.That(client.ClientId, Does.StartWith("pmc_"));
        Assert.That(client.ClientId, Has.Length.EqualTo(4 + 43));
        Assert.That(client.ClientName, Is.EqualTo("範例代理"));
        Assert.That(client.RedirectUris, Is.EqualTo(new[] { Loopback, "http://localhost:8080/cb" }));
        Assert.That(client.IssuedAt, Is.EqualTo(_time.GetUtcNow()));
    }

    [Test]
    public async Task Register_TwiceGivesDifferentClientIds()
    {
        // Act
        var first = (await RegisterAsync()).Value;
        var second = (await RegisterAsync()).Value;

        // Assert
        Assert.That(first.ClientId, Is.Not.EqualTo(second.ClientId));
    }

    [Test]
    public async Task Register_NoAuthMethodOrGrantTypes_IsAccepted()
    {
        // Act
        var result = await RegisterAsync(method: null);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task Register_DuplicateRedirects_AreMerged()
    {
        // Act
        var result = await RegisterAsync(redirects: [Loopback, Loopback]);

        // Assert
        Assert.That(result.Value.RedirectUris, Has.Count.EqualTo(1));
    }

    [TestCase("client_secret_post")]
    [TestCase("client_secret_basic")]
    public async Task Register_AskingForAClientSecret_IsRegisteredAsAPublicClientWithoutASecret(string method)
    {
        // Arrange：MCP 官方的 C# SDK 註冊時一律要 client_secret_post；RFC 7591 允許伺服器換成自己支援的值。

        // Act
        var result = await RegisterAsync(method: method);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value.ClientId, Does.StartWith("pmc_"));
    }

    [TestCase("private_key_jwt")]
    [TestCase("tls_client_auth")]
    [TestCase("client_secret_jwt")]
    public async Task Register_AuthMethodThatCannotBeTurnedIntoAPublicClient_IsInvalidClientMetadata(string method)
    {
        Assert.That((await RegisterAsync(method: method)).Failure?.Error, Is.EqualTo(OAuthError.InvalidClientMetadata));
    }

    [Test]
    public async Task Register_UnsupportedGrantOrResponseTypes_AreInvalidClientMetadata()
    {
        Assert.That((await RegisterAsync(grants: ["password"])).Failure?.Error, Is.EqualTo(OAuthError.InvalidClientMetadata));
        Assert.That((await RegisterAsync(grants: ["authorization_code", "implicit"])).Failure?.Error, Is.EqualTo(OAuthError.InvalidClientMetadata));
        Assert.That((await RegisterAsync(responses: ["token"])).Failure?.Error, Is.EqualTo(OAuthError.InvalidClientMetadata));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("‎‮")]
    public async Task Register_NameMissingOrEmptyAfterCleaning_IsInvalidClientMetadata(string? name)
    {
        Assert.That((await RegisterAsync(name)).Failure?.Error, Is.EqualTo(OAuthError.InvalidClientMetadata));
    }

    [Test]
    public async Task Register_NameTooLong_IsInvalidClientMetadata()
    {
        Assert.That((await RegisterAsync(new string('名', OAuthRules.MaxClientNameLength + 1))).Failure?.Error, Is.EqualTo(OAuthError.InvalidClientMetadata));
    }

    [Test]
    public async Task Register_NameWithControlAndBidiCharacters_IsStoredWithoutThem()
    {
        // Act
        var result = await RegisterAsync("好\0代\n理‮");

        // Assert
        Assert.That(result.Value.ClientName, Is.EqualTo("好代理"));
    }

    [Test]
    public async Task Register_NoRedirects_IsInvalidRedirectUri()
    {
        Assert.That((await RegisterAsync(redirects: [])).Failure?.Error, Is.EqualTo(OAuthError.InvalidRedirectUri));
    }

    [Test]
    public async Task Register_TooManyRedirects_IsInvalidRedirectUri()
    {
        var many = Enumerable.Range(0, OAuthRules.MaxRedirectUris + 1).Select(i => $"http://127.0.0.1:5000/cb{i}").ToArray();
        Assert.That((await RegisterAsync(redirects: many)).Failure?.Error, Is.EqualTo(OAuthError.InvalidRedirectUri));
    }

    [TestCase("ftp://app.example.com/cb")]
    [TestCase("myapp://callback")]
    [TestCase("http://app.example.com/cb")]
    [TestCase("https://app.example.com/cb#frag")]
    [TestCase("https://user:pw@app.example.com/cb")]
    [TestCase("/relative/path")]
    [TestCase("not a url")]
    [TestCase("https://app.example.com/a\0b")]
    public async Task Register_RedirectBreakingTheRules_IsInvalidRedirectUri(string redirect)
    {
        Assert.That((await RegisterAsync(redirects: [Loopback, redirect])).Failure?.Error, Is.EqualTo(OAuthError.InvalidRedirectUri));
    }

    [Test]
    public async Task Register_ExternalHttpsRedirect_IsRefusedBecauseTheClientIsUnverified()
    {
        // Act
        var result = await RegisterAsync(redirects: [Loopback, "https://app.example.com/cb"]);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRedirectUri));
        Assert.That(result.Failure?.Description, Does.Contain("沒有經過驗證的應用只能導回這台電腦上的程式"));
    }

    [TestCase("http://127.0.0.1:8080/cb")]
    [TestCase("http://[::1]:8080/cb")]
    [TestCase("http://localhost:8080/cb")]
    [TestCase("https://localhost/cb")]
    public async Task Register_LoopbackRedirects_MayBeHttp(string redirect)
    {
        Assert.That((await RegisterAsync(redirects: [redirect])).Succeeded, Is.True);
    }

    [Test]
    public async Task Register_RedirectTooLong_IsInvalidRedirectUri()
    {
        var long_ = "http://127.0.0.1/" + new string('a', OAuthRules.MaxRedirectUriLength);
        Assert.That((await RegisterAsync(redirects: [long_])).Failure?.Error, Is.EqualTo(OAuthError.InvalidRedirectUri));
    }

    [Test]
    public async Task Register_PurgesClientsUnusedForNinetyDays_ButKeepsTheOnesWithAnActiveGrant()
    {
        // Arrange
        var owner = await _host.StartWithOwnerAsync();
        var stale = (await RegisterAsync()).Value;
        var inUse = (await RegisterAsync()).Value;
        await _host.ApproveAsync(owner, inUse.ClientId);
        var recent = (await RegisterAsync()).Value;
        _time.Advance(TimeSpan.FromDays(91));
        await _host.OAuth.ApproveAsync(Server, owner.ToActor(), Parameters(recent.ClientId, NewPkce().Challenge), AccessTokenScopes.Grantable, CancellationToken.None);

        // Act
        await RegisterAsync();

        // Assert
        await using var db = await _host.CreateDbContextAsync();
        var remaining = await db.OAuthClients.Select(c => c.ClientId).ToListAsync();
        Assert.That(remaining, Does.Not.Contain(stale.ClientId));
        Assert.That(remaining, Does.Contain(inUse.ClientId).And.Contain(recent.ClientId));
    }
}
