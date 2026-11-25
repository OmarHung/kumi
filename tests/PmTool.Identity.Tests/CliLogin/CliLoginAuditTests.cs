using PmTool.Abstractions;
using static PmTool.Identity.Tests.CliLogin.CliLoginTestSupport;

namespace PmTool.Identity.Tests.CliLogin;

/// <summary>瀏覽器登入的來源位址、帳號事件、稽核紀錄，以及同一個碼再出現時撤銷它發出的權杖。</summary>
[TestFixture]
public sealed class CliLoginAuditTests
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

    [SetUp]
    public void ClearRequest() => _host.Request.Clear();

    [Test]
    public async Task Redeem_RecordsTheAddressOfTheRedeemingComputerOnBothTokens()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        _host.Request.Set("198.51.100.20");
        var (code, verifier) = await _host.ApproveAsync(user);
        _host.Request.Set("203.0.113.30");

        // Act
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;
        var listed = await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens.Cli!.Token.CreatedFrom, Is.EqualTo("203.0.113.30"));
            Assert.That(tokens.Agent!.Token.CreatedFrom, Is.EqualTo("203.0.113.30"));
            Assert.That(listed.Select(t => t.CreatedFrom), Is.All.EqualTo("203.0.113.30"));
        }
    }

    [Test]
    public async Task Redeem_PublishesOneEventWithBothTokensAndNoSecrets()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user, "Work-Laptop");
        _host.Request.Set("203.0.113.31");

        // Act
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;

        // Assert
        var accountEvent = _host.Events.Received.Single(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginTokensIssued);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accountEvent.Tokens.Select(t => t.Name), Is.EqualTo(new[] { "kumi（Work-Laptop）", "本機代理（Work-Laptop）" }));
            Assert.That(accountEvent.MachineName, Is.EqualTo("Work-Laptop"));
            Assert.That(accountEvent.SourceAddress, Is.EqualTo("203.0.113.31"));
            Assert.That(accountEvent.ToString(), Does.Not.Contain(tokens.Cli!.Secret).And.Not.Contain(tokens.Agent!.Secret).And.Not.Contain(code));
        }
    }

    [Test]
    public async Task Redeem_WritesApprovalAndRedemptionAuditEntriesWithMatchingEventId()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        _host.Request.Set("203.0.113.32");

        // Act
        var (code, verifier) = await _host.ApproveAsync(user, "Desk");
        await _host.RedeemAsync(code, verifier);

        // Assert
        var entries = await ListAuditAsync(user.PersonId);
        var approved = entries.Single(e => e.Action == AuditActions.CliLoginApproved);
        var redeemed = entries.Single(e => e.Action == AuditActions.CliLoginRedeemed);
        var eventId = _host.Events.Received.Single(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginTokensIssued).EventId;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(approved.Details["machine"], Is.EqualTo("Desk"));
            Assert.That(approved.Details["tokens"], Is.EqualTo("cli agent"));
            Assert.That(approved.Details["scopes"], Is.EqualTo("items:read items:write blueprint:read"));
            Assert.That(approved.ActorPersonId, Is.EqualTo(user.PersonId));
            Assert.That(redeemed.Outcome, Is.EqualTo(AuditOutcomes.Succeeded));
            Assert.That(redeemed.EventId, Is.EqualTo(eventId));
            Assert.That(redeemed.TargetPersonId, Is.EqualTo(user.PersonId));
            Assert.That(redeemed.SourceAddress, Is.EqualTo("203.0.113.32"));
            Assert.That(entries.SelectMany(e => e.Details.Values), Has.None.Contains(code).And.None.Contains(verifier));
        }
    }

    [TestCase("origin_mismatch")]
    [TestCase("verifier_mismatch")]
    public async Task Redeem_Failure_WritesAFailedAuditEntryWithTheReasonAndPublishesNothing(string reason)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);

        // Act
        var result = reason == "origin_mismatch"
            ? await _host.RedeemFromAsync(code, verifier, "198.51.100.9")
            : await _host.RedeemAsync(code, verifier + "x");

        // Assert
        var entry = (await ListAuditAsync(user.PersonId)).Single(e => e.Action == AuditActions.CliLoginRedeemed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
            Assert.That(entry.Outcome, Is.EqualTo(AuditOutcomes.Failed));
            Assert.That(entry.Details["reason"], Is.EqualTo(reason));
            Assert.That(_host.Events.Received.Where(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginTokensIssued), Is.Empty);
        }
    }

    [Test]
    public async Task Redeem_UnknownCode_WritesNoAuditEntry()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        await _host.RedeemAsync(new string('a', 43), new string('b', 43));

        // Assert
        Assert.That(await ListAuditAsync(user.PersonId), Has.None.Matches<AuditEntry>(e => e.Action == AuditActions.CliLoginRedeemed));
    }

    [Test]
    public async Task Redeem_TheSameCodeAgain_RevokesTheTokensItIssuedAndPublishesOneEvent()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user, "Reused");
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;
        await _host.AccessTokens.FindAsync(tokens.Cli!.Secret, CancellationToken.None);
        _host.Request.Set("198.51.100.21");

        // Act
        var again = await _host.RedeemAsync(code, verifier);

        // Assert
        var reused = _host.Events.Received.Where(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginCodeReused).ToList();
        var entry = (await ListAuditAsync(user.PersonId)).Single(e => e.Action == AuditActions.CliLoginCodeReused);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Code, Is.EqualTo(IdentityErrorCodes.CliLoginCodeInvalid));
            Assert.That(await _host.AccessTokens.FindAsync(tokens.Cli.Secret, CancellationToken.None), Is.Null);
            Assert.That(await _host.AccessTokens.FindAsync(tokens.Agent!.Secret, CancellationToken.None), Is.Null);
            Assert.That(await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None), Is.Empty);
            Assert.That(reused, Has.Count.EqualTo(1));
            Assert.That(reused[0].RevokedTokens, Is.EqualTo(2));
            Assert.That(reused[0].SourceAddress, Is.EqualTo("198.51.100.21"));
            Assert.That(reused[0].EventId, Is.EqualTo(entry.EventId));
            Assert.That(entry.Details["revokedTokens"], Is.EqualTo("2"));
        }
    }

    [Test]
    public async Task Redeem_TheSameCodeThreeTimes_DoesNothingTheThirdTime()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        await _host.RedeemAsync(code, verifier);
        await _host.RedeemAsync(code, verifier);

        // Act
        await _host.RedeemAsync(code, verifier);

        // Assert
        Assert.That(_host.Events.Received.Count(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginCodeReused), Is.EqualTo(1));
        Assert.That((await ListAuditAsync(user.PersonId)).Count(e => e.Action == AuditActions.CliLoginCodeReused), Is.EqualTo(1));
    }

    [Test]
    public async Task Redeem_TheSameCodeAfterAFailedRedemption_RevokesNothing()
    {
        // Arrange：第一次的 verifier 不對，沒發出任何權杖
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        await _host.RedeemAsync(code, verifier + "x");

        // Act
        await _host.RedeemAsync(code, verifier);

        // Assert
        Assert.That(_host.Events.Received.Where(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginCodeReused), Is.Empty);
    }

    [Test]
    public async Task Redeem_TheSameCodeWhenTheTokensWereAlreadyRevoked_PublishesNoEvent()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var (code, verifier) = await _host.ApproveAsync(user);
        var tokens = (await _host.RedeemAsync(code, verifier)).Value;
        await _host.AccessTokens.RevokeAsync(user.ToActor(), tokens.Cli!.Token.TokenId, CancellationToken.None);
        await _host.AccessTokens.RevokeAsync(user.ToActor(), tokens.Agent!.Token.TokenId, CancellationToken.None);

        // Act
        await _host.RedeemAsync(code, verifier);

        // Assert
        Assert.That(_host.Events.Received.Where(e => e.PersonId == user.PersonId && e.Kind == AccountEventKind.CliLoginCodeReused), Is.Empty);
    }

    private async Task<IReadOnlyList<AuditEntry>> ListAuditAsync(Guid personId)
    {
        var page = await _host.Audit.ListAsync(_owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit, PersonId: personId), CancellationToken.None);
        Assert.That(page.Succeeded, Is.True, page.Failure?.Message);
        return page.Value.Items;
    }
}
