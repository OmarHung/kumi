using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Identity.OAuth;
using PmTool.Identity.Persistence;
using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>導回網站與用戶端網站不同的警告、client_id 的連接埠、註冊的每日上限與稽核。</summary>
[TestFixture]
public sealed class OAuthSmallItemsTests
{
    private const string ClientUrl = "https://app.example.com/oauth/client.json";

    private readonly ManualTimeProvider _time = new();
    private readonly string _challenge = NewPkce().Challenge;

    private static string Document(string clientId, params string[] redirects) =>
        $"{{\"client_id\":\"{clientId}\",\"client_name\":\"範例應用\",\"redirect_uris\":[{string.Join(",", redirects.Select(r => $"\"{r}\""))}]}}";

    [TestCase("https://app.example.com/cb", true)]
    [TestCase("https://auth.app.example.com/cb", true)]
    [TestCase("http://127.0.0.1:5000/cb", true)]
    [TestCase("http://localhost:5000/cb", true)]
    [TestCase("https://other.example.net/cb", false)]
    [TestCase("https://example.com/cb", true)]
    [TestCase("https://pp.example.com/cb", false)]
    [TestCase("https://evilapp.example.com/cb", false)]
    public void IsSameSite_HostEqualOrSubdomainOrLoopback_IsTheSameSite(string redirect, bool expected)
    {
        // Act
        var same = OAuthRedirects.IsSameSite(new Uri(redirect), "app.example.com");

        // Assert
        Assert.That(same, Is.EqualTo(expected));
    }

    [TestCase("https://app.example.com/cb", false)]
    [TestCase("https://other.example.net/cb", false)]
    [TestCase("http://127.0.0.1:5000/cb", true)]
    [TestCase("http://localhost:5000/cb", true)]
    [TestCase("http://[::1]:5000/cb", true)]
    public void IsSameSite_ClientWithoutADomain_OnlyALoopbackRedirectCounts(string redirect, bool expected)
    {
        // Act
        var same = OAuthRedirects.IsSameSite(new Uri(redirect), null);

        // Assert
        Assert.That(same, Is.EqualTo(expected));
    }

    [Test]
    public void CleanName_VariationSelectors_AreRemovedButTheEmojiSelectorIsKept()
    {
        // Act
        var cleaned = OAuthText.CleanName("範例\uFE00應\U000E0100用\u2764\uFE0F", 80);

        // Assert
        Assert.That(cleaned, Is.EqualTo("範例應用\u2764\uFE0F"));
    }

    [Test]
    public async Task Register_AtTheDailyLimitWithRegistrationsNeverUsed_DeletesTheOldestAndAcceptsTheNewOne()
    {
        // Arrange：額度滿了，但裡面有從沒用過的註冊；最舊的那一筆讓位
        await using var host = await OAuthTestSupport.StartAsync(_time);
        var start = _time.GetUtcNow();
        await InsertRegisteredAsync(host, OAuthRules.MaxRegistrationsPerDay, start, usedAt: null, spreadSeconds: 1);
        var oldest = await ClientIdAtAsync(host, start);

        // Act
        var registered = await host.OAuth.RegisterClientAsync(new OAuthClientRegistration("範例", [Loopback], "none", null, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registered.Succeeded, Is.True, registered.Failure?.Description);
            Assert.That(await CountRegisteredAsync(host), Is.EqualTo(OAuthRules.MaxRegistrationsPerDay));
            Assert.That(await ExistsAsync(host, oldest), Is.False);
            Assert.That(await ExistsAsync(host, registered.Value.ClientId), Is.True);
        }
    }

    [Test]
    public async Task Register_AtTheDailyLimitWithOnlyUsedRegistrations_KeepsThemAllAndIsUnavailable()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time);
        await InsertRegisteredAsync(host, OAuthRules.MaxRegistrationsPerDay, _time.GetUtcNow(), usedAt: _time.GetUtcNow(), spreadSeconds: 0);

        // Act
        var registered = await host.OAuth.RegisterClientAsync(new OAuthClientRegistration("範例", [Loopback], "none", null, null), CancellationToken.None);

        // Assert
        Assert.That(registered.Failure?.Error, Is.EqualTo(OAuthError.TemporarilyUnavailable));
        Assert.That(await CountRegisteredAsync(host), Is.EqualTo(OAuthRules.MaxRegistrationsPerDay));
    }

    [Test]
    public async Task Check_RedirectOnAnotherSite_IsFlaggedButStillAccepted()
    {
        // Arrange
        var handler = FakeMetadataHandler.Json(Document(ClientUrl, "https://other.example.net/cb", "https://app.example.com/cb"));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        var other = await host.OAuth.CheckAuthorizationAsync(Server, Parameters(ClientUrl, _challenge, "https://other.example.net/cb"), CancellationToken.None);
        var same = await host.OAuth.CheckAuthorizationAsync(Server, Parameters(ClientUrl, _challenge, "https://app.example.com/cb"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(other.Succeeded, Is.True, other.Failure?.Description);
            Assert.That(other.Value.RedirectMatchesClient, Is.False);
            Assert.That(other.Value.RedirectHost, Is.EqualTo("other.example.net"));
            Assert.That(other.Value.ClientHost, Is.EqualTo("app.example.com"));
            Assert.That(same.Value.RedirectMatchesClient, Is.True);
        }
    }

    [Test]
    public async Task Check_RegisteredClientToLoopback_IsTheSameSite()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time);
        var client = await host.RegisterAsync();

        // Act
        var result = await host.OAuth.CheckAuthorizationAsync(Server, Parameters(client.ClientId, _challenge), CancellationToken.None);

        // Assert
        Assert.That(result.Value.RedirectMatchesClient, Is.True);
    }

    [Test]
    public async Task Check_ClientIdWithAPort_IsRejected()
    {
        // Arrange
        const string withPort = "https://app.example.com:8443/oauth/client.json";
        var handler = FakeMetadataHandler.Json(Document(withPort, "https://app.example.com/cb"));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        var result = await host.OAuth.CheckAuthorizationAsync(Server, Parameters(withPort, _challenge, "https://app.example.com/cb"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
            Assert.That(handler.Calls, Is.Zero, "連都不連");
        }
    }

    [Test]
    public async Task Check_NonOkResponse_SaysTheSameThingWithoutTheStatusCode_AndLogsTheStatus()
    {
        // Arrange
        var log = new CapturingLogger<ClientMetadataFetcher>();
        var handler = new FakeMetadataHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        await using var host = await OAuthTestSupport.StartAsync(
            _time, handler, configure: services => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ILogger<ClientMetadataFetcher>>(services, log));

        // Act
        var result = await host.OAuth.CheckAuthorizationAsync(Server, Parameters(ClientUrl, _challenge, "https://app.example.com/cb"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
            Assert.That(result.Failure?.Description, Does.Not.Contain("404").And.Contain("200"));
            Assert.That(log.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message), Has.Some.Contain("404"));
        }
    }

    [Test]
    public async Task Register_AtTheDailyLimit_IsTemporarilyUnavailable_AndRecoversWhenOldOnesAgeOut()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time);
        await InsertRegisteredAsync(host, OAuthRules.MaxRegistrationsPerDay, _time.GetUtcNow());

        // Act
        var blocked = await host.OAuth.RegisterClientAsync(new OAuthClientRegistration("範例", [Loopback], "none", null, null), CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(25));
        var recovered = await host.OAuth.RegisterClientAsync(new OAuthClientRegistration("範例", [Loopback], "none", null, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocked.Failure?.Error, Is.EqualTo(OAuthError.TemporarilyUnavailable));
            Assert.That(recovered.Succeeded, Is.True, recovered.Failure?.Description);
        }
    }

    [Test]
    public async Task Register_RegistrationNeverUsedForAnAuthorization_IsDeletedAfter24Hours_ButAUsedOneIsKept()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time);
        var unused = await host.RegisterAsync("沒用過");
        var owner = await host.StartWithOwnerAsync();
        var used = await host.RegisterAsync("用過");
        await host.ApproveAsync(owner, used.ClientId);
        _time.Advance(TimeSpan.FromHours(25));

        // Act
        await host.RegisterAsync("新的");

        // Assert
        var unusedCheck = await host.OAuth.CheckAuthorizationAsync(Server, Parameters(unused.ClientId, _challenge), CancellationToken.None);
        var usedCheck = await host.OAuth.CheckAuthorizationAsync(Server, Parameters(used.ClientId, _challenge), CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unusedCheck.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
            Assert.That(usedCheck.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Approve_PublishesAnEventAndWritesAnAuditEntryWithTheSameId()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time);
        var owner = await host.StartWithOwnerAsync();
        var client = await host.RegisterAsync("代理甲");
        host.Request.Set("203.0.113.20");

        // Act
        await host.ApproveAsync(owner, client.ClientId, scopes: [AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead]);

        // Assert
        var accountEvent = host.Events.Received.Single(e => e.Kind == AccountEventKind.OAuthAppAuthorized);
        var page = await host.Audit.ListAsync(owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit, PersonId: owner.PersonId), CancellationToken.None);
        var entry = page.Value.Items.Single(e => e.Action == AuditActions.OAuthAuthorized);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accountEvent.PersonId, Is.EqualTo(owner.PersonId));
            Assert.That(accountEvent.ClientName, Is.EqualTo("代理甲"));
            Assert.That(accountEvent.ClientHost, Is.Null);
            Assert.That(accountEvent.SourceAddress, Is.EqualTo("203.0.113.20"));
            Assert.That(accountEvent.Scopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead }));
            Assert.That(entry.EventId, Is.EqualTo(accountEvent.EventId));
            Assert.That(entry.ActorPersonId, Is.EqualTo(owner.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.20"));
            Assert.That(entry.Details["client"], Is.EqualTo("代理甲"));
            Assert.That(entry.Details["scopes"], Is.EqualTo("items:read blueprint:read"));
        }
    }

    [Test]
    public async Task Approve_Rejected_PublishesNothingAndWritesNoAudit()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time);
        var owner = await host.StartWithOwnerAsync();
        var client = await host.RegisterAsync();

        // Act
        var result = await host.OAuth.ApproveAsync(Server, owner.ToActor(), Parameters(client.ClientId, _challenge), [], CancellationToken.None);

        // Assert
        var page = await host.Audit.ListAsync(owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit, PersonId: owner.PersonId), CancellationToken.None);
        Assert.That(result.Succeeded, Is.False);
        Assert.That(host.Events.Received.Where(e => e.Kind == AccountEventKind.OAuthAppAuthorized), Is.Empty);
        Assert.That(page.Value.Items.Where(e => e.Action == AuditActions.OAuthAuthorized), Is.Empty);
    }

    [Test]
    public async Task RevokeGrant_WritesAnAuditEntry()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time);
        var owner = await host.StartWithOwnerAsync();
        var client = await host.RegisterAsync("代理乙");
        await host.ApproveAsync(owner, client.ClientId);
        var grant = (await host.OAuth.ListGrantsAsync(owner.ToActor(), CancellationToken.None)).Single();
        host.Request.Set("203.0.113.21");

        // Act
        await host.OAuth.RevokeGrantAsync(owner.ToActor(), grant.GrantId, CancellationToken.None);

        // Assert
        var page = await host.Audit.ListAsync(owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit, PersonId: owner.PersonId), CancellationToken.None);
        var entry = page.Value.Items.Single(e => e.Action == AuditActions.OAuthGrantRevoked);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ActorPersonId, Is.EqualTo(owner.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.21"));
            Assert.That(entry.Details["client"], Is.EqualTo("代理乙"));
        }
    }

    private static Task InsertRegisteredAsync(IdentityTestHost host, int count, DateTimeOffset createdAt) =>
        InsertRegisteredAsync(host, count, createdAt, usedAt: createdAt, spreadSeconds: 0);

    /// <summary>灌入動態註冊的用戶端；第 i 筆的建立時刻是 <paramref name="createdAt"/> 加 i 乘 <paramref name="spreadSeconds"/> 秒。</summary>
    private static async Task InsertRegisteredAsync(
        IdentityTestHost host, int count, DateTimeOffset createdAt, DateTimeOffset? usedAt, int spreadSeconds)
    {
        await using var db = await host.CreateDbContextAsync();
        for (var i = 0; i < count; i++)
        {
            db.OAuthClients.Add(new OAuthClientRow
            {
                Id = Guid.CreateVersion7(),
                Kind = OAuthClientRow.RegisteredKind,
                ClientId = OAuthRules.RegisteredClientPrefix + Guid.NewGuid().ToString("N"),
                Name = "灌入",
                RedirectUris = [Loopback],
                CreatedAt = createdAt.AddSeconds(i * spreadSeconds),
                LastUsedAt = usedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<string> ClientIdAtAsync(IdentityTestHost host, DateTimeOffset createdAt)
    {
        await using var db = await host.CreateDbContextAsync();
        return await db.OAuthClients.AsNoTracking().Where(c => c.CreatedAt == createdAt).Select(c => c.ClientId).SingleAsync();
    }

    private static async Task<int> CountRegisteredAsync(IdentityTestHost host)
    {
        await using var db = await host.CreateDbContextAsync();
        return await db.OAuthClients.CountAsync(c => c.Kind == OAuthClientRow.RegisteredKind);
    }

    private static async Task<bool> ExistsAsync(IdentityTestHost host, string clientId)
    {
        await using var db = await host.CreateDbContextAsync();
        return await db.OAuthClients.AnyAsync(c => c.ClientId == clientId);
    }
}
