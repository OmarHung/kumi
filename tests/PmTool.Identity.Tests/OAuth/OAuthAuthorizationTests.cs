using Microsoft.EntityFrameworkCore;
using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>授權請求的檢查、允許、拒絕（<c>internal/HANDOFF-mcp.md</c> 4.2）。</summary>
[TestFixture]
public sealed class OAuthAuthorizationTests
{
    private readonly ManualTimeProvider _time = new();
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;
    private OAuthRegisteredClient _client = null!;
    private string _challenge = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await OAuthTestSupport.StartAsync(_time);
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [SetUp]
    public async Task RegisterClientAsync()
    {
        _client = await _host.RegisterAsync("範例代理", Loopback);
        _challenge = NewPkce().Challenge;
    }

    [Test]
    public async Task Check_ValidRequest_DescribesTheClientAndTheRedirect()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge);

        // Act
        var result = await _host.OAuth.CheckAuthorizationAsync(Server, parameters, CancellationToken.None);

        // Assert
        var request = result.Value;
        Assert.That(request.ClientId, Is.EqualTo(_client.ClientId));
        Assert.That(request.ClientName, Is.EqualTo("範例代理"));
        Assert.That(request.ClientHost, Is.Null, "動態註冊的用戶端沒有驗證過的網域");
        Assert.That(request.RedirectHost, Is.EqualTo("127.0.0.1:5000"));
        Assert.That(request.RedirectIsLoopback, Is.True);
        Assert.That(request.RequestedScopes, Is.EqualTo(AccessTokenScopes.Grantable));
    }

    [Test]
    public async Task Check_HttpsRedirectOfAnUnverifiedClient_IsRefusedEvenIfTheRowSomehowHasIt()
    {
        // Arrange：註冊時就擋了，這裡直接寫資料表，模擬資料不乾淨。
        var clientId = await _host.InsertRegisteredClientAsync("https://app.example.com/callback");

        // Act
        var result = await _host.OAuth.CheckAuthorizationAsync(
            Server, Parameters(clientId, _challenge, "https://app.example.com/callback"), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
        Assert.That(result.Failure?.Description, Does.Contain("沒有經過驗證"));
        Assert.That(result.Failure?.RedirectTo, Is.Null, "導回網址不能信，不導回");
    }

    [Test]
    public async Task Check_ListedScopes_AreRequestedAndUnknownOnesAreDropped()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge, scope: "items:read nonsense blueprint:read");

        // Act
        var request = (await _host.OAuth.CheckAuthorizationAsync(Server, parameters, CancellationToken.None)).Value;

        // Assert
        Assert.That(request.RequestedScopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead }));
    }

    [Test]
    public async Task Check_AgentRunScope_IsNotGrantableByOAuthNeitherListedNorImplied()
    {
        // Arrange
        var asked = Parameters(_client.ClientId, _challenge, scope: "items:read agent:run");
        var everything = Parameters(_client.ClientId, _challenge);
        var onlyThat = Parameters(_client.ClientId, _challenge, scope: "agent:run");

        // Act
        var listed = (await _host.OAuth.CheckAuthorizationAsync(Server, asked, CancellationToken.None)).Value;
        var implied = (await _host.OAuth.CheckAuthorizationAsync(Server, everything, CancellationToken.None)).Value;
        var rejected = await _host.OAuth.CheckAuthorizationAsync(Server, onlyThat, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.RequestedScopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead }));
            Assert.That(implied.RequestedScopes, Does.Not.Contain(AccessTokenScope.AgentRun));
            Assert.That(rejected.Failure?.Error, Is.EqualTo(OAuthError.InvalidScope));
        }
    }

    [Test]
    public async Task Approve_AgentRunScope_IsInvalidScopeEvenIfTheClientAsksForIt()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge, scope: "items:read agent:run");

        // Act
        var result = await _host.OAuth.ApproveAsync(
            Server, _owner.ToActor(), parameters, [AccessTokenScope.ItemsRead, AccessTokenScope.AgentRun], CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidScope));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("pmc_does-not-exist")]
    [TestCase("not-a-client")]
    [TestCase("http://app.example.com/client.json")]
    [TestCase("pmc_a\0b")]
    public async Task Check_BadClient_IsInvalidClientAndCannotRedirect(string? clientId)
    {
        // Act
        var result = await _host.OAuth.CheckAuthorizationAsync(Server, Parameters(clientId!, _challenge), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
        Assert.That(result.Failure?.RedirectTo, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("http://127.0.0.1:5000/other")]
    [TestCase("https://app.example.com/callback")]
    [TestCase("https://evil.example.org/callback")]
    [TestCase("http://app.example.com/callback")]
    [TestCase("http://localhost:5000/callback")]
    [TestCase("http://127.0.0.1:5000/callback#fragment")]
    [TestCase("not a url")]
    public async Task Check_RedirectNotRegistered_IsInvalidRequestAndCannotRedirect(string? redirect)
    {
        // Act
        var result = await _host.OAuth.CheckAuthorizationAsync(Server, Parameters(_client.ClientId, _challenge, redirect!), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
        Assert.That(result.Failure?.RedirectTo, Is.Null, "導回網址沒驗過就不能導回");
    }

    [TestCase("http://127.0.0.1:1/callback")]
    [TestCase("http://127.0.0.1:53682/callback")]
    [TestCase("http://127.0.0.1/callback")]
    public async Task Check_LoopbackRedirect_IgnoresThePort(string redirect)
    {
        // Act
        var result = await _host.OAuth.CheckAuthorizationAsync(Server, Parameters(_client.ClientId, _challenge, redirect), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task Check_LoopbackRedirect_StillNeedsTheSamePathAndHost()
    {
        // Act
        var otherPath = await _host.OAuth.CheckAuthorizationAsync(
            Server, Parameters(_client.ClientId, _challenge, "http://127.0.0.1:9/other"), CancellationToken.None);
        var otherHost = await _host.OAuth.CheckAuthorizationAsync(
            Server, Parameters(_client.ClientId, _challenge, "http://localhost:5000/callback"), CancellationToken.None);

        // Assert
        Assert.That(otherPath.Succeeded, Is.False);
        Assert.That(otherHost.Succeeded, Is.False);
    }

    private static IEnumerable<TestCaseData> RedirectableFailures()
    {
        yield return new TestCaseData("token", "S256", "VALID", true, null, OAuthError.UnsupportedResponseType).SetName("response_type 不是 code");
        yield return new TestCaseData("code", "S256", null, true, null, OAuthError.InvalidRequest).SetName("沒有 code_challenge");
        yield return new TestCaseData("code", "S256", "short", true, null, OAuthError.InvalidRequest).SetName("code_challenge 太短");
        yield return new TestCaseData("code", "S256", new string('a', 129), true, null, OAuthError.InvalidRequest).SetName("code_challenge 太長");
        yield return new TestCaseData("code", "S256", new string('a', 42) + "+", true, null, OAuthError.InvalidRequest).SetName("code_challenge 不是 base64url");
        yield return new TestCaseData("code", "plain", "VALID", true, null, OAuthError.InvalidRequest).SetName("只收 S256");
        yield return new TestCaseData("code", null, "VALID", true, null, OAuthError.InvalidRequest).SetName("沒有 code_challenge_method");
        yield return new TestCaseData("code", "S256", "VALID", false, null, OAuthError.InvalidTarget).SetName("resource 不是這個伺服器的 MCP 端點");
        yield return new TestCaseData("code", "S256", "VALID", true, "nonsense other", OAuthError.InvalidScope).SetName("範圍全部不認得");
    }

    [TestCaseSource(nameof(RedirectableFailures))]
    public async Task Check_FailureAfterTheRedirectIsVerified_RedirectsTheErrorBackWithStateAndIssuer(
        string? responseType, string? method, string? challenge, bool rightResource, string? scope, string expectedError)
    {
        // Arrange
        var parameters = new OAuthAuthorizeParameters(
            responseType,
            _client.ClientId,
            Loopback,
            scope,
            "abc 123",
            challenge == "VALID" ? _challenge : challenge,
            method,
            rightResource ? Server.Resource : "https://other.example.com/mcp");

        // Act
        var result = await _host.OAuth.CheckAuthorizationAsync(Server, parameters, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(expectedError));
        var redirect = new Uri(result.Failure!.RedirectTo!);
        Assert.That(redirect.GetLeftPart(UriPartial.Path), Is.EqualTo("http://127.0.0.1:5000/callback"));
        var query = QueryOf(result.Failure.RedirectTo!);
        Assert.That(query["error"], Is.EqualTo(expectedError));
        Assert.That(query["error_description"], Is.Not.Empty);
        Assert.That(query["state"], Is.EqualTo("abc 123"));
        Assert.That(query["iss"], Is.EqualTo(Server.Issuer));
    }

    [Test]
    public async Task Check_MissingResource_IsInvalidTarget()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge) with { Resource = null };

        // Act
        var result = await _host.OAuth.CheckAuthorizationAsync(Server, parameters, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidTarget));
    }

    [Test]
    public async Task Check_StateTooLong_IsInvalidRequestAndTheErrorLeavesStateOut()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge, state: new string('s', OAuthRules.MaxStateLength + 1));

        // Act
        var result = await _host.OAuth.CheckAuthorizationAsync(Server, parameters, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
        Assert.That(QueryOf(result.Failure!.RedirectTo!)["state"], Is.Null);
    }

    [Test]
    public async Task Approve_RedirectsWithTheCodeStateAndIssuer()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge, state: "xyz");

        // Act
        var result = await _host.OAuth.ApproveAsync(Server, _owner.ToActor(), parameters, AccessTokenScopes.Grantable, CancellationToken.None);

        // Assert
        var url = result.Value;
        Assert.That(url, Does.StartWith(Loopback + "?"));
        var query = QueryOf(url);
        Assert.That(query["code"], Has.Length.EqualTo(43));
        Assert.That(query["state"], Is.EqualTo("xyz"));
        Assert.That(query["iss"], Is.EqualTo(Server.Issuer));
    }

    [Test]
    public async Task Approve_RedirectThatAlreadyHasAQuery_AppendsToIt()
    {
        // Arrange
        var client = await _host.RegisterAsync("有查詢字串", "http://127.0.0.1:5000/cb?tenant=a");
        var parameters = Parameters(client.ClientId, _challenge, "http://127.0.0.1:5000/cb?tenant=a");

        // Act
        var result = await _host.OAuth.ApproveAsync(Server, _owner.ToActor(), parameters, AccessTokenScopes.Grantable, CancellationToken.None);

        // Assert
        var query = QueryOf(result.Value);
        Assert.That(result.Value, Does.StartWith("http://127.0.0.1:5000/cb?tenant=a&code="));
        Assert.That(query["tenant"], Is.EqualTo("a"));
        Assert.That(query["code"], Is.Not.Empty);
    }

    [Test]
    public async Task Approve_WithoutState_LeavesStateOut()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge, state: null);

        // Act
        var result = await _host.OAuth.ApproveAsync(Server, _owner.ToActor(), parameters, AccessTokenScopes.Grantable, CancellationToken.None);

        // Assert
        Assert.That(QueryOf(result.Value)["state"], Is.Null);
    }

    [Test]
    public async Task Approve_ChecksTheRequestAgain()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge) with { CodeChallengeMethod = "plain" };

        // Act
        var result = await _host.OAuth.ApproveAsync(Server, _owner.ToActor(), parameters, AccessTokenScopes.Grantable, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
        Assert.That(result.Failure?.RedirectTo, Is.Not.Null);
    }

    [Test]
    public async Task Approve_ScopesNarrowerThanRequested_AreRecordedOnTheGrant()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var client = await _host.RegisterAsync();
        var parameters = Parameters(client.ClientId, _challenge);

        // Act
        await _host.OAuth.ApproveAsync(Server, user.ToActor(), parameters, [AccessTokenScope.ItemsRead], CancellationToken.None);

        // Assert
        var grant = (await _host.OAuth.ListGrantsAsync(user.ToActor(), CancellationToken.None)).Single();
        Assert.That(grant.Scopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead }));
    }

    [Test]
    public async Task Approve_NoScopes_IsInvalidScopeAndCannotRedirect()
    {
        // Act
        var result = await _host.OAuth.ApproveAsync(
            Server, _owner.ToActor(), Parameters(_client.ClientId, _challenge), [], CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidScope));
        Assert.That(result.Failure?.RedirectTo, Is.Null);
    }

    [Test]
    public async Task Approve_ScopeBeyondTheRequest_IsInvalidScope()
    {
        // Arrange
        var parameters = Parameters(_client.ClientId, _challenge, scope: "items:read");

        // Act
        var result = await _host.OAuth.ApproveAsync(
            Server, _owner.ToActor(), parameters, [AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite], CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidScope));
        Assert.That(result.Failure?.RedirectTo, Is.Null);
    }

    [Test]
    public async Task Approve_TwiceForTheSameClient_UpdatesTheSingleGrant()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var client = await _host.RegisterAsync();
        await _host.OAuth.ApproveAsync(Server, user.ToActor(), Parameters(client.ClientId, _challenge), AccessTokenScopes.Grantable, CancellationToken.None);

        // Act
        await _host.OAuth.ApproveAsync(
            Server, user.ToActor(), Parameters(client.ClientId, _challenge), [AccessTokenScope.BlueprintRead], CancellationToken.None);

        // Assert
        var grants = await _host.OAuth.ListGrantsAsync(user.ToActor(), CancellationToken.None);
        Assert.That(grants, Has.Count.EqualTo(1));
        Assert.That(grants[0].Scopes, Is.EqualTo(new[] { AccessTokenScope.BlueprintRead }));
    }

    [Test]
    public async Task Approve_AtTheGrantLimit_IsAccessDeniedWithARedirect()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        for (var i = 0; i < OAuthRules.MaxGrantsPerAccount; i++)
        {
            var other = await _host.RegisterAsync($"代理 {i}");
            await _host.OAuth.ApproveAsync(Server, user.ToActor(), Parameters(other.ClientId, _challenge), AccessTokenScopes.Grantable, CancellationToken.None);
        }

        // Act
        var result = await _host.OAuth.ApproveAsync(
            Server, user.ToActor(), Parameters(_client.ClientId, _challenge), AccessTokenScopes.Grantable, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.AccessDenied));
        Assert.That(QueryOf(result.Failure!.RedirectTo!)["error"], Is.EqualTo(OAuthError.AccessDenied));
        Assert.That(await _host.OAuth.ListGrantsAsync(user.ToActor(), CancellationToken.None), Has.Count.EqualTo(OAuthRules.MaxGrantsPerAccount));
    }

    [Test]
    public async Task Approve_ForADisabledAccount_IsAccessDenied()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        await _host.DisableAccountAsync(user.AccountId);

        // Act
        var result = await _host.OAuth.ApproveAsync(
            Server, user.ToActor(), Parameters(_client.ClientId, _challenge), AccessTokenScopes.Grantable, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.AccessDenied));
    }

    [Test]
    public async Task Approve_RecordsWhenTheClientWasLastUsed()
    {
        // Arrange
        var client = await _host.RegisterAsync();

        // Act
        await _host.OAuth.ApproveAsync(Server, _owner.ToActor(), Parameters(client.ClientId, _challenge), AccessTokenScopes.Grantable, CancellationToken.None);

        // Assert
        await using var db = await _host.CreateDbContextAsync();
        var row = await db.OAuthClients.AsNoTracking().SingleAsync(c => c.ClientId == client.ClientId);
        Assert.That(row.LastUsedAt, Is.EqualTo(_time.GetUtcNow()));
    }

    [Test]
    public async Task Deny_RedirectsAccessDeniedWithStateAndIssuer()
    {
        // Act
        var result = await _host.OAuth.DenyAsync(Server, Parameters(_client.ClientId, _challenge, state: "s9"), CancellationToken.None);

        // Assert
        var query = QueryOf(result.Value);
        Assert.That(query["error"], Is.EqualTo(OAuthError.AccessDenied));
        Assert.That(query["state"], Is.EqualTo("s9"));
        Assert.That(query["iss"], Is.EqualTo(Server.Issuer));
        Assert.That(query["code"], Is.Null);
    }

    [Test]
    public async Task Deny_BadRedirect_CannotRedirect()
    {
        // Act
        var result = await _host.OAuth.DenyAsync(
            Server, Parameters(_client.ClientId, _challenge, "https://evil.example.org/cb"), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
        Assert.That(result.Failure?.RedirectTo, Is.Null);
    }
}
