using PmTool.Abstractions;
using PmTool.Identity.Tests.OAuth;
using static PmTool.Identity.Tests.CliLogin.CliLoginTestSupport;

namespace PmTool.Identity.Tests.CliLogin;

/// <summary>電腦名稱不能帶著確認碼、碼重複兌換只在同一個來源才撤銷、確認碼不符要留稽核。</summary>
[TestFixture]
public sealed class CliLoginHardeningTests
{
    /// <summary>固定的 challenge，確認碼是 951123（見確認碼的測試向量）。</summary>
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

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
    public void ClearRequest() => _host.Request.Clear();

    // 阿拉伯-印度數字的 951123（U+0669 U+0665 U+0661 U+0661 U+0662 U+0663）
    private static readonly string ArabicIndicDigits = "٩٥١١٢٣";

    private static IEnumerable<TestCaseData> MachineNamesCarryingTheCode()
    {
        yield return new TestCaseData("確認碼 951 123").SetArgDisplayNames("電腦名稱寫出確認碼（有空白）");
        yield return new TestCaseData("951-123").SetArgDisplayNames("電腦名稱寫出確認碼（有連字號）");
        yield return new TestCaseData("９５１１２３").SetArgDisplayNames("電腦名稱用全形數字");
        yield return new TestCaseData("a9b5c1d1e2f3").SetArgDisplayNames("電腦名稱把數字夾在別的字裡");
        yield return new TestCaseData(ArabicIndicDigits).SetArgDisplayNames("電腦名稱用阿拉伯-印度數字");
        yield return new TestCaseData("Workstation 95 and 1123").SetArgDisplayNames("電腦名稱把數字拆在不同的詞裡");
        yield return new TestCaseData("MacBook 951123 Pro").SetArgDisplayNames("電腦名稱中間有確認碼");
    }

    [TestCaseSource(nameof(MachineNamesCarryingTheCode))]
    public async Task Check_MachineNameContainingTheConfirmationCode_IsInvalidWithTheMachinePath(string machine)
    {
        // Act
        var result = await _host.CliLogin.CheckAsync(_owner.ToActor(), Parameters(Challenge, machine), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginRequestInvalid));
            Assert.That(result.Failure?.Details.Select(d => d.Path), Does.Contain("machine"));
        }
    }

    [TestCaseSource(nameof(MachineNamesCarryingTheCode))]
    public async Task Approve_MachineNameContainingTheConfirmationCode_IsInvalidAndNoCodeIsIssued(string machine)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(Challenge, machine), Grant(challenge: Challenge), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginRequestInvalid));
            Assert.That(result.Failure?.Details.Select(d => d.Path), Does.Contain("machine"));
        }
    }

    [TestCase("MacBook-Pro-2024")]
    [TestCase("desk-12")]
    public async Task Check_MachineNameWithOtherDigits_IsAccepted(string machine)
    {
        // Act
        var result = await _host.CliLogin.CheckAsync(_owner.ToActor(), Parameters(Challenge, machine), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task Redeem_TheSameCodeFromAnotherOrigin_RevokesNothingAndPublishesNoEvent()
    {
        // Arrange：碼會留在瀏覽紀錄裡，別的位址拿到之後重放，不能因此撤掉本人剛換到的權杖
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;

        // Act
        var replayed = await _host.RedeemFromAsync(code, verifier, "198.51.100.77");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replayed.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
            Assert.That(await _host.AccessTokens.FindAsync(tokens.Cli!.Secret, CancellationToken.None), Is.Not.Null);
            Assert.That(await _host.AccessTokens.FindAsync(tokens.Agent!.Secret, CancellationToken.None), Is.Not.Null);
            Assert.That(_host.Events.Received.Where(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginCodeReused), Is.Empty);
        }
    }

    [Test]
    public async Task Redeem_TheSameCodeFromTheSameOriginAgain_StillRevokesTheTokens()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;

        // Act
        var again = await _host.RedeemAsync(code, verifier);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
            Assert.That(await _host.AccessTokens.FindAsync(tokens.Cli!.Secret, CancellationToken.None), Is.Null);
            Assert.That(_host.Events.Received.Count(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginCodeReused), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Approve_WrongConfirmationCode_WritesAFailedAuditEntryWithTheReason()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (_, challenge) = OAuthTestSupport.NewPkce();
        _host.Request.Set("203.0.113.50");

        // Act
        var result = await _host.CliLogin.ApproveAsync(
            user.ToActor(), Parameters(challenge), Grant() with { ConfirmationCode = "000000" }, CancellationToken.None);

        // Assert
        var page = await _host.Audit.ListAsync(_owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit, PersonId: user.PersonId), CancellationToken.None);
        var entry = page.Value.Items.Single(e => e.Action == AuditActions.CliLoginApproved);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginConfirmationMismatch));
            Assert.That(entry.Outcome, Is.EqualTo(AuditOutcomes.Failed));
            Assert.That(entry.ActorPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.50"));
            Assert.That(entry.Details["reason"], Is.EqualTo("confirmation_mismatch"));
        }
    }
}
