using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace PmTool.Notifications.Email;

/// <summary>
/// 用 MailKit 寄信。憑證一律驗證（不提供略過的選項）；選了 STARTTLS 而對方不支援時就失敗，不退回明文。
/// </summary>
internal sealed class MailKitEmailTransport(TimeSpan? operationTimeout = null) : IEmailTransport
{
    /// <summary>
    /// 連線加登入、寄一封信，各自的總時間上限。MailKit 自己的逾時只管讀寫；解析主機、連線、握手不一定算進去，
    /// 一個不回應的主機會讓這裡一直等，寄送的 advisory lock 也就一直被握著，所以用取消的期限包住整個動作。
    /// </summary>
    private readonly TimeSpan _operationTimeout = operationTimeout ?? EmailRules.SmtpTimeout * 3;

    public async Task<IEmailSession> OpenAsync(SmtpConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var client = new SmtpClient { Timeout = (int)Math.Min(EmailRules.SmtpTimeout.TotalMilliseconds, _operationTimeout.TotalMilliseconds) };
        try
        {
            using var limit = LimitedBy(cancellationToken);
            await client.ConnectAsync(connection.Host, connection.Port, SecureOptionsOf(connection.Security), limit.Token);
            if (connection.Username is not null || connection.Password is not null)
            {
                await client.AuthenticateAsync(connection.Username ?? string.Empty, connection.Password ?? string.Empty, limit.Token);
            }

            return new Session(client, connection, this);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw MailKitFailures.Translate(exception, connection, cancellationToken);
        }
    }

    private CancellationTokenSource LimitedBy(CancellationToken cancellationToken)
    {
        var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_operationTimeout);
        return limit;
    }

    private static SecureSocketOptions SecureOptionsOf(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.Tls => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.None,
    };

    private sealed class Session(SmtpClient client, SmtpConnection connection, MailKitEmailTransport owner) : IEmailSession
    {
        public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
        {
            try
            {
                using var message = Build(email);
                using var limit = owner.LimitedBy(cancellationToken);
                await client.SendAsync(message, limit.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                throw MailKitFailures.Translate(exception, connection, cancellationToken, sending: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (client.IsConnected)
                {
                    // 斷線時對方已經沒回應不算錯：信都寄完了。
                    await client.DisconnectAsync(quit: true, CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or ServiceNotConnectedException or ProtocolException)
            {
                // 同上，連線已經斷了。
            }
            finally
            {
                client.Dispose();
            }
        }

        private static MimeMessage Build(OutgoingEmail email)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(email.FromName ?? string.Empty, email.FromAddress));
            message.To.Add(MailboxAddress.Parse(email.ToAddress));
            message.Subject = email.Subject;

            // 自動產生的信：避免對方的自動回覆（休假通知、退信迴圈）回到我們的寄件人。
            message.Headers.Add("Auto-Submitted", "auto-generated");
            message.Headers.Add("X-Auto-Response-Suppress", "All");

            var body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody };
            message.Body = body.ToMessageBody();
            return message;
        }
    }
}
