using PmTool.Abstractions;

namespace PmTool.Notifications;

/// <summary>
/// 組織的寄信設定。只有組織管理員能讀、能改（<see cref="NotificationErrorCodes.EmailForbidden"/>）。
/// 規格見 <c>internal/HANDOFF-email-notifications.md</c> 第 3 節。
/// </summary>
public interface IEmailSettingsService
{
    /// <summary>還沒設定過時回「沒開、Gmail、其他都空白」的樣子，不是失敗。密碼不會讀出來，只說有沒有存。</summary>
    Task<Result<EmailSettings>> GetAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 存起來（有就整份換掉）。<see cref="EmailSettingsInput.Password"/> 是 null 時沿用已經存的密碼。
    /// 內容不對回 <see cref="NotificationErrorCodes.EmailSettingsInvalid"/>，細項的路徑是欄位名稱（camelCase）。
    /// </summary>
    Task<Result<EmailSettings>> SaveAsync(Actor actor, EmailSettingsInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 用這份（還沒存的）設定寄一封測試信到 <paramref name="recipientAddress"/>（呼叫端給的是管理員自己的 Email）。
    /// 不管 <see cref="EmailSettingsInput.Enabled"/>，欄位一律要齊全。密碼是 null 時用已經存的。
    /// 寄不出去回 <see cref="NotificationErrorCodes.EmailSendFailed"/>，訊息說明原因（連不上、帳號密碼不對、加密談不成、對方拒收）。
    /// 不改存的設定，也不記進寄送的狀態。
    /// </summary>
    Task<Result<Unit>> SendTestAsync(
        Actor actor, EmailSettingsInput input, string recipientAddress, CancellationToken cancellationToken);
}

/// <summary>自己要不要收 Email 通知。每個人都能改自己的；沒改過就是要收。</summary>
public interface IEmailPreferenceService
{
    Task<EmailPreference> GetAsync(Actor actor, CancellationToken cancellationToken);

    Task<EmailPreference> SetAsync(Actor actor, bool enabled, CancellationToken cancellationToken);
}

/// <summary>
/// 把該寄的通知寄出去。API 的背景工作定期呼叫；好幾個程序同時呼叫時只有一個在寄，其他的直接回 0。
/// 規則見 <c>internal/HANDOFF-email-notifications.md</c> 第 4 節。
/// </summary>
public interface IEmailDispatcher
{
    /// <summary>跑一輪，回傳這一輪寄出的信的封數。測試直接呼叫它，不必等計時器。</summary>
    Task<int> DispatchAsync(CancellationToken cancellationToken);
}

/// <summary>寄信服務。Gmail 是預設：主機、連接埠、加密方式固定，只要填 Gmail 地址與應用程式密碼。</summary>
public enum EmailProvider
{
    Gmail,

    /// <summary>其他家或自建的 SMTP 主機，主機、連接埠、加密方式、帳號都自己填。</summary>
    Smtp,
}

/// <summary>SMTP 的加密方式。</summary>
public enum SmtpSecurity
{
    /// <summary>先連上再升級成 TLS（通常是 587）。對方不支援升級就失敗，不退回明文。</summary>
    StartTls,

    /// <summary>一連上就是 TLS（通常是 465）。</summary>
    Tls,

    /// <summary>不加密，只適合同一個內網裡自建的轉信主機；這時不能設帳號密碼（會用明文送出）。</summary>
    None,
}

/// <summary>讀出來的寄信設定。</summary>
/// <param name="Host">只有 <see cref="EmailProvider.Smtp"/> 有；Gmail 是 null（實際連 <see cref="EmailRules.GmailHost"/>）。</param>
/// <param name="Port">同上。</param>
/// <param name="Security">同上。</param>
/// <param name="Username">Gmail 是 Gmail 地址；SMTP 可以是 null（不用登入的轉信主機）。</param>
/// <param name="HasPassword">有沒有存密碼。密碼本身不會讀出來。</param>
/// <param name="FromAddress">寄件人地址。只有 SMTP 有；Gmail 一律用 Gmail 地址（Gmail 會改掉別的寄件人）。</param>
/// <param name="FromName">寄件人名稱，null 是只寫地址。</param>
/// <param name="SiteUrl">網站的網址，信裡的連結用，例如 <c>https://pm.example.com</c>（結尾沒有斜線）。</param>
/// <param name="UpdatedAt">最後一次存的時間；沒存過是 null。</param>
/// <param name="UpdatedBy">最後一次存的人；沒存過是 null。</param>
public sealed record EmailSettings(
    bool Enabled,
    EmailProvider Provider,
    string? Host,
    int? Port,
    SmtpSecurity? Security,
    string? Username,
    bool HasPassword,
    string? FromAddress,
    string? FromName,
    string? SiteUrl,
    EmailDeliveryStatus Status,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy);

/// <summary>背景寄送的狀況（測試信不算）。</summary>
/// <param name="LastSentAt">最後一次寄出成功的時間。</param>
/// <param name="LastFailedAt">最後一次因為設定或連線而寄不出去的時間（某一個收件人被拒收不算）。</param>
/// <param name="LastError">那一次的原因，繁體中文，後面可能接著對方主機回的一小段話。</param>
public sealed record EmailDeliveryStatus(DateTimeOffset? LastSentAt, DateTimeOffset? LastFailedAt, string? LastError);

/// <summary>
/// 要存的寄信設定。<see cref="EmailSettings"/> 的欄位意思相同；選 Gmail 時 <see cref="Host"/>、<see cref="Port"/>、
/// <see cref="Security"/>、<see cref="FromAddress"/> 不看，存成 null。
/// </summary>
/// <param name="Password">null 是沿用已經存的，空字串是清掉，其他是換成新的。Gmail 的應用程式密碼會去掉空白。</param>
public sealed record EmailSettingsInput(
    bool Enabled,
    EmailProvider Provider,
    string? Host,
    int? Port,
    SmtpSecurity? Security,
    string? Username,
    string? Password,
    string? FromAddress,
    string? FromName,
    string? SiteUrl)
{
    // 帶著密碼：記錄、例外訊息與測試失敗的輸出都會呼叫 ToString，密碼一律遮住。形狀不變。
    public override string ToString() =>
        $"EmailSettingsInput {{ Enabled = {Enabled}, Provider = {Provider}, Host = {Host}, Port = {Port}, Security = {Security}, "
        + $"Username = {Username}, Password = {(Password is null ? "null" : "***")}, FromAddress = {FromAddress}, FromName = {FromName}, SiteUrl = {SiteUrl} }}";
}

/// <param name="Enabled">自己要不要收。</param>
/// <param name="OrganizationEnabled">組織開了寄信沒有；沒開的話自己開著也收不到。</param>
public sealed record EmailPreference(bool Enabled, bool OrganizationEnabled);

public static class EmailRules
{
    public const string GmailHost = "smtp.gmail.com";

    public const int GmailPort = 587;

    /// <summary>
    /// 能設的連接埠。只開 SMTP 常用的幾個：伺服器會照設定去連，任意連接埠等於讓人拿它掃內網。
    /// </summary>
    public static readonly IReadOnlyList<int> AllowedPorts = [25, 465, 587, 2525];

    public const int MaxHostLength = 253;

    public const int MaxUsernameLength = 254;

    public const int MaxPasswordLength = 256;

    public const int MaxFromNameLength = 100;

    public const int MaxSiteUrlLength = 200;

    /// <summary>通知產生之後這麼久還沒讀才寄：人在網頁上就不必再收信，也讓同一段時間的幾則併成一封。</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 一個組織在一輪裡，連線與寄出最多花這麼久；超過就當成這個組織這一輪失敗、換下一個組織。
    /// 一個卡住的郵件主機不能拖住所有組織的信。標記為已處理的寫入不受這個期限限制。
    /// </summary>
    public static readonly TimeSpan MaxOrganizationDispatchTime = TimeSpan.FromMinutes(2);

    /// <summary>超過這麼久還沒寄出（例如寄信一直失敗）就不寄了，免得修好之後一次收到一堆過時的信。</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>一封信最多列幾則，其餘的寫「還有幾則」，請他到收件匣看。</summary>
    public const int MaxNotificationsPerEmail = 20;

    /// <summary>一輪最多寄給幾個人，剩下的下一輪再寄。</summary>
    public const int MaxRecipientsPerRun = 50;

    /// <summary>連線、登入、寄出各自的逾時。</summary>
    public static readonly TimeSpan SmtpTimeout = TimeSpan.FromSeconds(20);
}
