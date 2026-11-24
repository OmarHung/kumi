namespace PmTool.Notifications.Email;

/// <summary>從設定組出實際要連的主機與寄件人。Gmail 的主機、連接埠、加密方式固定，寄件人一定是 Gmail 地址（Gmail 會改掉別的寄件人）。</summary>
internal static class EmailSenderIdentity
{
    public static SmtpConnection ConnectionOf(
        EmailProvider provider, string? host, int? port, SmtpSecurity? security, string? username, string? password) =>
        provider == EmailProvider.Gmail
            ? new SmtpConnection(EmailRules.GmailHost, EmailRules.GmailPort, SmtpSecurity.StartTls, username, password)
            : new SmtpConnection(host!, port!.Value, security!.Value, username, password);

    public static string FromAddressOf(EmailProvider provider, string? username, string? fromAddress) =>
        provider == EmailProvider.Gmail ? username! : fromAddress!;
}
