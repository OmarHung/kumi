using PmTool.Abstractions;

namespace PmTool.Notifications.Tests;

/// <summary>一個組織一輪的寄送有時間上限：卡住的郵件主機不能拖住別的組織，也不能讓寄出去的信重寄。</summary>
[TestFixture]
public sealed class EmailDispatchBudgetTests : EmailTestBase
{
    protected override TimeSpan? EmailBudget => TimeSpan.FromMilliseconds(300);

    private async Task<EmailDeliveryStatus> StatusOfAsync(Actor admin) =>
        (await Host.EmailSettings.GetAsync(admin, CancellationToken.None)).Value.Status;

    [Test]
    public async Task Dispatch_SendNeverReturns_RecordsAFailureWithBackoffAndKeepsTheNotificationWaiting()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(other, assignee: me));
        PassTheDelay();
        Transport.HangOnSend = _ => true;

        // Act
        var sent = await DispatchAsync();

        // Assert
        var status = await StatusOfAsync(Admin);
        var failedAt = Clock.GetUtcNow();
        Transport.HangOnSend = _ => false;
        var tooSoon = await DispatchAsync();
        Wait(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var retried = await DispatchAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.Zero);
            Assert.That(status.LastError, Is.EqualTo("這一輪寄送超過 0.3 秒，下一輪再試"));
            Assert.That(status.LastFailedAt, Is.EqualTo(failedAt));
            Assert.That(tooSoon, Is.Zero, "還在退避的時間內不再試");
            Assert.That(retried, Is.EqualTo(1), "沒寄出去的通知下一輪照樣寄");
        }
    }

    [Test]
    public async Task Dispatch_OneOrganizationHangs_TheNextOrganizationStillGetsItsMail()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        var secondOrganization = Guid.CreateVersion7();
        var (secondItem, secondMe, secondOther, _, _) = NewItem(secondOrganization);
        await EnableEmailAsync(SmtpInput());
        var secondAdmin = new Actor(secondOrganization, Guid.CreateVersion7(), true);
        Assert.That((await Host.EmailSettings.SaveAsync(secondAdmin, GmailInput(), CancellationToken.None)).Succeeded, Is.True);
        await HandleAsync(item.Created(other, assignee: me), secondItem.Created(secondOther, assignee: secondMe));
        PassTheDelay();
        var stuck = Host.People.EmailOf(Organization, me);
        Transport.HangOnSend = email => email.ToAddress == stuck;

        // Act
        var sent = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(Transport.Sent.Single().ToAddress, Is.EqualTo(Host.People.EmailOf(secondOrganization, secondMe)));
            Assert.That((await StatusOfAsync(Admin)).LastFailedAt, Is.Not.Null);
            Assert.That((await StatusOfAsync(secondAdmin)).LastFailedAt, Is.Null);
        }
    }

    [Test]
    public async Task Dispatch_MailSentJustBeforeTheLimit_IsStillMarkedHandledAndNotSentAgain()
    {
        // Arrange：第一封寄得出去，第二個收件人卡住；已寄出的那封不能因為期限到了而重寄。
        var scene = NewItem();
        var (item, first, second, creator, _) = scene;
        await EnableEmailAsync();
        await HandleAsync(item.Created(creator, assignee: first, mentioned: second));
        PassTheDelay();
        var stuck = EmailOf(second);
        Transport.HangOnSend = email => email.ToAddress == stuck;

        // Act
        var firstRun = await DispatchAsync();
        Transport.HangOnSend = _ => false;
        Wait(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var secondRun = await DispatchAsync();

        // Assert
        var sentTo = Transport.Sent.Select(m => m.ToAddress).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstRun + secondRun, Is.EqualTo(2));
            Assert.That(sentTo.Count(address => address == EmailOf(first)), Is.EqualTo(1));
            Assert.That(sentTo.Count(address => address == EmailOf(second)), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Dispatch_TimeRunsOutAfterSomeMailWasSent_IsNotAFailureAndTheNextRoundContinuesWithoutBackoff()
    {
        // Arrange：第一封寄得出去，第二個收件人卡住到時間用完。
        var (item, first, second, creator, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(creator, assignee: first, mentioned: second));
        PassTheDelay();
        Transport.HangOnSend = _ => Transport.Sent.Count >= 1; // 先處理到的那位寄得出去，後面的卡住。

        // Act
        var firstRun = await DispatchAsync();
        var status = await StatusOfAsync(Admin);
        Transport.HangOnSend = _ => false;
        var secondRun = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstRun, Is.EqualTo(1));
            Assert.That(status.LastError, Is.Null);
            Assert.That(status.LastFailedAt, Is.Null);
            Assert.That(secondRun, Is.EqualTo(1), "沒有退避，下一輪馬上接著寄");
        }
    }
}
