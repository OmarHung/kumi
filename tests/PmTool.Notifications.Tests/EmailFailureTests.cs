using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Notifications.Email;

namespace PmTool.Notifications.Tests;

/// <summary>寄不出去的處理：只略過被拒收的人、連線失敗的退避、成功之後歸零、錯誤原因的樣子。</summary>
[TestFixture]
public sealed class EmailFailureTests : EmailTestBase
{
    private static readonly TimeSpan[] Waits =
        [.. new[] { 1, 2, 5, 10, 30, 30 }.Select(minutes => TimeSpan.FromMinutes(minutes))];

    private static EmailDeliveryException Unreachable() => new(EmailFailureKind.Unreachable, "連不上 smtp.gmail.com:587");

    private async Task<(Scene Scene, Guid Me)> OneNotificationAsync()
    {
        var scene = NewItem();
        await EnableEmailAsync();
        await HandleAsync(scene.Item.Created(scene.People[1], assignee: scene.People[0]));
        PassTheDelay();
        return (scene, scene.People[0]);
    }

    private async Task<EmailDeliveryStatus> StatusAsync() => (await Host.EmailSettings.GetAsync(Admin, CancellationToken.None)).Value.Status;

    [Test]
    public async Task Dispatch_RecipientRejected_SkipsOnlyThatPersonAndSendsTheOthers()
    {
        // Arrange
        var (item, a, b, c, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(c, assignee: a, mentioned: b));
        PassTheDelay();
        var rejected = EmailOf(a);
        Transport.OnSend = email => email.ToAddress == rejected
            ? new EmailDeliveryException(EmailFailureKind.RecipientRejected, "對方拒收這個收件人", "550 no such user")
            : null;

        // Act
        var sent = await DispatchAsync();
        Transport.OnSend = _ => null;
        var next = await DispatchAsync();

        // Assert：被拒收的人只記一筆警告，不算組織的失敗，也不會一直重寄。
        var status = await StatusAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(Transport.Sent.Select(m => m.ToAddress), Is.EqualTo(new[] { EmailOf(b) }));
            Assert.That(status.LastFailedAt, Is.Null);
            Assert.That(status.LastSentAt, Is.EqualTo(Clock.GetUtcNow()));
            Assert.That(next, Is.Zero);
            Assert.That(Host.Logs.Entries, Has.Some.Matches<CapturedLog>(log => log.Level == LogLevel.Warning && log.Message.Contains(a.ToString())));
        }
    }

    [Test]
    public async Task Dispatch_ConnectionFails_RecordsTheReasonAndKeepsTheNotificationsWaiting()
    {
        // Arrange
        await OneNotificationAsync();
        Transport.OnOpen = _ => Unreachable();

        // Act
        var sent = await DispatchAsync();
        var status = await StatusAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.Zero);
            Assert.That(status.LastFailedAt, Is.EqualTo(Clock.GetUtcNow()));
            Assert.That(status.LastError, Is.EqualTo("連不上 smtp.gmail.com:587"));
            Assert.That(status.LastSentAt, Is.Null);
        }
    }

    [Test]
    public async Task Dispatch_ConnectionKeepsFailing_BacksOffOneTwoFiveTenThenThirtyMinutes()
    {
        // Arrange
        await OneNotificationAsync();
        Transport.OnOpen = _ => Unreachable();
        await DispatchAsync();
        var attempts = new List<int> { Transport.Connections.Count };

        // Act & Assert：每次失敗之後，沒到等待時間就不再試，到了才試一次。
        foreach (var wait in Waits)
        {
            Wait(wait - TimeSpan.FromSeconds(1));
            await DispatchAsync();
            Assert.That(Transport.Connections, Has.Count.EqualTo(attempts[^1]), $"等待 {wait} 還沒到就試了");
            Wait(TimeSpan.FromSeconds(2));
            await DispatchAsync();
            attempts.Add(Transport.Connections.Count);
            Assert.That(attempts[^1], Is.EqualTo(attempts[^2] + 1), $"等待 {wait} 到了卻沒有試");
        }
    }

    [Test]
    public async Task Dispatch_SucceedsAfterFailures_ResetsTheBackoff()
    {
        // Arrange：連續失敗三次，等待時間已經拉長到 5 分鐘。
        var (scene, _) = await OneNotificationAsync();
        Transport.OnOpen = _ => Unreachable();
        await DispatchAsync();
        Wait(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        await DispatchAsync();
        Wait(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        await DispatchAsync();
        Wait(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Transport.OnOpen = _ => null;
        var recovered = await DispatchAsync();

        // 再來一則通知、又失敗：等待時間從頭算，是 1 分鐘。
        await HandleAsync(scene.Item.Commented(scene.People[1], "又一則"));
        PassTheDelay();
        Transport.OnOpen = _ => Unreachable();
        await DispatchAsync();
        var attemptsAfterFailure = Transport.Connections.Count;
        Wait(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        // Act
        await DispatchAsync();

        // Assert
        var status = await StatusAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recovered, Is.EqualTo(1));
            Assert.That(Transport.Connections, Has.Count.EqualTo(attemptsAfterFailure + 1));
            Assert.That(status.LastSentAt, Is.Not.Null);
            Assert.That(status.LastFailedAt, Is.GreaterThan(status.LastSentAt!.Value));
        }
    }

    [Test]
    public async Task Dispatch_AfterSuccess_KeepsTheLastFailureForTheStatusToCompare()
    {
        // Arrange
        await OneNotificationAsync();
        Transport.OnOpen = _ => Unreachable();
        await DispatchAsync();
        var failedAt = Clock.GetUtcNow();
        Wait(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        Transport.OnOpen = _ => null;

        // Act
        await DispatchAsync();

        // Assert：網頁比較兩個時間，決定顯示失敗還是成功。
        var status = await StatusAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.LastFailedAt, Is.EqualTo(failedAt));
            Assert.That(status.LastError, Is.Not.Null);
            Assert.That(status.LastSentAt, Is.GreaterThan(failedAt));
        }
    }

    [Test]
    public async Task Save_AfterFailures_TriesAgainRightAwayWithTheNewSettings()
    {
        // Arrange
        await OneNotificationAsync();
        Transport.OnOpen = _ => Unreachable();
        await DispatchAsync();
        Transport.OnOpen = _ => null;
        await EnableEmailAsync(GmailInput());

        // Act：還在等待時間裡，但設定剛改過。
        var sent = await DispatchAsync();

        // Assert
        Assert.That(sent, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispatch_FailsMidwayThroughTheOrganization_StopsAndKeepsTheRestWaiting()
    {
        // Arrange
        var (item, a, b, c, _) = NewItem();
        await EnableEmailAsync();
        await HandleAsync(item.Created(c, assignee: a, mentioned: b));
        PassTheDelay();
        var calls = 0;
        Transport.OnSend = _ => ++calls == 1 ? null : new EmailDeliveryException(EmailFailureKind.Timeout, "逾時");

        // Act
        var sent = await DispatchAsync();
        Transport.OnSend = _ => null;
        Wait(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var retried = await DispatchAsync();

        // Assert：第二個人的通知沒有被當成寄出了。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(retried, Is.EqualTo(1));
            Assert.That(Transport.Sent.Select(m => m.ToAddress).Distinct().Count(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Dispatch_OneOrganizationFailing_DoesNotStopTheOthers()
    {
        // Arrange：第一個組織用 SMTP 連不上，第二個用 Gmail 寄得出去。
        var (item, me, other, _, _) = NewItem();
        var secondOrganization = Guid.CreateVersion7();
        var (secondItem, secondMe, secondOther, _, _) = NewItem(secondOrganization);
        await EnableEmailAsync(SmtpInput());
        var secondAdmin = new Actor(secondOrganization, Guid.CreateVersion7(), true);
        Assert.That((await Host.EmailSettings.SaveAsync(secondAdmin, GmailInput(), CancellationToken.None)).Succeeded, Is.True);
        Transport.OnOpen = connection => connection.Host == "smtp.example.com" ? Unreachable() : null;
        await HandleAsync(item.Created(other, assignee: me), secondItem.Created(secondOther, assignee: secondMe));
        PassTheDelay();

        // Act
        var sent = await DispatchAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(1));
            Assert.That(Transport.Sent.Single().ToAddress, Is.EqualTo(Host.People.EmailOf(secondOrganization, secondMe)));
            Assert.That((await StatusAsync()).LastFailedAt, Is.Not.Null);
            Assert.That((await Host.EmailSettings.GetAsync(secondAdmin, CancellationToken.None)).Value.Status.LastFailedAt, Is.Null);
        }
    }

    [Test]
    public async Task Dispatch_ServerResponseWithControlCharacters_IsStoredCleanAndShort()
    {
        // Arrange
        await OneNotificationAsync();
        var response = "535 5.7.8 Bad credentials\r\n" + new string('x', 2000);
        Transport.OnOpen = _ => new EmailDeliveryException(
            EmailFailureKind.AuthenticationFailed, "帳號或密碼不對（Gmail 要用應用程式密碼，不是登入密碼）", response);

        // Act
        await DispatchAsync();

        // Assert
        var error = (await StatusAsync()).LastError!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Does.StartWith("帳號或密碼不對（Gmail 要用應用程式密碼，不是登入密碼）（535 5.7.8 Bad credentials"));
            Assert.That(error.Length, Is.LessThanOrEqualTo(500));
            Assert.That(error, Does.Not.Contain('\r').And.Not.Contain('\n'));
        }
    }

    [Test]
    public async Task Dispatch_StoredPasswordCannotBeDecrypted_RecordsThatAndDoesNotConnect()
    {
        // Arrange：金鑰不見了（資料庫換過），存的密碼解不開。
        await OneNotificationAsync();
        await using (var db = await Host.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE notifications.email_settings SET protected_password = 'not-a-protected-payload'");
        }

        // Act
        var sent = await DispatchAsync();

        // Assert
        var status = await StatusAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.Zero);
            Assert.That(Transport.Connections, Is.Empty);
            Assert.That(status.LastError, Is.EqualTo("存的密碼解不開，請重新輸入"));
            Assert.That(status.LastFailedAt, Is.EqualTo(Clock.GetUtcNow()));
        }
    }

    [Test]
    public async Task Dispatch_AnyFailure_NeverPutsThePasswordInLogsOrStatus()
    {
        // Arrange
        await OneNotificationAsync();
        Transport.OnOpen = connection => new EmailDeliveryException(EmailFailureKind.AuthenticationFailed, $"登入 {connection} 失敗");

        // Act
        await DispatchAsync();

        // Assert
        var status = await StatusAsync();
        var everything = string.Join('\n', Host.Logs.Entries.Select(log => log.Message + log.Exception)) + status.LastError;
        Assert.That(everything, Does.Not.Contain("abcdefghijklmnop"));
    }
}
