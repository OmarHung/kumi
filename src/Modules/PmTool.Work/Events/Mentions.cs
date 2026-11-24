using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Persistence;

namespace PmTool.Work.Events;

/// <summary>
/// 說明與留言裡的提及，寫法是 <c>@[名稱](mention:人員編號)</c>（見 <see cref="WorkEventRules.MentionScheme"/>）。
/// 內容照文字存、不擋不認得的提及；只有事件裡的 <see cref="ItemEventData.MentionedPersonIds"/> 要過濾。
/// </summary>
internal static partial class Mentions
{
    // 名稱有長度上限：沒有上限時，一大串「@[」每個都要往後掃到底，時間是二次方，儲存會因為逾時而失敗。
    private const string Pattern = @"@\[(?<name>[^\]\r\n]{0,100})\]\(mention:(?<id>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\)";

    /// <summary>文字裡提及的人，依第一次出現的順序、去重。解析逾時就當作沒有提及，不讓儲存失敗。</summary>
    public static IReadOnlyList<Guid> Parse(string? text) => Parse(Link(), text);

    /// <summary>
    /// 留言的摘要：提及換成「@名稱」、Markdown 的符號去掉（見 <see cref="PlainText"/>），最多 <see cref="WorkEventRules.MaxExcerptLength"/> 個字。
    /// 提及的解析逾時就不換，照原文處理。
    /// </summary>
    public static string Excerpt(string text) => Excerpt(Link(), text);

    // 給測試用的接縫：換成一定逾時的樣式，才能證明逾時的路徑。
    internal static IReadOnlyList<Guid> Parse(Regex link, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        try
        {
            return [.. link.Matches(text)
                .Select(m => Guid.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture))
                .Distinct()];
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }

    internal static string Excerpt(Regex link, string text)
    {
        string withMentions;
        try
        {
            // 附件的參照先換掉：圖片和檔案連結後面是一串編號，收件匣不該看到。
            var withoutAttachments = AttachmentFile().Replace(
                AttachmentImage().Replace(text, "［圖片］"), m => Unescape(m.Groups["name"].Value));
            withMentions = link.Replace(withoutAttachments, m => "@" + m.Groups["name"].Value);
        }
        catch (RegexMatchTimeoutException)
        {
            withMentions = text;
        }

        var plain = PlainText.From(withMentions);
        if (plain.Length <= WorkEventRules.MaxExcerptLength)
        {
            return plain;
        }

        // 不要從代理字元對的中間切開，切出來的半個字元寫不進 JSON。
        var length = char.IsHighSurrogate(plain[WorkEventRules.MaxExcerptLength - 1])
            ? WorkEventRules.MaxExcerptLength - 1
            : WorkEventRules.MaxExcerptLength;
        return plain[..length];
    }

    /// <summary>
    /// 這一次要通知的提及：<paramref name="after"/> 有、<paramref name="before"/> 沒有的人（留言沒有舊內容），
    /// 而且是看得到這個項目的成員（只看自己的角色要是項目的負責人或建立者）、不是做這件事的人，最多 <see cref="WorkEventRules.MaxMentionsPerChange"/> 個。
    /// 先過濾再截斷：前面幾個不合格的提及不會擠掉後面合格的。
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> NotifiableAsync(
        WorkDbContext db, ProjectAccess access, Actor actor, ItemEntity item, string? before, string? after, CancellationToken cancellationToken)
    {
        var known = Parse(before).ToHashSet();
        var added = Parse(after).Where(id => id != actor.PersonId && !known.Contains(id)).ToArray();
        if (added.Length == 0)
        {
            return [];
        }

        var readers = await ProjectReaders.FilterItemAsync(
            db, access.Project.OrganizationId, access.Project.Id, access.Schema, new ItemOwnership(item.CreatedBy, item.AssigneeId), added, cancellationToken);
        return [.. added.Where(readers.Contains).Take(WorkEventRules.MaxMentionsPerChange)];
    }

    private static string Unescape(string name) => EscapedChar().Replace(name, "$1");

    private const string AttachmentId = @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}";

    // 檔名裡的 ] 與 \ 在 Markdown 裡是跳脫過的（\] \\）；長度有上限，理由同提及。
    [GeneratedRegex(@"!\[(?:\\.|[^\]\\\r\n]){0,255}\]\(attachment:" + AttachmentId + @"\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AttachmentImage();

    [GeneratedRegex(@"\[(?<name>(?:\\.|[^\]\\\r\n]){0,255})\]\(attachment:" + AttachmentId + @"\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AttachmentFile();

    [GeneratedRegex(@"\\(.)", RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EscapedChar();

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Link();
}
