using System.Globalization;
using System.Net;
using System.Text;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Email;

/// <summary>
/// 組信（規格見 HANDOFF-email-notifications 第 5 節）。HTML 與純文字各一份；標題、摘錄、名稱都是使用者輸入，
/// HTML 裡的所有文字一律跳脫、摘錄照原文顯示（Markdown 不轉換）。樣式寫成 inline、單欄、不用圖片。
/// </summary>
internal static partial class EmailComposer
{
    public const string UnknownActorName = "（已不在組織）";

    public const string TestSubject = "測試信：Email 通知的設定可以用了";

    private const int MaxSubjectLength = 150;

    private const string TimeFormat = "MM/dd HH:mm";

    /// <summary>一封通知信要的東西。<paramref name="Names"/> 查不到的人用 <see cref="UnknownActorName"/>。</summary>
    public sealed record Context(
        string FromAddress,
        string? FromName,
        string ToAddress,
        string SiteUrl,
        TimeZoneInfo Zone,
        IReadOnlyDictionary<Guid, string> Names);

    public static OutgoingEmail Test(string fromAddress, string? fromName, string toAddress, string? siteUrl)
    {
        const string text = "這是從 Kumi 的「寄信設定」頁寄出的測試信。收到這封信，代表寄信的設定可以用了。";
        var html = $"<p style=\"{BodyStyle}\">{Encode(text)}</p>";
        return new OutgoingEmail(fromAddress, fromName, toAddress, TestSubject, Wrap(html), siteUrl is null ? text : $"{text}\n{siteUrl}");
    }

    /// <param name="notifications">要寄的通知，越舊的越前面；至少一則。只列前 <see cref="EmailRules.MaxNotificationsPerEmail"/> 則，其餘寫「還有幾則」。</param>
    public static OutgoingEmail Notification(Context context, IReadOnlyList<NotificationRow> notifications)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notifications);
        if (notifications.Count == 0)
        {
            throw new ArgumentException("至少要有一則通知。", nameof(notifications));
        }

        var listed = notifications.Take(EmailRules.MaxNotificationsPerEmail).ToList();
        var omitted = notifications.Count - listed.Count;
        var groups = listed.GroupBy(n => n.ItemId).ToList();
        var html = new StringBuilder();
        var text = new StringBuilder();
        foreach (var group in groups)
        {
            AppendGroup(context, group.ToList(), html, text);
        }

        if (omitted > 0)
        {
            var more = $"還有 {omitted} 則，請到收件匣看。";
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"{BodyStyle}\">{Encode(more)}</p>");
            text.Append(more).Append("\n\n");
        }

        var inbox = $"{context.SiteUrl}/inbox";
        var account = $"{context.SiteUrl}/account";
        const string inboxLabel = "到收件匣看全部：";
        const string accountLabel = "不想收這些信：到「我的帳號」關掉 Email 通知（";
        html.Append(CultureInfo.InvariantCulture,
            $"<p style=\"{FooterStyle}\">{Encode(inboxLabel)}<a href=\"{Encode(inbox)}\">{Encode(inbox)}</a><br>{Encode(accountLabel)}<a href=\"{Encode(account)}\">{Encode(account)}</a>）</p>");
        text.Append(inboxLabel).Append(inbox).Append('\n').Append(accountLabel).Append(account).Append("）\n");

        return new OutgoingEmail(
            context.FromAddress, context.FromName, context.ToAddress, SubjectOf(context, notifications), Wrap(html.ToString()), text.ToString());
    }

    internal static string SubjectOf(Context context, IReadOnlyList<NotificationRow> notifications)
    {
        var first = notifications[0];
        var subject = notifications.Count == 1
            ? $"{NameOf(context, first.ActorPersonId)} {Action(first)}：{first.ItemKey} {first.ItemTitle}"
            : notifications.All(n => n.ItemId == first.ItemId)
                ? $"{notifications.Count} 則新通知：{first.ItemKey} {first.ItemTitle}"
                : $"{notifications.Count} 則新通知：{first.ItemKey} {first.ItemTitle} 等";

        // 標題是使用者輸入：換行會把標頭拆開，所以一律換成空白。
        var oneLine = new string([.. subject.Select(c => char.IsControl(c) ? ' ' : c)]);
        return TextLimits.Truncate(oneLine, MaxSubjectLength);
    }

    private static void AppendGroup(Context context, List<NotificationRow> rows, StringBuilder html, StringBuilder text)
    {
        var head = rows[0];
        var url = ItemUrl(context.SiteUrl, head);
        var title = $"{head.ItemKey} {head.ItemTitle}";
        html.Append(CultureInfo.InvariantCulture,
            $"<h3 style=\"margin:20px 0 6px;font-size:16px\"><a href=\"{Encode(url)}\" style=\"color:#1a56db;text-decoration:none\">{Encode(title)}</a></h3><ul style=\"margin:0;padding-left:20px\">");
        text.Append(title.ReplaceLineEndings(" ")).Append('\n').Append(url).Append('\n');
        foreach (var row in rows)
        {
            var line = $"{NameOf(context, row.ActorPersonId)} {Describe(row)}";
            var time = TimeZoneInfo.ConvertTime(row.CreatedAt, context.Zone).ToString(TimeFormat, CultureInfo.InvariantCulture);
            var link = row.CommentId is null ? url : $"{url}#comment-{row.CommentId}";
            html.Append(CultureInfo.InvariantCulture,
                $"<li style=\"{BodyStyle}margin:4px 0\">{Encode(line)}<span style=\"color:#6b7280\"> · {Encode(time)}</span> <a href=\"{Encode(link)}\" style=\"color:#1a56db\">查看</a></li>");
            text.Append("  - ").Append(line.ReplaceLineEndings(" ")).Append("（").Append(time).Append("）\n");
        }

        html.Append("</ul>");
        text.Append('\n');
    }

    private static string ItemUrl(string siteUrl, NotificationRow row) =>
        $"{siteUrl}/projects/{Uri.EscapeDataString(row.ProjectKey!)}/items/{Uri.EscapeDataString(row.ItemKey!)}";

    private static string NameOf(Context context, Guid personId) =>
        context.Names.TryGetValue(personId, out var name) ? name : UnknownActorName;

    /// <summary>主旨用的動作：「王小明 指派給你」。</summary>
    private static string Action(NotificationRow row) => row.Kind switch
    {
        NotificationKind.Assigned => "指派給你",
        NotificationKind.Mentioned => "提及了你",
        NotificationKind.Commented => "留言",
        NotificationKind.StateChanged => "改了狀態",
        NotificationKind.Replied => "回覆了你的留言",
        _ => "有新動態",
    };

    /// <summary>內文的一行（不含做事的人的名稱與時間）。</summary>
    private static string Describe(NotificationRow row) => row.Kind switch
    {
        NotificationKind.Assigned => "把這個項目指派給你",
        NotificationKind.Mentioned => WithExcerpt("提及了你", row.Excerpt),
        NotificationKind.Commented => WithExcerpt("留言", row.Excerpt),
        NotificationKind.StateChanged => row.FromStateName is null
            ? $"把狀態改成「{row.ToStateName}」"
            : $"把狀態從「{row.FromStateName}」改成「{row.ToStateName}」",
        NotificationKind.Replied => WithExcerpt("回覆了你的留言", row.Excerpt),
        _ => "有新動態",
    };

    private static string WithExcerpt(string action, string? excerpt) =>
        string.IsNullOrWhiteSpace(excerpt) ? action : $"{action}：『{excerpt}』";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private const string BodyStyle = "margin:8px 0;font-size:14px;line-height:1.6;color:#111827;";

    private const string FooterStyle = "margin:24px 0 0;padding-top:12px;border-top:1px solid #e5e7eb;font-size:12px;line-height:1.6;color:#6b7280;";

    private static string Wrap(string body) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head>"
        + "<body style=\"margin:0;padding:16px;background:#ffffff;font-family:-apple-system,'Segoe UI',Helvetica,Arial,sans-serif\">"
        + $"<div style=\"max-width:560px;margin:0 auto\">{body}</div></body></html>";
}
