using MailKit.Net.Smtp;
using PmTool.Http;
using PmTool.Notifications.Email;

namespace PmTool.Notifications.Tests;

/// <summary>MailKit 的失敗翻成給人看的說明：只有分類與狀態碼，對方主機回的原文留在記錄裡。</summary>
[TestFixture]
public sealed class MailKitFailureMessageTests
{
    private static readonly SmtpConnection Connection = new("smtp.example.com", 587, SmtpSecurity.StartTls, "mailer", "secret");

    [Test]
    public void Translate_CommandRejectedWhileSending_DescribesTheCategoryAndStatusCodeOnly()
    {
        // Arrange
        var command = new SmtpCommandException(
            SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed, "554 5.7.1 blocked by gateway.corp.internal");

        // Act
        var failure = MailKitFailures.Translate(command, Connection, CancellationToken.None, sending: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Describe(500), Is.EqualTo("對方主機拒收了這封信（554）"));
            Assert.That(failure.ServerResponse, Does.Contain("gateway.corp.internal"));
        }
    }

    [Test]
    public void Translate_CommandRejectedWhileConnecting_DoesNotShowTheServersWords()
    {
        // Arrange
        var command = new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.MailboxUnavailable, "550 no relay for 10.9.9.9");

        // Act
        var failure = MailKitFailures.Translate(command, Connection, CancellationToken.None);

        // Assert
        Assert.That(failure.Describe(500), Is.EqualTo("對方主機拒絕了連線（550）"));
    }

    [Test]
    public void Translate_BlockedAddressWhenPrivateHostsAreNotAllowed_SaysTheSameAsAnyUnreachableHost()
    {
        // Act
        var blocked = MailKitFailures.Translate(new BlockedAddressException("smtp.example.com"), Connection, CancellationToken.None, privateHostsBlocked: true);
        var refused = MailKitFailures.Translate(
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused), Connection, CancellationToken.None, privateHostsBlocked: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocked.Kind, Is.EqualTo(EmailFailureKind.Unreachable));
            Assert.That(blocked.Describe(500), Is.EqualTo("連不上 smtp.example.com:587（請確認主機與連接埠；這個部署不連內網與本機的位址）"));
            Assert.That(blocked.Describe(500), Is.EqualTo(refused.Describe(500)));
        }
    }

    [Test]
    public void Translate_UnreachableWhenPrivateHostsAreAllowed_HasNoHint()
    {
        // Act
        var failure = MailKitFailures.Translate(
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused), Connection, CancellationToken.None);

        // Assert
        Assert.That(failure.Describe(500), Is.EqualTo("連不上 smtp.example.com:587"));
    }
}
