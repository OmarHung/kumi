using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Notifications.Tests;

/// <summary>帳號通知的信：讀了也照寄、不等延遲、不和項目的通知併在一起，但照樣尊重本人關掉 Email 通知。</summary>
[TestFixture]
public sealed class AccountNoticeEmailTests : EmailTestBase
{
    private IAccountEventSink Sink => Host.Services.GetServices<IAccountEventSink>().Single();

    private Task PublishAsync(Guid person, Guid? eventId = null) => Sink.PublishAsync(
        new AccountEvent(eventId ?? Guid.CreateVersion7(), Organization, person, AccountEventKind.AccessTokenCreated, Clock.GetUtcNow())
        {
            Tokens = [new AccountEventToken("CI", [AccessTokenScope.ItemsRead], null)],
            SourceAddress = "203.0.113.5",
        },
        CancellationToken.None);

    private Guid NewPerson()
    {
        var person = Guid.CreateVersion7();
        Host.People.Add(Organization, person, "Me", $"me-{person:N}@example.com");
        return person;
    }

    private Task MarkAllReadAsync(Guid person) =>
        Host.Inbox.MarkAllReadAsync(new Actor(Organization, person, false), CancellationToken.None);

    [Test]
    public async Task Dispatch_AccountNotice_IsSentWithoutWaitingTheDelayAndLinksToTheIntegrationsPage()
    {
        // Arrange
        var me = NewPerson();
        await EnableEmailAsync();
        await PublishAsync(me);

        // Act
        var sent = await DispatchAsync();

        // Assert
        var mail = Transport.Sent.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(mail.ToAddress, Is.EqualTo(EmailOf(me)));
            Assert.That(mail.Subject, Is.EqualTo("Kumi：你的帳號建立了存取權杖"));
            Assert.That(mail.TextBody, Does.Contain($"{SiteUrl}/account/integrations"));
        }
    }

    [Test]
    public async Task Dispatch_LocalAgentMachineAdded_SaysWhichComputerAndFromWhere()
    {
        // Arrange
        var me = NewPerson();
        await EnableEmailAsync();
        await Sink.PublishAsync(
            new AccountEvent(Guid.CreateVersion7(), Organization, me, AccountEventKind.LocalAgentMachineAdded, Clock.GetUtcNow())
            {
                MachineName = "辦公室的 Mac",
                SourceAddress = "203.0.113.5",
            },
            CancellationToken.None);

        // Act
        await DispatchAsync();

        // Assert
        var mail = Transport.Sent.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Is.EqualTo("Kumi：你的本機代理出現在新的電腦"));
            Assert.That(mail.TextBody, Does.Contain("辦公室的 Mac").And.Contain("203.0.113.5"));
        }
    }

    [Test]
    public async Task Dispatch_AccountNoticeAlreadyRead_IsStillSent()
    {
        // Arrange
        var me = NewPerson();
        await EnableEmailAsync();
        await PublishAsync(me);
        await MarkAllReadAsync(me);
        PassTheDelay();

        // Act
        await DispatchAsync();

        // Assert
        Assert.That(Transport.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Dispatch_AccountNotice_IsSentOnlyOnce()
    {
        // Arrange
        var me = NewPerson();
        await EnableEmailAsync();
        await PublishAsync(me);

        // Act
        await DispatchAsync();
        PassTheDelay();
        await DispatchAsync();

        // Assert
        Assert.That(Transport.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Dispatch_PersonTurnedOffEmailNotifications_GetsNoAccountMailEither()
    {
        // Arrange
        var me = NewPerson();
        await EnableEmailAsync();
        await Host.EmailPreferences.SetAsync(new Actor(Organization, me, false), false, CancellationToken.None);
        await PublishAsync(me);

        // Act
        await DispatchAsync();
        await EmailPreferencesBackOnAsync(me);
        await DispatchAsync();

        // Assert
        Assert.That(Transport.Sent, Is.Empty);
    }

    private Task EmailPreferencesBackOnAsync(Guid me) =>
        Host.EmailPreferences.SetAsync(new Actor(Organization, me, false), true, CancellationToken.None);

    [Test]
    public async Task Dispatch_OrganizationWithoutEmail_SendsNothing()
    {
        // Arrange
        var me = NewPerson();
        await PublishAsync(me);

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_AccountNoticeAndItemNotification_AreSeparateMails()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        Host.People.Add(Organization, me, "Me", $"me-{me:N}@example.com");
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        await PublishAsync(me);
        PassTheDelay();

        // Act
        await DispatchAsync();

        // Assert
        var subjects = Transport.SentTo($"me-{me:N}@example.com").Select(m => m.Subject).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subjects, Has.Count.EqualTo(2));
            Assert.That(subjects, Does.Contain("Kumi：你的帳號建立了存取權杖"));
            Assert.That(subjects.Single(s => s != "Kumi：你的帳號建立了存取權杖"), Does.Contain("WEB-1"));
        }
    }

    [Test]
    public async Task Dispatch_ReadItemNotification_IsStillNotSent()
    {
        // Arrange：項目通知「讀了就不寄」的規則不變。
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        await MarkAllReadAsync(me);
        PassTheDelay();

        // Act
        await DispatchAsync();

        // Assert
        Assert.That(Transport.Sent, Is.Empty);
    }

    [Test]
    public async Task Dispatch_ItemNotificationWithinTheDelay_IsStillWaiting()
    {
        // Arrange：帳號通知不等延遲，項目的通知照舊要等。
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }
}
