using PmTool.Notifications;

namespace PmTool.Notifications.Email;

/// <summary>連到哪一台 SMTP 主機、怎麼登入。Gmail 與其他 SMTP 在這裡已經是同一個形狀。</summary>
internal sealed record SmtpConnection(string Host, int Port, SmtpSecurity Security, string? Username, string? Password)
{
    // 密碼不能出現在記錄或例外訊息裡，所以覆寫掉自動產生的 ToString。
    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>要寄出的一封信。標頭（不要觸發自動回覆）由傳送的那一層加。</summary>
internal sealed record OutgoingEmail(
    string FromAddress, string? FromName, string ToAddress, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// 寄信的那一層。放在模組內部的介面後面：正式的實作用 MailKit，測試換成假的，記下寄了什麼、或模擬失敗。
/// 失敗一律丟 <see cref="EmailDeliveryException"/>，呼叫端不必認得 MailKit 的例外。
/// </summary>
internal interface IEmailTransport
{
    /// <summary>連線並登入。一個組織的信用同一個連線寄，寄完再斷。</summary>
    Task<IEmailSession> OpenAsync(SmtpConnection connection, CancellationToken cancellationToken);
}

internal interface IEmailSession : IAsyncDisposable
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

internal enum EmailFailureKind
{
    /// <summary>連不上。</summary>
    Unreachable,

    /// <summary>帳號或密碼不對。</summary>
    AuthenticationFailed,

    /// <summary>加密談不成（對方不支援、憑證不被信任）。</summary>
    TlsFailed,

    /// <summary>對方拒收寄件人地址。</summary>
    SenderRejected,

    /// <summary>對方拒收這一個收件人（信箱不存在）。只影響這個人，不影響其他人的信。</summary>
    RecipientRejected,

    /// <summary>收件人的地址本身就寄不了（格式讓寄信的函式庫解不開）。只影響這個人。</summary>
    InvalidRecipient,

    /// <summary>這封信在 DATA 階段被永久拒收（5xx）。只略過這個收件人，不影響其他人的信。</summary>
    MessageRejected,

    /// <summary>收件人暫時收不了（RCPT 或 DATA 回 4xx，例如信箱滿、greylisting）。這一輪略過他，通知照舊在等，不讓整個組織退避。</summary>
    RecipientTemporarilyFailed,

    Timeout,

    /// <summary>其他（對方回了不認得的狀態、連線中途斷了）。</summary>
    Other,
}

/// <summary>寄信失敗。<see cref="Exception.Message"/> 是我們自己的分類說明，<see cref="ServerResponse"/> 是對方主機回的話（可能是 null）。</summary>
internal sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(EmailFailureKind kind, string message, string? serverResponse = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        ServerResponse = serverResponse;
    }

    public EmailFailureKind Kind { get; }

    public string? ServerResponse { get; }

    /// <summary>給人看的原因：說明後面接對方主機的話（去掉控制字元），總長不超過 <paramref name="maxLength"/>。</summary>
    public string Describe(int maxLength)
    {
        var detail = ServerResponse is null ? string.Empty : new string([.. ServerResponse.Where(c => !char.IsControl(c))]).Trim();
        var text = detail.Length == 0 ? Message : $"{Message}（{detail}）";
        return TextLimits.Truncate(text, maxLength);
    }
}

/// <summary>文字截斷。不切在 surrogate pair 中間：落單的半個字元，資料庫與標頭都不收。</summary>
internal static class TextLimits
{
    /// <summary>超過 <paramref name="maxLength"/> 就截短、結尾加「…」，總長不超過 <paramref name="maxLength"/>。</summary>
    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = maxLength - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut] + "…";
    }
}
