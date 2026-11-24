using PmTool.Abstractions;
using PmTool.Identity.Tests.OAuth;
using static PmTool.Identity.Tests.CliLogin.CliLoginTestSupport;

namespace PmTool.Identity.Tests.CliLogin;

/// <summary>檢查與核准：請求的欄位、使用者在同意頁的決定、上限，以及核准時什麼都還沒建立。</summary>
[TestFixture]
public sealed class CliLoginRequestTests
{
    private static readonly string Challenge = OAuthTestSupport.NewPkce().Challenge;

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

    [Test]
    public async Task Check_ReturnsTheTrimmedMachineNameTheTokensAndTheScopes()
    {
        // Arrange
        var parameters = Parameters(Challenge, machine: "  MacBook-Pro  ");

        // Act
        var result = await _host.CliLogin.CheckAsync(_owner.ToActor(), parameters, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
            Assert.That(result.Value.MachineName, Is.EqualTo("MacBook-Pro"));
            Assert.That(result.Value.Tokens, Is.EqualTo(new[] { CliLoginTokenKind.Cli, CliLoginTokenKind.Agent }));
            Assert.That(result.Value.Scopes, Is.EqualTo(CliScopes));
        }
    }

    [Test]
    public async Task Check_OnlyAgent_AcceptsEmptyScopes()
    {
        // Act
        var result = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge, tokens: [CliLoginTokenKind.Agent]), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [TestCase(1023, "port")]
    [TestCase(65536, "port")]
    [TestCase(0, "port")]
    public async Task Check_PortOutOfRange_IsInvalidWithThePortPath(int port, string path)
    {
        // Arrange
        var parameters = Parameters(Challenge) with { Port = port };

        // Act
        var result = await _host.CliLogin.CheckAsync(_owner.ToActor(), parameters, CancellationToken.None);

        // Assert
        AssertInvalid(result, path);
    }

    [TestCase("")]
    [TestCase("short")]
    [TestCase("has spaces in it 123456")]
    [TestCase("not+base64url/chars=12345")]
    public async Task Check_StateThatIsNotBase64UrlOrTheRightLength_IsInvalid(string state)
    {
        // Act
        var result = await _host.CliLogin.CheckAsync(_owner.ToActor(), Parameters(Challenge) with { State = state }, CancellationToken.None);

        // Assert
        AssertInvalid(result, "state");
    }

    [Test]
    public async Task Check_StateLongerThanTheLimit_IsInvalid()
    {
        // Act
        var result = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge) with { State = new string('a', CliLoginRules.MaxStateLength + 1) }, CancellationToken.None);

        // Assert
        AssertInvalid(result, "state");
    }

    [TestCase("")]
    [TestCase("tooshort")]
    public async Task Check_BadChallenge_IsInvalid(string challenge)
    {
        // Act
        var result = await _host.CliLogin.CheckAsync(_owner.ToActor(), Parameters(challenge), CancellationToken.None);

        // Assert
        AssertInvalid(result, "challenge");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Mac\nBook")]
    [TestCase("Mac\0Book")]
    [TestCase("Mac‮Book")]
    [TestCase("Mac​Book")]
    public async Task Check_MachineNameThatIsEmptyOrHasControlOrInvisibleCharacters_IsInvalid(string machine)
    {
        // Act
        var result = await _host.CliLogin.CheckAsync(_owner.ToActor(), Parameters(Challenge, machine: machine), CancellationToken.None);

        // Assert
        AssertInvalid(result, "machine");
    }

    [Test]
    public async Task Check_MachineNameLongerThanTheLimit_IsInvalid_AndExactlyTheLimitIsFine()
    {
        // Act
        var tooLong = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge, machine: new string('m', CliLoginRules.MaxMachineNameLength + 1)), CancellationToken.None);
        var exact = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge, machine: new string('m', CliLoginRules.MaxMachineNameLength)), CancellationToken.None);

        // Assert
        AssertInvalid(tooLong, "machine");
        Assert.That(exact.Succeeded, Is.True);
    }

    [Test]
    public async Task Check_NoTokensOrDuplicatedTokens_IsInvalid()
    {
        // Act
        var none = await _host.CliLogin.CheckAsync(_owner.ToActor(), Parameters(Challenge, tokens: []), CancellationToken.None);
        var twice = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge, tokens: [CliLoginTokenKind.Cli, CliLoginTokenKind.Cli]), CancellationToken.None);

        // Assert
        AssertInvalid(none, "tokens");
        AssertInvalid(twice, "tokens");
    }

    [Test]
    public async Task Check_ScopeProblems_AreInvalidWithTheScopesPath()
    {
        // Act
        var empty = await _host.CliLogin.CheckAsync(_owner.ToActor(), Parameters(Challenge, scopes: []), CancellationToken.None);
        var duplicated = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge, scopes: [AccessTokenScope.ItemsRead, AccessTokenScope.ItemsRead]), CancellationToken.None);
        var agentRun = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge, scopes: [AccessTokenScope.ItemsRead, AccessTokenScope.AgentRun]), CancellationToken.None);
        var withoutCli = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge, tokens: [CliLoginTokenKind.Agent], scopes: [AccessTokenScope.ItemsRead]), CancellationToken.None);

        // Assert
        AssertInvalid(empty, "scopes");
        AssertInvalid(duplicated, "scopes");
        AssertInvalid(agentRun, "scopes");
        AssertInvalid(withoutCli, "scopes");
    }

    [Test]
    public async Task Check_SeveralProblems_AreAllListed()
    {
        // Act
        var result = await _host.CliLogin.CheckAsync(
            _owner.ToActor(), Parameters(Challenge, machine: "") with { Port = 1, State = "x" }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "port", "state", "machine" }));
    }

    [Test]
    public async Task Approve_ReturnsALoopbackRedirectWithTheCodeAndTheState()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.CliLogin.ApproveAsync(user.ToActor(), Parameters(Challenge), Grant(), CancellationToken.None);

        // Assert
        var uri = new Uri(result.Value.RedirectUri);
        var query = QueryOf(result.Value.RedirectUri);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uri.Scheme, Is.EqualTo("http"));
            Assert.That(uri.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(uri.Port, Is.EqualTo(Port));
            Assert.That(uri.AbsolutePath, Is.EqualTo(CliLoginRules.CallbackPath));
            Assert.That(query["state"], Is.EqualTo(State));
            Assert.That(query["code"], Has.Length.EqualTo(43));
        }
    }

    [Test]
    public async Task Approve_DoesNotCreateAnyTokenYet()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        await _host.ApproveAsync(user);

        // Assert
        Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task Approve_InvalidRequest_IsRejectedLikeCheck()
    {
        // Act
        var result = await _host.CliLogin.ApproveAsync(
            _owner.ToActor(), Parameters(Challenge) with { Port = 80 }, Grant(), CancellationToken.None);

        // Assert
        AssertInvalid(result, "port");
    }

    [Test]
    public async Task Approve_GrantScopesMayDifferFromTheRequestedOnes()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(Challenge), Grant(cliScopes: [AccessTokenScope.BlueprintWrite]), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task Approve_GrantScopeProblems_AreInvalidWithTheScopesPath()
    {
        // Act
        var empty = await _host.CliLogin.ApproveAsync(_owner.ToActor(), Parameters(Challenge), Grant(cliScopes: []), CancellationToken.None);
        var agentRun = await _host.CliLogin.ApproveAsync(
            _owner.ToActor(), Parameters(Challenge), Grant(cliScopes: [AccessTokenScope.AgentRun]), CancellationToken.None);
        var duplicated = await _host.CliLogin.ApproveAsync(
            _owner.ToActor(), Parameters(Challenge), Grant(cliScopes: [AccessTokenScope.ItemsRead, AccessTokenScope.ItemsRead]), CancellationToken.None);
        var withoutCli = await _host.CliLogin.ApproveAsync(
            _owner.ToActor(), Parameters(Challenge, tokens: [CliLoginTokenKind.Agent]), Grant(), CancellationToken.None);

        // Assert
        AssertInvalid(empty, "scopes");
        AssertInvalid(agentRun, "scopes");
        AssertInvalid(duplicated, "scopes");
        AssertInvalid(withoutCli, "scopes");
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(AccessTokenRules.MaxExpiresInDays + 1)]
    public async Task Approve_ExpiryOutOfRange_IsRejectedWithTheExpiryCode(int days)
    {
        // Act
        var result = await _host.CliLogin.ApproveAsync(
            _owner.ToActor(), Parameters(Challenge), Grant(expiresInDays: days), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenExpiryInvalid));
        Assert.That(result.Failure?.Details.Single().Path, Is.EqualTo("expiresInDays"));
    }

    [Test]
    public async Task Approve_OnlyAgentButThePersonalAiIsNotAllowed_IsForbidden()
    {
        // Act
        var result = await _host.CliLogin.ApproveAsync(
            _owner.ToActor(), Parameters(Challenge, tokens: [CliLoginTokenKind.Agent]), Grant(includeAgent: false, cliScopes: []), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginAgentNotAllowed));
        }
    }

    [Test]
    public async Task Approve_WhenTheAccountIsAlmostFull_StillApprovesBecauseRedemptionRevokesTheOldTokens()
    {
        // Arrange（已經有 19 把，再要兩把：兌換時會換掉舊的，不能在同意頁就擋掉重跑精靈的人）
        var user = await _host.AddUserAsync(_owner.ToActor());
        await FillTokensAsync(user, AccessTokenRules.MaxActiveTokensPerAccount - 1);

        // Act
        var two = await _host.CliLogin.ApproveAsync(user.ToActor(), Parameters(Challenge), Grant(), CancellationToken.None);

        // Assert
        Assert.That(two.Succeeded, Is.True, two.Failure?.Message);
    }

    private async Task FillTokensAsync(AuthenticatedUser user, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var created = await _host.AccessTokens.CreateAsync(
                user.ToActor(), new NewAccessToken($"填滿 {i}", [AccessTokenScope.ItemsRead], null), CancellationToken.None);
            Assert.That(created.Succeeded, Is.True, created.Failure?.Message);
        }
    }

    private static void AssertInvalid<T>(Result<T> result, string path)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginRequestInvalid));
            Assert.That(result.Failure?.Details.Select(d => d.Path), Does.Contain(path));
        }
    }
}
