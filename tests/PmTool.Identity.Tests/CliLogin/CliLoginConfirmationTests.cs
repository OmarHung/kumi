using PmTool.Abstractions;
using PmTool.Identity.Tests.OAuth;
using static PmTool.Identity.Tests.CliLogin.CliLoginTestSupport;

namespace PmTool.Identity.Tests.CliLogin;

/// <summary>六碼確認碼：算法的測試向量、比對的寫法、核准時缺少或不符就不建立登入碼。</summary>
[TestFixture]
public sealed class CliLoginConfirmationTests
{
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

    [TestCase("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "951123")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "941901")]
    [TestCase("abcdefghijklmnopqrstuvwxyz0123456789-_ABCDEFG", "342122")]
    public void ConfirmationCodeOf_KnownChallenges_GivesTheExpectedCode(string challenge, string expected) =>
        Assert.That(CliLoginRules.ConfirmationCodeOf(challenge), Is.EqualTo(expected));

    [TestCase("951123", true)]
    [TestCase("951 123", true)]
    [TestCase("  951123  ", true)]
    [TestCase("951-123", true)]
    [TestCase(" 9 5 1 - 1 2 3 ", true)]
    [TestCase("951124", false)]
    [TestCase("95112", false)]
    [TestCase("9511234", false)]
    [TestCase("", false)]
    [TestCase("abcdef", false)]
    [TestCase("９５１１２３", false)]
    public void ConfirmationCodeMatches_Input_IsComparedWithoutSpacesAndDashes(string input, bool expected) =>
        Assert.That(CliLoginRules.ConfirmationCodeMatches("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", input), Is.EqualTo(expected));

    [Test]
    public void ConfirmationCodeMatches_NullInput_DoesNotMatch() =>
        Assert.That(CliLoginRules.ConfirmationCodeMatches("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", null), Is.False);

    [Test]
    public async Task Approve_WithTheRightCodeEvenWithSpaces_Succeeds()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var challenge = OAuthTestSupport.NewPkce().Challenge;
        var code = CliLoginRules.ConfirmationCodeOf(challenge);
        var spaced = $"{code[..3]} {code[3..]}";

        // Act
        var result = await _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(challenge), Grant() with { ConfirmationCode = spaced }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("000000x")]
    public async Task Approve_MissingOrWrongCode_IsRejectedWithTheConfirmationPathAndNoCodeIsIssued(string? input)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (verifier, challenge) = OAuthTestSupport.NewPkce();

        // Act
        var result = await _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(challenge), Grant() with { ConfirmationCode = input }, CancellationToken.None);
        var withTheRightCode = await _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(challenge), Grant(challenge: challenge), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginConfirmationMismatch));
            Assert.That(result.Failure?.Details.Single().Path, Is.EqualTo("confirmationCode"));
            Assert.That(withTheRightCode.Succeeded, Is.True);
            Assert.That(verifier, Is.Not.Empty);
        }
    }

    [Test]
    public async Task Approve_ADifferentCode_IsRejected()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var challenge = OAuthTestSupport.NewPkce().Challenge;
        var other = ((int.Parse(CliLoginRules.ConfirmationCodeOf(challenge)) + 1) % 1_000_000).ToString("D6");

        // Act
        var result = await _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(challenge), Grant() with { ConfirmationCode = other }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginConfirmationMismatch));
    }

    [Test]
    public async Task Check_DoesNotRevealTheCode()
    {
        // Arrange
        var challenge = OAuthTestSupport.NewPkce().Challenge;

        // Act
        var result = await _host.CliLogin.CheckAsync(_owner.ToActor(), Parameters(challenge), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value.ToString(), Does.Not.Contain(CliLoginRules.ConfirmationCodeOf(challenge)));
    }
}
