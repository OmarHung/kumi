using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace PmTool.Notifications.Email;

/// <summary>把 MailKit 與網路層的例外歸成我們自己的分類。說明裡不放密碼。</summary>
internal static class MailKitFailures
{
    private const int FirstPermanentFailureCode = 500;

    /// <param name="sending">連線之後寄信的階段。對方在這個階段回的狀態碼，要分是這個收件人、這封信的問題，還是整台主機的問題。</param>
    public static EmailDeliveryException Translate(
        Exception exception, SmtpConnection connection, CancellationToken cancellationToken, bool sending = false)
    {
        var endpoint = $"{connection.Host}:{connection.Port}";
        return exception switch
        {
            EmailDeliveryException already => already,
            SmtpCommandException command => TranslateCommand(command, sending),
            AuthenticationException auth => new EmailDeliveryException(
                EmailFailureKind.AuthenticationFailed, AuthenticationMessage(connection), auth.Message, exception),
            SslHandshakeException => new EmailDeliveryException(
                EmailFailureKind.TlsFailed, $"和 {endpoint} 的加密談不成（憑證不被信任，或對方不支援這個加密方式）", null, exception),
            NotSupportedException => new EmailDeliveryException(
                EmailFailureKind.TlsFailed, $"{endpoint} 不支援所選的加密方式", null, exception),
            ParseException => new EmailDeliveryException(EmailFailureKind.InvalidRecipient, "收件人的地址格式寄不出去", null, exception),
            TimeoutException => new EmailDeliveryException(EmailFailureKind.Timeout, $"連到 {endpoint} 逾時", null, exception),
            OperationCanceledException when !cancellationToken.IsCancellationRequested =>
                new EmailDeliveryException(EmailFailureKind.Timeout, $"連到 {endpoint} 逾時", null, exception),
            SocketException or IOException or ServiceNotConnectedException or ProtocolException =>
                new EmailDeliveryException(EmailFailureKind.Unreachable, $"連不上 {endpoint}", null, exception),
            _ => new EmailDeliveryException(EmailFailureKind.Other, $"寄信時發生錯誤（{exception.GetType().Name}）", null, exception),
        };
    }

    private static EmailDeliveryException TranslateCommand(SmtpCommandException command, bool sending)
    {
        var permanent = (int)command.StatusCode >= FirstPermanentFailureCode;
        if (command.ErrorCode == SmtpErrorCode.SenderNotAccepted)
        {
            return new EmailDeliveryException(EmailFailureKind.SenderRejected, "對方拒收寄件人地址", command.Message, command);
        }

        if (!sending)
        {
            return new EmailDeliveryException(EmailFailureKind.Other, "對方主機拒絕了連線", command.Message, command);
        }

        // 連線、登入、MAIL FROM 都過了：之後的拒絕只和這個收件人、這封信有關。
        if (!permanent)
        {
            return new EmailDeliveryException(
                EmailFailureKind.RecipientTemporarilyFailed, "對方暫時收不了這個收件人的信", command.Message, command);
        }

        return command.ErrorCode == SmtpErrorCode.RecipientNotAccepted
            ? new EmailDeliveryException(EmailFailureKind.RecipientRejected, "對方拒收這個收件人", command.Message, command)
            : new EmailDeliveryException(EmailFailureKind.MessageRejected, "對方主機拒收了這封信", command.Message, command);
    }

    private static string AuthenticationMessage(SmtpConnection connection) =>
        connection.Host.Equals(EmailRules.GmailHost, StringComparison.OrdinalIgnoreCase)
            ? "帳號或密碼不對（Gmail 要用應用程式密碼，不是登入密碼）"
            : "帳號或密碼不對";
}
