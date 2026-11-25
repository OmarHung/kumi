using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>權杖的建立與撤銷：記下建立的位址、發帳號事件、寫稽核紀錄。</summary>
[TestFixture]
public sealed class AccessTokenAuditTests
{
    private static readonly AccessTokenScope[] Read = [AccessTokenScope.ItemsRead];

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

    [Test]
    public async Task Create_RecordsTheAddressOfTheRequestAndListsIt()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        _host.Request.Set("203.0.113.7");

        // Act
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("來源", Read, 30), CancellationToken.None)).Value;
        var listed = (await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None)).Single(t => t.TokenId == created.Token.TokenId);

        // Assert
        Assert.That(created.Token.CreatedFrom, Is.EqualTo("203.0.113.7"));
        Assert.That(listed.CreatedFrom, Is.EqualTo("203.0.113.7"));
    }

    [Test]
    public async Task Create_WithoutARequest_LeavesTheAddressEmpty()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("沒有請求", Read, 30), CancellationToken.None)).Value;

        // Assert
        Assert.That(created.Token.CreatedFrom, Is.Null);
    }

    [Test]
    public async Task Create_PublishesOneEventWithoutThePlaintext()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        _host.Request.Set("203.0.113.8");

        // Act
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("事件", Read, 30), CancellationToken.None)).Value;

        // Assert
        var published = _host.Events.Received.Where(e => e.PersonId == user.PersonId).ToList();
        var accountEvent = published.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accountEvent.Kind, Is.EqualTo(AccountEventKind.AccessTokenCreated));
            Assert.That(accountEvent.OrganizationId, Is.EqualTo(user.OrganizationId));
            Assert.That(accountEvent.SourceAddress, Is.EqualTo("203.0.113.8"));
            Assert.That(accountEvent.Tokens.Single().Name, Is.EqualTo("事件"));
            Assert.That(accountEvent.Tokens.Single().Scopes, Is.EqualTo(Read));
            Assert.That(accountEvent.Tokens.Single().ExpiresAt, Is.EqualTo(created.Token.ExpiresAt));
            Assert.That(accountEvent.ToString(), Does.Not.Contain(created.Secret));
        }
    }

    [Test]
    public async Task Create_EventIdIsTheAuditEntryId()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        _host.Request.Set("203.0.113.9", "kumi-test");

        // Act
        await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("稽核", [AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead], 30), CancellationToken.None);

        // Assert
        var eventId = _host.Events.Received.Single(e => e.PersonId == user.PersonId).EventId;
        var entry = (await ListAuditAsync(user.PersonId)).Single(e => e.Action == AuditActions.AccessTokenCreated);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.EventId, Is.EqualTo(eventId));
            Assert.That(entry.Outcome, Is.EqualTo(AuditOutcomes.Succeeded));
            Assert.That(entry.ActorPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entry.TargetPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.9"));
            Assert.That(entry.Details["tokenName"], Is.EqualTo("稽核"));
            Assert.That(entry.Details["scopes"], Is.EqualTo("items:read blueprint:read"));
            Assert.That(entry.Details["via"], Is.EqualTo("web"));
            Assert.That(entry.Details, Contains.Key("expiresAt"));
        }
    }

    [Test]
    public async Task Create_RejectedInput_PublishesNothingAndWritesNoAudit()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("壞的", Read, null), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.False);
        Assert.That(_host.Events.Received.Where(e => e.PersonId == user.PersonId), Is.Empty);
        Assert.That(await ListAuditAsync(user.PersonId), Has.None.Matches<AuditEntry>(e => e.Action == AuditActions.AccessTokenCreated));
    }

    [Test]
    public async Task Create_SinkThrows_TheTokenIsStillCreated()
    {
        // Arrange
        await using var host = await IdentityTestHost.StartAsync(services => services.AddSingleton<IAccountEventSink, ThrowingSink>());
        var owner = await host.StartWithOwnerAsync();
        var user = await host.AddUserAsync(owner.ToActor());

        // Act
        var result = await host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("丟例外", Read, 30), CancellationToken.None);
        var found = result.Succeeded ? await host.AccessTokens.FindAsync(result.Value.Secret, CancellationToken.None) : null;

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
        Assert.That(found?.TokenId, Is.EqualTo(result.Value.Token.TokenId));
    }

    [Test]
    public async Task Revoke_WritesAnAuditEntry()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("要撤銷", Read, 30), CancellationToken.None)).Value;
        _host.Request.Set("203.0.113.10");

        // Act
        var revoked = await _host.AccessTokens.RevokeAsync(user.ToActor(), created.Token.TokenId, CancellationToken.None);

        // Assert
        Assert.That(revoked.Succeeded, Is.True);
        var entry = (await ListAuditAsync(user.PersonId)).Single(e => e.Action == AuditActions.AccessTokenRevoked);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ActorPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.10"));
            Assert.That(entry.Details["tokenName"], Is.EqualTo("要撤銷"));
        }
    }

    [Test]
    public async Task Revoke_UnknownToken_WritesNoAuditEntry()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.AccessTokens.RevokeAsync(user.ToActor(), Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.False);
        Assert.That(await ListAuditAsync(user.PersonId), Has.None.Matches<AuditEntry>(e => e.Action == AuditActions.AccessTokenRevoked));
    }

    private async Task<IReadOnlyList<AuditEntry>> ListAuditAsync(Guid personId)
    {
        var page = await _host.Audit.ListAsync(_owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit, PersonId: personId), CancellationToken.None);
        Assert.That(page.Succeeded, Is.True, page.Failure?.Message);
        return page.Value.Items;
    }

    private sealed class ThrowingSink : IAccountEventSink
    {
        public Task PublishAsync(AccountEvent accountEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("通知壞了");
    }
}
