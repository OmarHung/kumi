using System.Text.RegularExpressions;
using MimeKit;
using PmTool.Abstractions;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Email;

/// <summary>密碼怎麼處理：沿用已經存的、清掉、換成新的。</summary>
internal enum PasswordChange
{
    Keep,
    Clear,
    Replace,
}

/// <summary>檢查過、整理好的設定（還沒加密密碼）。Gmail 時主機、連接埠、加密方式、寄件人地址都是 null。</summary>
internal sealed record ValidatedEmailSettings(
    bool Enabled,
    EmailProvider Provider,
    string? Host,
    int? Port,
    SmtpSecurity? Security,
    string? Username,
    PasswordChange PasswordChange,
    string? NewPassword,
    string? FromAddress,
    string? FromName,
    string? SiteUrl)
{
    // 帶著密碼，不能在記錄、例外訊息或測試失敗的輸出裡露出來。
    public override string ToString() =>
        $"ValidatedEmailSettings {{ Enabled = {Enabled}, Provider = {Provider}, Host = {Host}, Port = {Port}, Security = {Security}, "
        + $"Username = {Username}, PasswordChange = {PasswordChange}, NewPassword = {(NewPassword is null ? "null" : "***")} }}";
}

/// <summary>寄信設定的欄位檢查（規格見 HANDOFF-email-notifications 第 3.1 節）。細項的路徑是 camelCase 的欄位名稱。</summary>
internal static partial class EmailSettingsValidator
{
    private const string UsernamePath = "username";
    private const string PasswordPath = "password";
    private const string HostPath = "host";
    private const string PortPath = "port";
    private const string SecurityPath = "security";
    private const string FromAddressPath = "fromAddress";
    private const string FromNamePath = "fromName";
    private const string SiteUrlPath = "siteUrl";

    private const int MaxEmailLength = 254;

    /// <param name="requireFields">寄測試信時一律當作開著，必填的欄位都要有。</param>
    /// <param name="existing">已經存的設定（沒存過是 null）：密碼沿用時算有密碼，也要確認沒有換目的地。</param>
    public static Result<ValidatedEmailSettings> Validate(EmailSettingsInput input, bool requireFields, EmailSettingsRow? existing)
    {
        ArgumentNullException.ThrowIfNull(input);
        var hasStoredPassword = existing?.ProtectedPassword is not null;

        var problems = new List<FailureDetail>();
        var required = requireFields || input.Enabled;
        var isGmail = input.Provider == EmailProvider.Gmail;

        var username = Clean(input.Username, UsernamePath, EmailRules.MaxUsernameLength, problems);
        var fromName = Clean(input.FromName, FromNamePath, EmailRules.MaxFromNameLength, problems);
        var password = CheckPassword(input, isGmail, problems);
        var hasPassword = password.Change switch
        {
            PasswordChange.Keep => hasStoredPassword,
            PasswordChange.Clear => false,
            _ => true,
        };

        CheckUsername(username, isGmail, required, problems);
        if (isGmail && required && !hasPassword)
        {
            problems.Add(Required(PasswordPath, "要填 Gmail 的應用程式密碼。"));
        }

        var siteUrl = CheckSiteUrl(input.SiteUrl, required, problems);

        string? host = null, fromAddress = null;
        int? port = null;
        SmtpSecurity? security = null;
        if (!isGmail)
        {
            host = CheckHost(input.Host, required, problems);
            port = CheckPort(input.Port, required, problems);
            security = input.Security;
            CheckSecurity(security, required, username is not null || hasPassword, problems);
            fromAddress = CheckFromAddress(input.FromAddress, required, problems);
        }

        CheckPasswordDestination(existing, input.Password is null, (input.Provider, host, port, username), problems);

        return problems.Count > 0
            ? new Failure(FailureKind.Invalid, NotificationErrorCodes.EmailSettingsInvalid, "寄信設定有幾個欄位不對。") { Details = problems }
            : new ValidatedEmailSettings(
                input.Enabled, input.Provider, host, port, security, username, password.Change, password.Value, fromAddress, fromName, siteUrl);
    }

    /// <summary>
    /// 沿用已經存的密碼時，不能換寄信服務、主機、連接埠或帳號：不然另一個管理員只要把設定指到自己的主機、
    /// 密碼留白，伺服器就會把存的密碼送過去。要換目的地就要重新輸入密碼。加密方式不算（不加密時本來就不能有密碼）。
    /// </summary>
    private static void CheckPasswordDestination(
        EmailSettingsRow? existing,
        bool keepsPassword,
        (EmailProvider Provider, string? Host, int? Port, string? Username) next,
        List<FailureDetail> problems)
    {
        if (!keepsPassword || existing?.ProtectedPassword is null)
        {
            return;
        }

        var same = existing.Provider == next.Provider
            && string.Equals(existing.Host, next.Host, StringComparison.OrdinalIgnoreCase)
            && existing.Port == next.Port
            && string.Equals(existing.Username, next.Username, StringComparison.OrdinalIgnoreCase);
        if (!same)
        {
            problems.Add(Required(PasswordPath, "改了寄信服務、主機、連接埠或帳號，要重新輸入密碼。"));
        }
    }

    private static (PasswordChange Change, string? Value) CheckPassword(EmailSettingsInput input, bool isGmail, List<FailureDetail> problems)
    {
        if (input.Password is null)
        {
            return (PasswordChange.Keep, null);
        }

        // Google 把應用程式密碼顯示成四組四碼，貼上來常常帶著空白。
        var value = isGmail ? new string([.. input.Password.Where(c => !char.IsWhiteSpace(c))]) : input.Password;
        if (value.Length == 0)
        {
            return (PasswordChange.Clear, null);
        }

        if (value.Length > EmailRules.MaxPasswordLength || value.Any(char.IsControl))
        {
            problems.Add(Invalid(PasswordPath, "密碼太長或含有不能用的字元。"));
            return (PasswordChange.Keep, null);
        }

        return (PasswordChange.Replace, value);
    }

    private static void CheckUsername(string? username, bool isGmail, bool required, List<FailureDetail> problems)
    {
        if (isGmail && username is null)
        {
            if (required)
            {
                problems.Add(Required(UsernamePath, "要填 Gmail 地址。"));
            }

            return;
        }

        if (isGmail && username is not null && !IsEmailAddress(username))
        {
            problems.Add(Invalid(UsernamePath, "Gmail 地址的格式不對。"));
        }
    }

    private static string? CheckHost(string? raw, bool required, List<FailureDetail> problems)
    {
        var host = Clean(raw, HostPath, EmailRules.MaxHostLength, problems);
        if (host is null)
        {
            if (required && !problems.Any(p => p.Path == HostPath))
            {
                problems.Add(Required(HostPath, "要填 SMTP 主機。"));
            }

            return null;
        }

        if (!HostPattern().IsMatch(host))
        {
            problems.Add(Invalid(HostPath, "主機只能是主機名稱（英數字、點與連字號）或 IPv4 位址。"));
        }

        return host;
    }

    private static int? CheckPort(int? port, bool required, List<FailureDetail> problems)
    {
        if (port is null)
        {
            if (required)
            {
                problems.Add(Required(PortPath, "要選連接埠。"));
            }

            return null;
        }

        if (!EmailRules.AllowedPorts.Contains(port.Value))
        {
            problems.Add(new FailureDetail(
                NotificationErrorCodes.EmailPortNotAllowed, PortPath, $"連接埠只能是 {string.Join("、", EmailRules.AllowedPorts)}。"));
        }

        return port;
    }

    private static void CheckSecurity(SmtpSecurity? security, bool required, bool hasCredentials, List<FailureDetail> problems)
    {
        if (security is null)
        {
            if (required)
            {
                problems.Add(Required(SecurityPath, "要選加密方式。"));
            }

            return;
        }

        if (security == SmtpSecurity.None && hasCredentials)
        {
            problems.Add(new FailureDetail(
                NotificationErrorCodes.EmailInsecureCredentials, SecurityPath, "不加密時不能設帳號或密碼，會用明文送出。"));
        }
    }

    private static string? CheckFromAddress(string? raw, bool required, List<FailureDetail> problems)
    {
        var address = Clean(raw, FromAddressPath, MaxEmailLength, problems);
        if (address is null)
        {
            if (required && !problems.Any(p => p.Path == FromAddressPath))
            {
                problems.Add(Required(FromAddressPath, "要填寄件人地址。"));
            }

            return null;
        }

        if (!IsEmailAddress(address))
        {
            problems.Add(Invalid(FromAddressPath, "寄件人地址的格式不對。"));
        }

        return address;
    }

    private static string? CheckSiteUrl(string? raw, bool required, List<FailureDetail> problems)
    {
        var text = Clean(raw, SiteUrlPath, EmailRules.MaxSiteUrlLength, problems);
        if (text is null)
        {
            if (required && !problems.Any(p => p.Path == SiteUrlPath))
            {
                problems.Add(Required(SiteUrlPath, "要填網站網址，信裡的連結要用。"));
            }

            return null;
        }

        var isValid = Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo)
            && !text.Contains('?', StringComparison.Ordinal)
            && !text.Contains('#', StringComparison.Ordinal);
        if (!isValid)
        {
            problems.Add(Invalid(SiteUrlPath, "網站網址要是 http 或 https 開頭的完整網址，不能帶帳號密碼、問號或 #。"));
            return null;
        }

        return text.TrimEnd('/');
    }

    /// <summary>去掉前後空白、空的當 null；含 NUL 與控制字元或太長都算不合格（資料庫不收 NUL）。</summary>
    private static string? Clean(string? raw, string path, int maxLength, List<FailureDetail> problems)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Any(char.IsControl) || text.Length > maxLength)
        {
            problems.Add(Invalid(path, $"內容太長（最多 {maxLength} 個字）或含有不能用的字元。"));
            return null;
        }

        return text;
    }

    /// <summary>只擋明顯打錯的寫法，和身分模組的 Email 檢查同一個標準；真正收不收得到只有寄信才知道。</summary>
    internal static bool IsEmailAddress(string email)
    {
        if (email.Length is 0 or > MaxEmailLength || email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || ForbiddenInAddress.Contains(c)))
        {
            return false;
        }

        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@'))
        {
            return false;
        }

        var domain = email[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..", StringComparison.Ordinal);
    }

    // 地址裡不該出現的符號：它們在 Email 的語法裡是群組、註解、引號、多個地址的分隔，不是一般信箱會有的字元。
    private const string ForbiddenInAddress = "<>,;\"()[]\\";

    /// <summary>寄信的函式庫真的解得開、而且就是這一個地址（不是一串地址或附了名稱）。</summary>
    internal static bool IsDeliverable(string address) =>
        IsEmailAddress(address) && MailboxAddress.TryParse(address, out var mailbox) && mailbox.Address == address;

    private static FailureDetail Required(string path, string message) => new(NotificationErrorCodes.EmailFieldRequired, path, message);

    private static FailureDetail Invalid(string path, string message) => new(NotificationErrorCodes.EmailFieldInvalid, path, message);

    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$")]
    private static partial Regex HostPattern();
}
