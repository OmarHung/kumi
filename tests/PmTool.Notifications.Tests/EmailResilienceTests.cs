using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Notifications.Email;

namespace PmTool.Notifications.Tests;

/// <summary>一個收件人、一封信、一個組織的問題，不能卡住別人的信；以及時間的邊界與鎖的釋放。</summary>
[TestFixture]
public sealed class EmailResilienceTests : EmailTestBase
{
    private async Task<EmailDeliveryStatus> StatusAsync(Actor? admin = null) =>
        (await Host.EmailSettings.GetAsync(admin ?? Admin, CancellationToken.None)).Value.Status;

    /// <summary>a 與 b 各有一則指派通知，a 的比較舊，所以先寄 a。</summary>
    private async Task<(Guid A, Guid B)> TwoRecipientsAsync()
    {
        var (item, a, b, c, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(c, assignee: a));
        Wait(TimeSpan.FromSeconds(1));
        var second = new EventBuilder(Organization, item.ProjectId, Guid.CreateVersion7());
        await HandleAsync(second.Created(c, assignee: b));
        PassTheDelay();
        return (a, b);
    }

    [TestCase("a,b@example.com")]
    [TestCase("a b@example.com")]
    [TestCase("<a@example.com>")]
    public async Task Dispatch_RecipientAddressCannotBeParsed_IsSkippedAndTheOthersAreStillSent(string badAddress)
    {
        // Arrange
        var (a, b) = await TwoRecipientsAsync();
        Host.People.Add(Organization, a, "Person Bad", badAddress);

        // Act
        var sent = await DispatchAsync();
        var next = await DispatchAsync();

        // Assert：不會一直重試，也不算組織的失敗。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(Transport.Sent.Single().ToAddress, Is.EqualTo(EmailOf(b)));
            Assert.That(next, Is.Zero);
            Assert.That((await StatusAsync()).LastFailedAt, Is.Null);
            Assert.That(Host.Logs.Entries, Has.Some.Matches<CapturedLog>(log => log.Level == LogLevel.Warning && log.Message.Contains(a.ToString())));
        }
    }

    [Test]
    public async Task Dispatch_TransportSaysInvalidRecipient_IsSkippedLikeARefusal()
    {
        // Arrange
        var (a, b) = await TwoRecipientsAsync();
        var bad = EmailOf(a);
        Transport.OnSend = email => email.ToAddress == bad ? new EmailDeliveryException(EmailFailureKind.InvalidRecipient, "地址不對") : null;

        // Act
        var sent = await DispatchAsync();
        Transport.OnSend = _ => null;
        var next = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(next, Is.Zero);
            Assert.That(Transport.Sent.Select(m => m.ToAddress), Is.EqualTo(new[] { EmailOf(b) }));
        }
    }

    [Test]
    public async Task Dispatch_MessagePermanentlyRefusedAtData_SkipsOnlyThatRecipient()
    {
        // Arrange
        var (a, b) = await TwoRecipientsAsync();
        var refused = EmailOf(a);
        Transport.OnSend = email => email.ToAddress == refused
            ? new EmailDeliveryException(EmailFailureKind.MessageRejected, "對方主機拒收了這封信", "554 message too large")
            : null;

        // Act
        var sent = await DispatchAsync();
        Transport.OnSend = _ => null;
        var next = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(Transport.Sent.Single().ToAddress, Is.EqualTo(EmailOf(b)));
            Assert.That(next, Is.Zero, "永久拒收的不再重寄");
            Assert.That((await StatusAsync()).LastFailedAt, Is.Null);
            Assert.That(Transport.Connections, Has.Count.EqualTo(1), "沒有因此讓整個組織退避、重連");
        }
    }

    [Test]
    public async Task Dispatch_RecipientTemporarilyFailing_IsSkippedThisRoundAndRetriedNextRoundWithoutBackoff()
    {
        // Arrange：第一個人的信箱滿了（4xx）。
        var (a, b) = await TwoRecipientsAsync();
        var full = EmailOf(a);
        Transport.OnSend = email => email.ToAddress == full
            ? new EmailDeliveryException(EmailFailureKind.RecipientTemporarilyFailed, "暫時收不了", "452 mailbox full")
            : null;

        // Act
        var first = await DispatchAsync();
        var statusAfterFirst = await StatusAsync();
        Transport.OnSend = _ => null;
        var second = await DispatchAsync();

        // Assert：第二輪立刻就試，不必等組織的退避。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(statusAfterFirst.LastFailedAt, Is.Null);
            Assert.That(second, Is.EqualTo(1));
            Assert.That(Transport.Sent.Select(m => m.ToAddress), Is.EqualTo(new[] { EmailOf(b), EmailOf(a) }));
        }
    }

    [Test]
    public async Task Dispatch_RecipientKeepsFailingTemporarily_IsEventuallyDroppedByMaxAge()
    {
        // Arrange
        var (a, _) = await TwoRecipientsAsync();
        var full = EmailOf(a);
        Transport.OnSend = email => email.ToAddress == full
            ? new EmailDeliveryException(EmailFailureKind.RecipientTemporarilyFailed, "暫時收不了")
            : null;
        await DispatchAsync();
        Wait(EmailRules.MaxAge + TimeSpan.FromMinutes(1));
        Transport.OnSend = _ => null;

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Dispatch_UnexpectedErrorInOneOrganization_RecordsAGenericFailureAndTheOtherOrganizationStillSends()
    {
        // Arrange
        var failing = Organization;
        var other = Guid.CreateVersion7();
        var otherAdmin = new Actor(other, Guid.CreateVersion7(), true);
        var (item, me, actor, _, _) = NewItem(failing);
        var (otherItem, otherMe, otherActor, _, _) = NewItem(other);
        await EnableEmailAsync();
        Assert.That((await Host.EmailSettings.SaveAsync(otherAdmin, GmailInput(), CancellationToken.None)).Succeeded, Is.True);
        await HandleAsync(item.Created(actor, assignee: me), otherItem.Created(otherActor, assignee: otherMe));
        PassTheDelay();
        Host.OrganizationClock.OnZone = org => org == failing ? new InvalidOperationException("時區服務壞了") : null;

        // Act
        var sent = await DispatchAsync();
        var status = await StatusAsync();
        Host.OrganizationClock.OnZone = _ => null;
        var immediately = await DispatchAsync();
        Wait(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var afterBackoff = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(Transport.Sent.First().ToAddress, Is.EqualTo(Host.People.EmailOf(other, otherMe)));
            Assert.That(status.LastError, Is.EqualTo("寄送時發生內部錯誤，下一輪再試。"));
            Assert.That(status.LastFailedAt, Is.EqualTo(Clock.GetUtcNow() - TimeSpan.FromMinutes(1) - TimeSpan.FromSeconds(1)));
            Assert.That(immediately, Is.Zero, "和連線失敗一樣要退避");
            Assert.That(afterBackoff, Is.EqualTo(1));
            Assert.That(Host.Logs.Entries, Has.Some.Matches<CapturedLog>(log => log.Level == LogLevel.Error && log.Exception?.Message == "時區服務壞了"));
        }
    }

    [Test]
    public async Task Dispatch_CancelledWhileSending_ReleasesTheLockSoAnotherProcessCanTakeOver()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        Transport.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var first = Host.Dispatcher.DispatchAsync(cancellation.Token);
        await Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Act
        await cancellation.CancelAsync();
        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        Transport.Gate = null;
        using var otherProcess = Host.CreateScope();
        var sent = await otherProcess.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(CancellationToken.None);

        // Assert：鎖放掉了，沒寄完的信由下一個接手。
        Assert.That(sent, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispatch_ExactlyTheDelayOld_IsSentButOneMicrosecondEarlierIsNot()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        Wait(EmailRules.Delay - TimeSpan.FromTicks(10));

        // Act
        var early = await DispatchAsync();
        Wait(TimeSpan.FromTicks(10));
        var exact = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(early, Is.Zero);
            Assert.That(exact, Is.EqualTo(1));
        }
    }

    [TestCase(0, 1)]
    [TestCase(10, 0)]
    public async Task Dispatch_AroundMaxAge_ExactlyMaxAgeIsStillSentOneMicrosecondOverIsNot(long extraTicks, int expected)
    {
        // Arrange：資料庫的時間精度是微秒，10 個 tick。
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        Wait(EmailRules.MaxAge + TimeSpan.FromTicks(extraTicks));

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.EqualTo(expected));
    }

    [Test]
    public async Task Save_TurningEmailOnFirstTime_DropsNotificationsCreatedWhileItWasOff()
    {
        // Arrange：通知在沒開寄信時產生，背景工作還沒跑過就開了。
        var (item, me, other, _, _) = NewItem();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        await EnableEmailAsync();

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public async Task Save_TurningEmailOnAgainAfterOff_DropsNotificationsFromTheOffPeriodButKeepsNewOnes()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await EnableEmailAsync(GmailInput(enabled: false));
        await HandleAsync(item.Created(other, assignee: me));
        await EnableEmailAsync();
        await HandleAsync(item.Commented(other, "開了之後的留言"));
        PassTheDelay();

        // Act
        await DispatchAsync();

        // Assert
        var mail = Transport.Sent.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Does.StartWith("Person 1 留言"));
            Assert.That(mail.TextBody, Does.Not.Contain("指派給你"));
        }
    }

    [Test]
    public async Task Save_WhileAlreadyEnabled_DoesNotDropPendingNotifications()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        await EnableEmailAsync(GmailInput() with { FromName = "新名稱" });
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.EqualTo(1));
    }
}
