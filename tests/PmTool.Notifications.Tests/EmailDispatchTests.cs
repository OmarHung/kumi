using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Notifications.Email;

namespace PmTool.Notifications.Tests;

/// <summary>背景寄送：什麼時候寄、寄給誰、不該寄的怎麼處理。</summary>
[TestFixture]
public sealed class EmailDispatchTests : EmailTestBase
{
    [Test]
    public async Task Dispatch_NotificationOlderThanTheDelay_SendsOneMailToTheRecipient()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        var mail = Transport.Sent.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(mail.ToAddress, Is.EqualTo(EmailOf(me)));
            Assert.That(mail.FromAddress, Is.EqualTo("team@example.com"));
            Assert.That(mail.FromName, Is.EqualTo("Example 公司"));
            Assert.That(mail.Subject, Is.EqualTo("Person 1 指派給你：WEB-1 範例項目"));
        }
    }

    [Test]
    public async Task Dispatch_Gmail_ConnectsToGmailWithTheAppPasswordWithoutSpaces()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();

        // Act
        await DispatchAsync();

        // Assert
        var connection = Transport.Connections.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(connection.Host, Is.EqualTo("smtp.gmail.com"));
            Assert.That(connection.Port, Is.EqualTo(587));
            Assert.That(connection.Security, Is.EqualTo(SmtpSecurity.StartTls));
            Assert.That(connection.Username, Is.EqualTo("team@example.com"));
            Assert.That(connection.Password, Is.EqualTo("abcdefghijklmnop"));
        }
    }

    [Test]
    public async Task Dispatch_Smtp_ConnectsToTheConfiguredHostAndSendsFromTheFromAddress()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync(SmtpInput());
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();

        // Act
        await DispatchAsync();

        // Assert
        var connection = Transport.Connections.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(connection.ToString(), Is.EqualTo("smtp.example.com:587"));
            Assert.That(connection.Username, Is.EqualTo("mailer"));
            Assert.That(connection.Password, Is.EqualTo("secret"));
            Assert.That(Transport.Sent.Single().FromAddress, Is.EqualTo("noreply@example.com"));
        }
    }

    [Test]
    public async Task Dispatch_NotificationYoungerThanTheDelay_WaitsAndSendsLater()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        Wait(EmailRules.Delay - TimeSpan.FromSeconds(1));

        // Act
        var early = await DispatchAsync();
        Wait(TimeSpan.FromSeconds(2));
        var later = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(early, Is.Zero);
            Assert.That(later, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Dispatch_ReadBeforeTheDelay_NeverSends()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        await Host.Inbox.MarkAllReadAsync(new Actor(Organization, me, false), CancellationToken.None);
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_SeveralNotificationsForOnePerson_SendsOneMailListingAllOfThem()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        Wait(EmailRules.Delay - TimeSpan.FromMinutes(1));
        await HandleAsync(item.Commented(other, "第二則"));
        await HandleAsync(item.Transitioned(other, "待辦", "進行中"));
        Wait(TimeSpan.FromMinutes(2));

        // Act
        var sent = await DispatchAsync();

        // Assert：最舊的那一則超過延遲，比較新的也一起寄。
        var mail = Transport.Sent.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(mail.Subject, Does.StartWith("3 則新通知：WEB-1 範例項目"));
            Assert.That(mail.TextBody, Does.Contain("指派給你").And.Contain("第二則").And.Contain("「待辦」改成「進行中」"));
        }
    }

    [Test]
    public async Task Dispatch_AfterSending_DoesNotSendTheSameNotificationsAgain()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        await DispatchAsync();

        // Act
        var again = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again, Is.Zero);
            Assert.That(Transport.Sent, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Dispatch_MoreThanTwentyNotifications_ListsTwentyAndSaysHowManyAreLeft()
    {
        // Arrange：一則指派加 25 則留言，共 26 則。
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        for (var i = 0; i < 25; i++)
        {
            await HandleAsync(item.Commented(other, $"留言 {i}"));
        }

        PassTheDelay();

        // Act
        await DispatchAsync();
        var again = await DispatchAsync();

        // Assert：寄出的和沒列出的都算處理完了，不會下一輪又寄一次。
        var mail = Transport.Sent.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Does.StartWith("26 則新通知"));
            Assert.That(mail.TextBody, Does.Contain("還有 6 則"));
            Assert.That(mail.TextBody, Does.Contain("留言 18").And.Not.Contain("留言 19"));
            Assert.That(again, Is.Zero);
        }
    }

    [Test]
    public async Task Dispatch_NotificationOlderThanMaxAge_IsNotSentEvenAfterTheSettingsAreFixed()
    {
        // Arrange：設定一直是錯的，過了一天才修好。
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        Transport.OnOpen = _ => new EmailDeliveryException(EmailFailureKind.Unreachable, "連不上");
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        await DispatchAsync();
        Wait(EmailRules.MaxAge + TimeSpan.FromMinutes(1));
        Transport.OnOpen = _ => null;

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_OrganizationHadNotEnabledEmail_MarksOldNotificationsHandledSoEnablingDoesNotSendThem()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        var whileOff = await DispatchAsync();
        await EnableEmailAsync();

        // Act
        var afterEnabling = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(whileOff, Is.Zero);
            Assert.That(afterEnabling, Is.Zero);
        }
    }

    [Test]
    public async Task Dispatch_OrganizationDisabledAfterwards_StopsSendingPendingNotifications()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        await EnableEmailAsync(GmailInput(enabled: false));

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_PersonTurnedOffEmail_DoesNotSendToThatPerson()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await Host.EmailPreferences.SetAsync(new Actor(Organization, me, false), false, CancellationToken.None);
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_PersonTurnedEmailBackOn_SendsAgain()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        var actor = new Actor(Organization, me, false);
        await Host.EmailPreferences.SetAsync(actor, false, CancellationToken.None);
        await Host.EmailPreferences.SetAsync(actor, true, CancellationToken.None);
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.EqualTo(1));
    }

    [TestCase("no-email")]
    [TestCase("disabled")]
    [TestCase("left")]
    public async Task Dispatch_RecipientCannotReceiveMail_DoesNotSend(string reason)
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        switch (reason)
        {
            case "no-email":
                Host.People.Add(Organization, me, "Person 0", null);
                break;
            case "disabled":
                Host.People.Add(Organization, me, "Person 0", "me@example.com", disabled: true);
                break;
            default:
                Host.People.Remove(Organization, me);
                break;
        }

        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_ItemTheRecipientCanNoLongerSee_DoesNotSend()
    {
        // Arrange：只看得到自己項目的人，項目的負責人換掉之後就看不到了。
        var (item, me, other, _, _) = NewItem();
        Host.Access.AllowOwnOnly(item.ProjectId, me);
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        Host.Access.HideItem(me, item.ItemId);
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_RecipientRemovedFromTheProject_DoesNotSend()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        Host.Access.Revoke(item.ProjectId, me);
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_OnlyTheVisibleNotificationsAreListed()
    {
        // Arrange：同一個人在兩個項目有通知，其中一個後來看不到了。
        var (visibleItem, me, other, _, _) = NewItem();
        var hiddenItem = new EventBuilder(Organization, visibleItem.ProjectId, Guid.CreateVersion7());
        Host.Access.AllowOwnOnly(visibleItem.ProjectId, me);
        await EnableEmailAsync();
        await HandleAsync(visibleItem.Created(other, assignee: me), hiddenItem.Created(other, assignee: me));
        Host.Access.HideItem(me, hiddenItem.ItemId);
        PassTheDelay();

        // Act
        await DispatchAsync();

        // Assert
        var mail = Transport.Sent.Single();

        // 只有一則，所以主旨是單則的寫法。
        Assert.That(mail.Subject, Does.StartWith("Person 1 指派給你"));
    }

    [Test]
    public async Task Dispatch_SeveralRecipientsInOneOrganization_ShareOneConnection()
    {
        // Arrange
        var (item, a, b, c, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(c, assignee: a, mentioned: b));
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(2));
            Assert.That(Transport.Connections, Has.Count.EqualTo(1));
            Assert.That(Transport.ClosedSessions, Is.EqualTo(1));
            Assert.That(Transport.Sent.Select(m => m.ToAddress), Is.EquivalentTo(new[] { EmailOf(a), EmailOf(b) }));
        }
    }

    [Test]
    public async Task Dispatch_NothingToSend_DoesNotConnectAtAll()
    {
        // Arrange
        await EnableEmailAsync();

        // Act
        var sent = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.Zero);
            Assert.That(Transport.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task Dispatch_ManyRecipients_SendsAtMostTheLimitPerRunAndTheRestNextRun()
    {
        // Arrange：55 個人各被提及。
        var (item, _, other, _, _) = NewItem();
        Guid[] crowd = [.. Enumerable.Range(0, EmailRules.MaxRecipientsPerRun + 5).Select(_ => Guid.CreateVersion7())];
        Host.Access.Allow(item.ProjectId, crowd);
        foreach (var person in crowd)
        {
            Host.People.Add(Organization, person, "Crowd", $"crowd-{person:N}@example.com");
        }

        await EnableEmailAsync();

        // 一個事件最多提及 20 個人，分幾個事件提。
        foreach (var group in crowd.Chunk(20))
        {
            await HandleAsync(item.Commented(other, "大家好", group));
        }

        PassTheDelay();

        // Act
        var first = await DispatchAsync();
        var second = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(EmailRules.MaxRecipientsPerRun));
            Assert.That(second, Is.EqualTo(5));
        }
    }

    [Test]
    public async Task Dispatch_ConcurrentDispatchers_OnlyOneSends()
    {
        // Arrange：第一個寄信寄到一半（停在假的傳送裡）。
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        Transport.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = DispatchAsync();
        await Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using var otherProcess = Host.CreateScope();

        // Act
        var second = await otherProcess.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(CancellationToken.None);
        Transport.Gate.SetResult();
        var firstResult = await first;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.Zero);
            Assert.That(firstResult, Is.EqualTo(1));
            Assert.That(Transport.Sent, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Dispatch_AfterAnotherDispatcherFinished_TheLockIsFreeAgain()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        await DispatchAsync();
        await HandleAsync(item.Commented(other, "又一則"));
        PassTheDelay();
        using var otherProcess = Host.CreateScope();

        // Act
        var sent = await otherProcess.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(CancellationToken.None);

        // Assert
        Assert.That(sent, Is.EqualTo(1));
    }
}
