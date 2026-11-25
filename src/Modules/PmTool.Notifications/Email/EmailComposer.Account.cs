using System.Globalization;
using System.Text;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Email;

/// <summary>帳號通知的信：不分組、不連到項目，連到「命令列與整合」頁；告訴本人做了什麼，不是他做的要到那一頁撤銷並改密碼。</summary>
internal static partial class EmailComposer
{
    private const string ProductName = "Kumi";

    private const string DateFormat = "yyyy/MM/dd";

    private const string IntegrationsLabel = "「命令列與整合」";

    /// <summary>權杖範圍的對外名稱和畫面上的說法（和網頁的存取權杖頁同一組用詞，兩邊要一致）。</summary>
    private static readonly IReadOnlyDictionary<string, string> ScopeLabels = new Dictionary<string, string>
    {
        ["items:read"] = "讀取項目",
        ["items:write"] = "修改項目",
        ["blueprint:read"] = "讀取設定",
        ["blueprint:write"] = "修改設定",
        ["agent:run"] = "本機代理",
    };

    /// <param name="notices">要寄的帳號通知，越舊的越前面；至少一則，每一則都要有 <see cref="NotificationRow.AccountNotice"/>。</param>
    public static OutgoingEmail AccountNotices(Context context, IReadOnlyList<NotificationRow> notices)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notices);
        if (notices.Count == 0)
        {
            throw new ArgumentException("至少要有一則通知。", nameof(notices));
        }

        var listed = notices.Take(EmailRules.MaxNotificationsPerEmail).ToList();
        var html = new StringBuilder();
        var text = new StringBuilder();
        foreach (var row in listed)
        {
            AppendNotice(context, row, html, text);
        }

        var omitted = notices.Count - listed.Count;
        if (omitted > 0)
        {
            var more = $"還有 {omitted} 則，請到收件匣看。";
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"{BodyStyle}\">{Encode(more)}</p>");
            text.Append(more).Append("\n\n");
        }

        var url = $"{context.SiteUrl}/account/integrations";
        var warning = $"不是你做的話，請到{IntegrationsLabel}撤銷並改密碼：";
        html.Append(CultureInfo.InvariantCulture,
            $"<p style=\"{FooterStyle}\">{Encode(warning)}<a href=\"{Encode(url)}\">{Encode(url)}</a></p>");
        text.Append(warning).Append(url).Append('\n');

        return new OutgoingEmail(
            context.FromAddress, context.FromName, context.ToAddress, AccountSubject(notices), Wrap(html.ToString()), text.ToString());
    }

    internal static string AccountSubject(IReadOnlyList<NotificationRow> notices) =>
        notices.Count == 1
            ? $"{ProductName}：{AccountSubjectOf(notices[0].AccountNotice!.Event)}"
            : $"{ProductName}：你的帳號有 {notices.Count} 則安全通知";

    private static string AccountSubjectOf(AccountNoticeEvent kind) => kind switch
    {
        AccountNoticeEvent.AccessTokenCreated => "你的帳號建立了存取權杖",
        AccountNoticeEvent.CliLoginTokensIssued => "命令列登入了你的帳號",
        AccountNoticeEvent.OauthAppAuthorized => "你的帳號授權了 AI 用戶端",
        AccountNoticeEvent.CliLoginCodeReused => "你的登入碼被重複使用",
        AccountNoticeEvent.LocalAgentMachineAdded => "你的本機代理出現在新的電腦",
        _ => "你的帳號有新的安全通知",
    };

    private static void AppendNotice(Context context, NotificationRow row, StringBuilder html, StringBuilder text)
    {
        var notice = row.AccountNotice!;
        var time = TimeZoneInfo.ConvertTime(row.CreatedAt, context.Zone).ToString(TimeFormat, CultureInfo.InvariantCulture);
        var headline = OneLine(AccountHeadline(notice));
        var details = AccountDetails(context, notice);

        html.Append(CultureInfo.InvariantCulture,
            $"<h3 style=\"margin:20px 0 6px;font-size:16px\">{Encode(headline)}<span style=\"color:#6b7280;font-weight:normal\"> · {Encode(time)}</span></h3><ul style=\"margin:0;padding-left:20px\">");
        text.Append(headline).Append('（').Append(time).Append("）\n");
        foreach (var detail in details)
        {
            html.Append(CultureInfo.InvariantCulture, $"<li style=\"{BodyStyle}margin:4px 0\">{Encode(detail)}</li>");
            text.Append("  - ").Append(detail).Append('\n');
        }

        html.Append("</ul>");
        text.Append('\n');
    }

    private static string AccountHeadline(AccountNotice notice) => notice.Event switch
    {
        AccountNoticeEvent.AccessTokenCreated when notice.Tokens.Count == 1 => $"建立了存取權杖「{notice.Tokens[0].Name}」",
        AccountNoticeEvent.AccessTokenCreated => $"建立了 {notice.Tokens.Count} 把存取權杖",
        AccountNoticeEvent.CliLoginTokensIssued => notice.MachineName is { Length: > 0 } machine
            ? $"命令列在「{machine}」登入，建立了 {notice.Tokens.Count} 把存取權杖"
            : $"命令列登入，建立了 {notice.Tokens.Count} 把存取權杖",
        AccountNoticeEvent.OauthAppAuthorized => $"授權了「{notice.ClientName ?? "AI 用戶端"}」連到你的帳號",
        AccountNoticeEvent.CliLoginCodeReused => $"登入碼被重複使用，已撤銷 {notice.RevokedTokens} 把存取權杖",
        AccountNoticeEvent.LocalAgentMachineAdded => notice.MachineName is { Length: > 0 } added
            ? $"本機代理第一次從新的電腦「{added}」登記"
            : "本機代理第一次從新的電腦登記",
        _ => "帳號有新的安全通知",
    };

    private static List<string> AccountDetails(Context context, AccountNotice notice)
    {
        List<string> lines = [];
        foreach (var token in notice.Tokens)
        {
            var expiry = token.ExpiresAt is { } at
                ? $"{TimeZoneInfo.ConvertTime(at, context.Zone).ToString(DateFormat, CultureInfo.InvariantCulture)} 到期"
                : "不會過期";
            lines.Add(OneLine($"權杖「{token.Name}」：{ScopeText(token.Scopes)}；{expiry}"));
        }

        if (notice.Scopes.Count > 0)
        {
            lines.Add($"範圍：{ScopeText(notice.Scopes)}");
        }

        AddIfPresent(lines, "用戶端網址", notice.ClientHost);
        AddIfPresent(lines, "電腦名稱", notice.MachineName);
        AddIfPresent(lines, "來源位址", notice.SourceAddress);
        return lines;
    }

    private static void AddIfPresent(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(OneLine($"{label}：{value}"));
        }
    }

    private static string ScopeText(IReadOnlyList<string> scopes) =>
        scopes.Count == 0 ? "沒有範圍" : string.Join('、', scopes.Select(scope => ScopeLabels.GetValueOrDefault(scope, scope)));

    /// <summary>名稱、電腦名稱是使用者輸入：換行會把純文字拆成好幾行，所以一律換成空白。</summary>
    private static string OneLine(string value) => new([.. value.ReplaceLineEndings(" ").Select(c => char.IsControl(c) ? ' ' : c)]);
}
