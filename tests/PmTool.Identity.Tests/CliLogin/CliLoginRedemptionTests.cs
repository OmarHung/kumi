using PmTool.Abstractions;
using PmTool.Identity.Tests.OAuth;
using static PmTool.Identity.Tests.CliLogin.CliLoginTestSupport;

namespace PmTool.Identity.Tests.CliLogin;

/// <summary>兌換：建出來的權杖、一次性與 PKCE、替換這台電腦的舊權杖、上限與停用的帳號。</summary>
[TestFixture]
public sealed class CliLoginRedemptionTests
{
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
    public async Task Redeem_BothTokens_AreCreatedWithTheirNamesScopesAndMachineName()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user, "MacBook-Pro", grant: Grant(expiresInDays: 30));

        // Act
        var result = await _host.RedeemAsync(code, verifier);

        // Assert
        var tokens = result.Value;
        var listed = await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
            Assert.That(tokens.Account.Email, Is.EqualTo(user.Email));
            Assert.That(tokens.Account.DisplayName, Is.EqualTo(user.DisplayName));
            Assert.That(tokens.Account.OrganizationName, Is.EqualTo(user.OrganizationName));
            Assert.That(tokens.AgentSkipped, Is.False);
            Assert.That(tokens.Cli!.Secret, Does.StartWith(AccessTokenRules.Prefix));
            Assert.That(tokens.Cli.Token.Name, Is.EqualTo("kumi（MacBook-Pro）"));
            Assert.That(tokens.Cli.Token.Scopes, Is.EqualTo(CliScopes));
            Assert.That(tokens.Cli.Token.MachineName, Is.EqualTo("MacBook-Pro"));
            Assert.That(tokens.Cli.Token.ExpiresAt, Is.EqualTo(_time.GetUtcNow().AddDays(30)));
            Assert.That(tokens.Agent!.Token.Name, Is.EqualTo("本機代理（MacBook-Pro）"));
            Assert.That(tokens.Agent.Token.Scopes, Is.EqualTo(CliLoginRules.AgentScopes));
            Assert.That(tokens.Agent.Token.MachineName, Is.EqualTo("MacBook-Pro"));
            Assert.That(tokens.Agent.Secret, Is.Not.EqualTo(tokens.Cli.Secret));
            Assert.That(listed.Select(t => t.MachineName), Is.All.EqualTo("MacBook-Pro"));
            Assert.That(listed, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Redeem_TheSecretsWorkAsTokensWithTheRightScopes()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;
        var cli = await _host.AccessTokens.FindAsync(tokens.Cli!.Secret, CancellationToken.None);
        var agent = await _host.AccessTokens.FindAsync(tokens.Agent!.Secret, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cli?.Scopes, Is.EqualTo(CliScopes));
            Assert.That(cli?.User.PersonId, Is.EqualTo(user.PersonId));
            Assert.That(agent?.Scopes, Is.EqualTo(CliLoginRules.AgentScopes));
        }
    }

    [Test]
    public async Task Redeem_NoExpiry_NeverExpires()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user, grant: Grant(expiresInDays: null));

        // Act
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;

        // Assert
        Assert.That(tokens.Cli!.Token.ExpiresAt, Is.Null);
    }

    [Test]
    public async Task Redeem_OnlyCli_HasNoAgentTokenAndIsNotSkipped()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user, tokens: [CliLoginTokenKind.Cli]);

        // Act
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens.Cli, Is.Not.Null);
            Assert.That(tokens.Agent, Is.Null);
            Assert.That(tokens.AgentSkipped, Is.False);
        }
    }

    [Test]
    public async Task Redeem_OnlyAgent_HasNoCliToken()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user, tokens: [CliLoginTokenKind.Agent]);

        // Act
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens.Cli, Is.Null);
            Assert.That(tokens.Agent, Is.Not.Null);
        }
    }

    [Test]
    public async Task Redeem_AgentRequestedButTheOrganizationDoesNotAllow_SkipsItAndSaysSo()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user, grant: Grant(includeAgent: false));

        // Act
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens.Cli, Is.Not.Null);
            Assert.That(tokens.Agent, Is.Null);
            Assert.That(tokens.AgentSkipped, Is.True);
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Redeem_TheSameCodeTwice_FailsTheSecondTimeAndCreatesNothingMore()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        await _host.RedeemAsync(code, verifier);

        // Act
        var second = await _host.RedeemAsync(code, verifier);

        // Assert
        AssertCodeInvalid(second);
        Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Redeem_TwoAtTheSameTime_OnlyOneSucceeds()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _host.RedeemAsync(code, verifier)));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Redeem_AfterTheCodeExpired_Fails()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        _time.Advance(CliLoginRules.CodeLifetime + TimeSpan.FromSeconds(1));

        // Act
        var result = await _host.RedeemAsync(code, verifier);

        // Assert
        AssertCodeInvalid(result);
    }

    [Test]
    public async Task Redeem_JustBeforeTheCodeExpires_Works()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        _time.Advance(CliLoginRules.CodeLifetime - TimeSpan.FromSeconds(1));

        // Act
        var result = await _host.RedeemAsync(code, verifier);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task Redeem_WrongVerifier_FailsAndBurnsTheCodeSoTheRightOneNoLongerWorks()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        var wrong = OAuthTestSupport.NewPkce().Verifier;

        // Act
        var guessed = await _host.RedeemAsync(code, wrong);
        var right = await _host.RedeemAsync(code, verifier);

        // Assert
        AssertCodeInvalid(guessed);
        AssertCodeInvalid(right);
        Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
    }

    [TestCase("")]
    [TestCase("short")]
    public async Task Redeem_VerifierWithABadFormat_FailsAndBurnsTheCode(string badVerifier)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        var bad = await _host.RedeemAsync(code, badVerifier);
        var right = await _host.RedeemAsync(code, verifier);

        // Assert
        AssertCodeInvalid(bad);
        AssertCodeInvalid(right);
    }

    [TestCase("")]
    [TestCase("not-a-code")]
    [TestCase("pmt_not-a-code")]
    public async Task Redeem_UnknownOrMalformedCode_FailsWithTheSameError(string code)
    {
        // Act
        var result = await _host.RedeemAsync(code, OAuthTestSupport.NewPkce().Verifier);

        // Assert
        AssertCodeInvalid(result);
    }

    [Test]
    public async Task Redeem_AWellFormedButUnknownCode_FailsWithTheSameError()
    {
        // Act
        var result = await _host.RedeemAsync(new string('A', 43), OAuthTestSupport.NewPkce().Verifier);

        // Assert
        AssertCodeInvalid(result);
    }

    [Test]
    public async Task Redeem_WhenTheAccountWasDisabledAfterApproval_FailsAndCreatesNothing()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        await _host.DisableAccountAsync(user.AccountId);

        // Act
        var result = await _host.RedeemAsync(code, verifier);

        // Assert
        AssertCodeInvalid(result);
        Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task Redeem_ReplaceTokens_RevokesTheOwnOnesAndLeavesTheOthersAlone()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var other = await _host.AddUserAsync(_owner.ToActor());
        var mine = await CreateAsync(user, "舊的 kumi");
        var mineKept = await CreateAsync(user, "網頁上自己建的");
        var theirs = await CreateAsync(other, "別人的");
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        var result = await _host.RedeemAsync(code, verifier, mine.Secret, theirs.Secret, "pmt_not-hashable", "garbage");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
            Assert.That(await _host.AccessTokens.FindAsync(mine.Secret, CancellationToken.None), Is.Null);
            Assert.That(await _host.AccessTokens.FindAsync(mineKept.Secret, CancellationToken.None), Is.Not.Null);
            Assert.That(await _host.AccessTokens.FindAsync(theirs.Secret, CancellationToken.None), Is.Not.Null);
            Assert.That(await _host.AccessTokens.FindAsync(result.Value.Cli!.Secret, CancellationToken.None), Is.Not.Null);
        }
    }

    [Test]
    public async Task Redeem_ReplacingTheTokensFromTheLastLogin_KeepsTheListFromGrowing()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code1, verifier1) = await _host.ApproveAsync(user);
        var first = (await _host.RedeemAsync(code1, verifier1)).Value;
        var (code2, verifier2) = await _host.ApproveAsync(user);

        // Act
        await _host.RedeemAsync(code2, verifier2, first.Cli!.Secret, first.Agent!.Secret);

        // Assert
        var listed = await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Count(t => t.MachineName is not null && t.Name.StartsWith("kumi", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(listed.Count(t => t.MachineName is not null), Is.EqualTo(2));
            Assert.That(await _host.AccessTokens.FindAsync(first.Cli.Secret, CancellationToken.None), Is.Null);
            Assert.That(await _host.AccessTokens.FindAsync(first.Agent.Secret, CancellationToken.None), Is.Null);
        }
    }

    [Test]
    public async Task Redeem_ReplaceTokens_BeyondTheMaximumAreIgnored()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var tokens = new List<CreatedAccessToken>();
        for (var i = 0; i < CliLoginRules.MaxReplaceTokens + 1; i++)
        {
            tokens.Add(await CreateAsync(user, $"舊的 {i}"));
        }

        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        await _host.RedeemAsync(code, verifier, [.. tokens.Select(t => t.Secret)]);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(tokens[^1].Secret, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task Redeem_ReplaceTokens_DoNotTouchTokensIssuedByAnOAuthGrant()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (_, oauth) = await _host.ConnectAsync(user);
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        var result = await _host.RedeemAsync(code, verifier, oauth.AccessToken);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
        Assert.That(await _host.AccessTokens.FindAsync(oauth.AccessToken, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task Redeem_WhenTheTokensFilledUpAfterApproval_IsALimitConflictAndTheCodeIsStillBurned()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user, tokens: [CliLoginTokenKind.Cli]);
        await FillAsync(user, AccessTokenRules.MaxActiveTokensPerAccount);

        // Act
        var full = await _host.RedeemAsync(code, verifier);
        var again = await _host.RedeemAsync(code, verifier);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(full.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(full.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenLimitReached));
            AssertCodeInvalid(again);
        }
    }

    [Test]
    public async Task Redeem_WhenFullButTheReplacedTokenMakesRoom_Succeeds()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        await FillAsync(user, AccessTokenRules.MaxActiveTokensPerAccount - 1);
        var (code, verifier) = await _host.ApproveAsync(user, tokens: [CliLoginTokenKind.Cli]);
        var last = await CreateAsync(user, "第二十把");

        // Act
        var result = await _host.RedeemAsync(code, verifier, last.Secret);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public void Redemption_ToString_DoesNotShowTheSecrets()
    {
        // Act
        var text = new CliLoginRedemption("the-code", "the-verifier", ["pmt_old"]).ToString();

        // Assert
        Assert.That(text, Does.Not.Contain("the-code").And.Not.Contain("the-verifier").And.Not.Contain("pmt_old"));
    }

    private async Task<CreatedAccessToken> CreateAsync(AuthenticatedUser user, string name)
    {
        var created = await _host.AccessTokens.CreateAsync(
            user.ToActor(), new NewAccessToken(name, [AccessTokenScope.ItemsRead], null), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, created.Failure?.Message);
        return created.Value;
    }

    private async Task FillAsync(AuthenticatedUser user, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await CreateAsync(user, $"填滿 {i}");
        }
    }

    private static void AssertCodeInvalid(Result<CliLoginTokens> result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
        }
    }
}
