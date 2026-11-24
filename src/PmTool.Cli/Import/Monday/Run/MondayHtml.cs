using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>被提及的人在這裡對上的人。</summary>
internal sealed record MentionTarget(Guid PersonId, string DisplayName);

/// <param name="Name">檔名（寫在圖片的說明裡）。</param>
/// <param name="AttachmentId">匯成附件之後的編號；null 是沒有搬過來。</param>
internal sealed record ImageTarget(string Name, Guid? AttachmentId);

/// <summary>轉換要問的兩件事：monday 的人是誰、monday 的檔案搬到哪裡去了。</summary>
internal sealed record HtmlContext(Func<string, MentionTarget?> ResolveUser, Func<string, ImageTarget?> ResolveAsset);

/// <param name="ReferencedAssetIds">內文裡用 img 引用的 asset 編號；沒有被引用的檔案接在留言最後面。</param>
internal sealed record HtmlMarkdown(string Text, IReadOnlySet<string> ReferencedAssetIds);

/// <summary>
/// monday 留言的 HTML → Markdown（internal/HANDOFF-monday-import.md 第 6.4 節）。用 AngleSharp 解析，轉換規則自己寫，
/// 因為提及、圖片要對到這裡的人與附件，通用的轉換套件做不到。
/// </summary>
internal static partial class MondayHtml
{
    private const string MentionClass = "user_mention_editor";
    private const string UnknownImageName = "圖片";

    [GeneratedRegex(@"[ \t\r\n\f]+")]
    private static partial Regex Whitespace();

    public static HtmlMarkdown ToMarkdown(string? html, HtmlContext context)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(html))
        {
            return new HtmlMarkdown(string.Empty, referenced);
        }

        var body = new HtmlParser().ParseDocument(html).Body;
        var text = body is null ? string.Empty : new Converter(context, referenced).Block(body, startsLine: true, inListItem: false);
        return new HtmlMarkdown(MarkdownText.Tidy(text), referenced);
    }

    /// <summary>輸出的緩衝：知道現在是不是在一行的開頭（行首的符號要跳脫）、段落之間要空幾行。</summary>
    private sealed class Output(bool startsLine)
    {
        private readonly StringBuilder _text = new();

        public bool AtLineStart => _text.Length == 0 ? startsLine : _text[^1] == '\n';

        public bool EndsWithSpace => _text.Length > 0 && _text[^1] == ' ';

        public bool IsEmpty => _text.Length == 0;

        public void Raw(string text) => _text.Append(text);

        /// <summary>前面要空一行（段落、標題、區塊）；開頭不用空。</summary>
        public void Hard() => BreakTo(2);

        /// <summary>只要換行（清單裡的巢狀清單緊接在文字後面）。</summary>
        public void Soft() => BreakTo(1);

        private void BreakTo(int newlines)
        {
            while (_text.Length > 0 && _text[^1] == ' ')
            {
                _text.Length--;
            }

            if (_text.Length == 0)
            {
                return;
            }

            var present = 0;
            while (present < _text.Length && _text[^(present + 1)] == '\n')
            {
                present++;
            }

            _text.Append('\n', Math.Max(0, newlines - present));
        }

        /// <summary>文字節點：HTML 的空白收成一個空格，行首與空格後面的空格丟掉，再跳脫。</summary>
        public void Text(string raw)
        {
            var text = Whitespace().Replace(MarkdownText.StripInvisible(raw).Replace(' ', ' '), " ");
            if (AtLineStart || EndsWithSpace)
            {
                text = text.TrimStart(' ');
            }

            if (text.Length > 0)
            {
                _text.Append(MarkdownText.EscapeInline(text, AtLineStart));
            }
        }

        /// <summary>接上已經轉好的一段行內文字；開頭的空白在行首或前面已經是空格時不重複。</summary>
        public void Inline(string text)
        {
            _text.Append(AtLineStart || EndsWithSpace ? text.TrimStart(' ') : text);
        }

        public override string ToString() => _text.ToString();
    }

    private sealed class Converter(HtmlContext context, HashSet<string> referenced)
    {
        public string Block(INode parent, bool startsLine, bool inListItem)
        {
            var output = new Output(startsLine);
            RenderChildren(parent, output, inListItem);
            return output.ToString();
        }

        private string InlineText(INode parent, bool startsLine = false)
        {
            var output = new Output(startsLine);
            RenderChildren(parent, output, inListItem: false);
            return output.ToString();
        }

        private void RenderChildren(INode parent, Output output, bool inListItem)
        {
            foreach (var child in parent.ChildNodes)
            {
                switch (child)
                {
                    case IText text:
                        output.Text(text.Data);
                        break;
                    case IElement element:
                        Render(element, output, inListItem);
                        break;
                }
            }
        }

        private void Render(IElement element, Output output, bool inListItem)
        {
            switch (element.LocalName)
            {
                case "script" or "style" or "head" or "template":
                    break;
                case "p" or "div" or "section" or "article" or "header" or "footer" or "figure" or "hr":
                    output.Hard();
                    RenderChildren(element, output, inListItem);
                    output.Hard();
                    break;
                case "br":
                    output.Raw("\n");
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Heading(element, output);
                    break;
                case "strong" or "b":
                    Wrap(element, output, "**");
                    break;
                case "em" or "i":
                    Wrap(element, output, "*");
                    break;
                case "s" or "del" or "strike":
                    Wrap(element, output, "~~");
                    break;
                case "code":
                    InlineCode(element, output);
                    break;
                case "pre":
                    CodeBlock(element, output);
                    break;
                case "blockquote":
                    Quote(element, output);
                    break;
                case "ul" or "ol":
                    List(element, output, inListItem);
                    break;
                case "table":
                    Table(element, output);
                    break;
                case "a":
                    Anchor(element, output);
                    break;
                case "img":
                    Image(element, output);
                    break;
                default:
                    RenderChildren(element, output, inListItem);
                    break;
            }
        }

        private void Heading(IElement element, Output output)
        {
            var title = Whitespace().Replace(InlineText(element), " ").Trim();
            if (title.Length == 0)
            {
                return;
            }

            output.Hard();
            output.Raw(new string('#', element.LocalName[1] - '0') + " " + title);
            output.Hard();
        }

        /// <summary>強調的記號要貼著字：前後的空白放到記號外面，不然 Markdown 不認得。</summary>
        private void Wrap(IElement element, Output output, string marker)
        {
            var inner = InlineText(element);
            var core = inner.Trim();
            if (core.Length == 0)
            {
                output.Inline(inner);
                return;
            }

            var lead = inner[..inner.IndexOf(core[0])];
            var trail = inner[(inner.LastIndexOf(core[^1]) + 1)..];
            output.Inline(lead + marker + core + marker + trail);
        }

        private static void InlineCode(IElement element, Output output)
        {
            var code = MarkdownText.StripInvisible(element.TextContent).Replace('\n', ' ');
            if (code.Trim().Length == 0)
            {
                return;
            }

            // 圍欄的反引號比內文裡最長的一串多一個：內文逃不出去（例如偽造提及）。
            var fence = new string('`', MarkdownText.LongestBacktickRun(code) + 1);
            var pad = code.Contains('`', StringComparison.Ordinal) || code.StartsWith(' ') || code.EndsWith(' ') ? " " : string.Empty;
            output.Inline(fence + pad + code + pad + fence);
        }

        private static void CodeBlock(IElement element, Output output)
        {
            var code = MarkdownText.StripInvisible(element.TextContent).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n', ' ');
            if (code.Trim().Length == 0)
            {
                return;
            }

            var fence = new string('`', Math.Max(3, MarkdownText.LongestBacktickRun(code) + 1));
            output.Hard();
            output.Raw($"{fence}\n{code}\n{fence}");
            output.Hard();
        }

        private void Quote(IElement element, Output output)
        {
            var inner = MarkdownText.Tidy(Block(element, startsLine: true, inListItem: false));
            if (inner.Length == 0)
            {
                return;
            }

            output.Hard();
            output.Raw(string.Join('\n', inner.Split('\n').Select(line => line.Length == 0 ? ">" : "> " + line)));
            output.Hard();
        }

        private void List(IElement list, Output output, bool inListItem)
        {
            var ordered = list.LocalName == "ol";
            var number = ordered && int.TryParse(list.GetAttribute("start"), NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : 1;
            var lines = new List<string>();
            var lastIndent = 0;
            foreach (var item in list.Children)
            {
                if (item.LocalName is "ul" or "ol")
                {
                    // <ul> 直接包 <ul>（沒有 <li>）：當成上一個項目的巢狀內容；沒有上一個時就是獨立的清單。
                    lines.AddRange(NestedList(item, lastIndent));
                    continue;
                }

                if (item.LocalName != "li")
                {
                    continue;
                }

                var content = MarkdownText.Tidy(Block(item, startsLine: true, inListItem: true));
                if (content.Length == 0)
                {
                    continue;
                }

                // 縮排跟標記一樣寬：巢狀的內容才會屬於這個清單項目（無序是兩格，有序的「1. 」是三格）。
                var marker = ordered ? $"{number++}. " : "- ";
                var indent = new string(' ', marker.Length);
                lastIndent = marker.Length;
                var rows = content.Split('\n');
                lines.Add(marker + rows[0]);
                lines.AddRange(rows.Skip(1).Select(row => row.Length == 0 ? row : indent + row));
            }

            if (lines.Count == 0)
            {
                return;
            }

            // 清單項目裡的巢狀清單緊接在文字後面（只換行），外面的清單前後空一行。
            if (inListItem)
            {
                output.Soft();
            }
            else
            {
                output.Hard();
            }

            output.Raw(string.Join('\n', lines));
            if (inListItem)
            {
                output.Soft();
            }
            else
            {
                output.Hard();
            }
        }

        private IEnumerable<string> NestedList(IElement list, int indent)
        {
            var nested = new Output(startsLine: true);
            List(list, nested, inListItem: true);
            var text = MarkdownText.Tidy(nested.ToString());
            return text.Length == 0
                ? []
                : text.Split('\n').Select(row => row.Length == 0 ? row : new string(' ', indent) + row);
        }

        private void Table(IElement table, Output output)
        {
            var rows = table.QuerySelectorAll("tr")
                .Select(row => string.Join(" | ", row.Children
                    .Where(cell => cell.LocalName is "td" or "th")
                    .Select((cell, index) => Whitespace().Replace(InlineText(cell, startsLine: index == 0), " ").Trim())))
                .Where(row => row.Replace("|", string.Empty, StringComparison.Ordinal).Trim().Length > 0)
                .ToArray();
            if (rows.Length == 0)
            {
                return;
            }

            output.Hard();
            output.Raw(string.Join('\n', rows));
            output.Hard();
        }

        private void Anchor(IElement anchor, Output output)
        {
            if (anchor.ClassList.Contains(MentionClass))
            {
                Mention(anchor, output);
                return;
            }

            var href = anchor.GetAttribute("href")?.Trim();
            var allowed = href is not null && (IsWebUrl(href) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                RenderChildren(anchor, output, inListItem: false);
                return;
            }

            var text = Whitespace().Replace(InlineText(anchor), " ").Trim();
            var plain = MarkdownText.StripInvisible(anchor.TextContent).Trim();
            if (text.Length == 0 || (plain == href && IsWebUrl(href)))
            {
                // <網址> 寫法：網頁的自動連結不認得網址在哪裡結束，會把後面緊接的中文也吃進去。
                output.Inline($"<{MarkdownText.EncodeDestination(href!)}>");
                return;
            }

            output.Inline($"[{text}]({MarkdownText.EncodeDestination(href!)})");
        }

        private void Mention(IElement anchor, Output output)
        {
            var name = MarkdownText.StripInvisible(anchor.TextContent).Trim().TrimStart('@').Trim();
            var isUser = string.Equals(anchor.GetAttribute("data-mention-type") ?? "User", "User", StringComparison.OrdinalIgnoreCase);
            var id = anchor.GetAttribute("data-mention-id")?.Trim();
            if (name.Length == 0)
            {
                name = "未知的人";
            }

            var target = isUser && !string.IsNullOrEmpty(id) ? context.ResolveUser(id) : null;
            output.Inline(target is null
                ? "@" + MarkdownText.EscapeInline(name, atLineStart: false)
                : $"@[{MarkdownText.EscapeBrackets(target.DisplayName)}](mention:{target.PersonId})");
        }

        private void Image(IElement image, Output output)
        {
            var assetId = image.GetAttribute("data-asset_id")?.Trim();
            if (!string.IsNullOrEmpty(assetId))
            {
                referenced.Add(assetId);
                var target = context.ResolveAsset(assetId)
                    ?? new ImageTarget(MarkdownText.StripInvisible(image.GetAttribute("alt") ?? string.Empty).Trim(), null);
                output.Inline(ImageMarkdown(target));
                return;
            }

            var source = image.GetAttribute("src")?.Trim();
            if (source is not null && IsWebUrl(source))
            {
                output.Inline($"![]({MarkdownText.EncodeDestination(source)})");
            }
        }

        private static bool IsWebUrl(string url) =>
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>圖片搬成附件之後的寫法；沒有搬過來就說明（「見 monday 原連結」）。</summary>
    public static string ImageMarkdown(ImageTarget target)
    {
        var cleaned = MarkdownText.StripInvisible(target.Name);
        var name = cleaned.Length == 0 ? UnknownImageName : cleaned;
        return target.AttachmentId is { } id
            ? $"![{MarkdownText.EscapeBrackets(name)}](attachment:{id})"
            : $"［圖片：{MarkdownText.EscapeInline(name, atLineStart: false)}，沒有搬過來，見 monday 原連結］";
    }

    /// <summary>沒有被 img 引用的檔案（PDF、Excel…）接在留言最後面的寫法。</summary>
    public static string FileMarkdown(string rawName, Guid? attachmentId, bool isImage)
    {
        var name = MarkdownText.StripInvisible(rawName);
        if (isImage)
        {
            return ImageMarkdown(new ImageTarget(name, attachmentId));
        }

        return attachmentId is { } id
            ? $"[{MarkdownText.EscapeBrackets(name)}](attachment:{id})"
            : $"［檔案：{MarkdownText.EscapeInline(name, atLineStart: false)}，沒有搬過來，見 monday 原連結］";
    }
}
