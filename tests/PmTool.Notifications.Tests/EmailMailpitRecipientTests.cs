using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using PmTool.Notifications.Email;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 對只收特定收件人的 Mailpit（其他的 RCPT 回 5xx）：被拒收的人不能讓同一條連線上後面的信寄不出去。
/// </summary>
[TestFixture]
public sealed class EmailMailpitRecipientTests
{
    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private IContainer? _mailpit;
    private HttpClient? _api;

    private IContainer Mailpit => _mailpit ?? throw new InvalidOperationException("Mailpit 還沒啟動。");

    private SmtpConnection Connection => new(Mailpit.Hostname, Mailpit.GetMappedPublicPort(SmtpPort), SmtpSecurity.None, null, null);

    [OneTimeSetUp]
    public async Task StartMailpitAsync()
    {
        _mailpit = new ContainerBuilder("axllent/mailpit:v1.29")
            .WithPortBinding(SmtpPort, true)
            .WithPortBinding(ApiPort, true)
            .WithEnvironment("MP_SMTP_ALLOWED_RECIPIENTS", @"^ok-.*@example\.com$")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiPort).ForPath("/api/v1/info")))
            .Build();
        await _mailpit.StartAsync();
        _api = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(ApiPort)}") };
    }

    [OneTimeTearDown]
    public async Task StopMailpitAsync()
    {
        _api?.Dispose();
        if (_mailpit is not null)
        {
            await _mailpit.DisposeAsync();
        }
    }

    private static OutgoingEmail Mail(string to) => EmailComposer.Test("noreply@example.com", null, to, null);

    private async Task<bool> ArrivedAsync(string recipient)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var response = await _api!.GetAsync($"/api/v1/search?query={Uri.EscapeDataString($"to:{recipient}")}");
            var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            if (list.GetProperty("messages").GetArrayLength() > 0)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    [Test]
    public async Task SendAsync_RecipientRefusedWith5xx_ThrowsRecipientRejectedAndTheSameSessionStillDeliversTheNext()
    {
        // Arrange
        var transport = new MailKitEmailTransport(allowPrivateHosts: true);
        var refused = $"nobody-{Guid.NewGuid():N}@example.com";
        var accepted = $"ok-{Guid.NewGuid():N}@example.com";
        await using var session = await transport.OpenAsync(Connection, CancellationToken.None);

        // Act
        var failure = Assert.ThrowsAsync<EmailDeliveryException>(async () => await session.SendAsync(Mail(refused), CancellationToken.None));
        await session.SendAsync(Mail(accepted), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Kind, Is.EqualTo(EmailFailureKind.RecipientRejected));
            Assert.That(await ArrivedAsync(accepted), Is.True, "被拒收的人後面的信要寄得出去");
            Assert.That(await ArrivedAsync(refused), Is.False);
        }
    }

    [Test]
    public async Task SendAsync_SeveralRefusalsInARow_NeverBreakTheSession()
    {
        // Arrange
        var transport = new MailKitEmailTransport(allowPrivateHosts: true);
        var accepted = $"ok-{Guid.NewGuid():N}@example.com";
        await using var session = await transport.OpenAsync(Connection, CancellationToken.None);

        // Act
        for (var i = 0; i < 3; i++)
        {
            var failure = Assert.ThrowsAsync<EmailDeliveryException>(
                async () => await session.SendAsync(Mail($"nobody-{i}@example.com"), CancellationToken.None));
            Assert.That(failure!.Kind, Is.EqualTo(EmailFailureKind.RecipientRejected));
        }

        await session.SendAsync(Mail(accepted), CancellationToken.None);

        // Assert
        Assert.That(await ArrivedAsync(accepted), Is.True);
    }

    [TestCase("a,b@example.com")]
    [TestCase("not an address")]
    public async Task SendAsync_AddressTheLibraryCannotParse_ThrowsInvalidRecipientAndTheSessionStaysUsable(string address)
    {
        // Arrange
        var transport = new MailKitEmailTransport(allowPrivateHosts: true);
        var accepted = $"ok-{Guid.NewGuid():N}@example.com";
        await using var session = await transport.OpenAsync(Connection, CancellationToken.None);

        // Act
        var failure = Assert.ThrowsAsync<EmailDeliveryException>(async () => await session.SendAsync(Mail(address), CancellationToken.None));
        await session.SendAsync(Mail(accepted), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Kind, Is.EqualTo(EmailFailureKind.InvalidRecipient));
            Assert.That(await ArrivedAsync(accepted), Is.True);
        }
    }

    [Test]
    public async Task Dispatch_FirstRecipientRefusedBy5xx_SecondRecipientOnTheSameRunStillReceivesAndNothingIsRecordedAsFailure()
    {
        // Arrange：被拒收的人通知比較舊，所以同一輪先寄他，再寄收得到的人。
        var clock = new ManualClock();
        var organization = Guid.CreateVersion7();
        var item = new EventBuilder(organization, Guid.CreateVersion7(), Guid.CreateVersion7());
        var second = new EventBuilder(organization, item.ProjectId, Guid.CreateVersion7());
        Guid refused = Guid.CreateVersion7(), accepted = Guid.CreateVersion7(), actor = Guid.CreateVersion7();
        var acceptedAddress = $"ok-{accepted:N}@example.com";
        await using var host = await NotificationsTestHost.StartAsync(
            new PmTool.Abstractions.DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), null, clock, null, useRealEmailTransport: true, allowPrivateEmailHosts: true);
        host.Access.Allow(item.ProjectId, refused, accepted, actor);
        host.People.Add(organization, refused, "Refused", $"nobody-{refused:N}@example.com");
        host.People.Add(organization, accepted, "Accepted", acceptedAddress);
        host.People.Add(organization, actor, "Actor", $"ok-{actor:N}@example.com");
        await using (var db = await host.CreateDbContextAsync())
        {
            db.EmailSettings.Add(new EmailSettingsRow
            {
                OrganizationId = organization,
                Enabled = true,
                Provider = EmailProvider.Smtp,
                Host = Mailpit.Hostname,
                Port = Mailpit.GetMappedPublicPort(SmtpPort),
                Security = SmtpSecurity.None,
                FromAddress = "noreply@example.com",
                SiteUrl = "https://pm.example.com",
                UpdatedAt = clock.GetUtcNow(),
                UpdatedBy = actor,
                ConsecutiveFailures = 0,
            });
            await db.SaveChangesAsync();
        }

        await host.Consumer.HandleAsync([item.Created(actor, assignee: refused) with { OccurredAt = clock.GetUtcNow() }], CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        await host.Consumer.HandleAsync([second.Created(actor, assignee: accepted) with { OccurredAt = clock.GetUtcNow() }], CancellationToken.None);
        clock.Advance(EmailRules.Delay + TimeSpan.FromSeconds(1));

        // Act
        var sent = await host.Dispatcher.DispatchAsync(CancellationToken.None);
        var next = await host.Dispatcher.DispatchAsync(CancellationToken.None);

        // Assert
        var admin = new PmTool.Abstractions.Actor(organization, actor, true);
        var status = (await host.EmailSettings.GetAsync(admin, CancellationToken.None)).Value.Status;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(await ArrivedAsync(acceptedAddress), Is.True);
            Assert.That(next, Is.Zero, "被拒收的人不再重寄");
            Assert.That(status.LastFailedAt, Is.Null);
            Assert.That(status.LastSentAt, Is.Not.Null);
        }
    }
}
