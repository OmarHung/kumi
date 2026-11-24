using PmTool.Cli.Import.Monday.Run;

namespace PmTool.Cli.Tests;

/// <summary>monday 留言 HTML → Markdown（交接第 6.4 節）：每一條規則一個測試。</summary>
[TestFixture]
public sealed class MondayHtmlTests
{
    private static readonly Guid DevId = Guid.Parse("0190a000-0000-7000-8000-000000000001");
    private static readonly Guid ImageId = Guid.Parse("0190a000-0000-7000-8000-0000000000a1");

    private static HtmlContext Context() => new(
        user => user == "1002" ? new MentionTarget(DevId, "Example Dev") : null,
        asset => asset switch
        {
            "501" => new ImageTarget("shot.png", ImageId),
            "502" => new ImageTarget("big.png", null),
            _ => null,
        });

    private static string Convert(string html) => MondayHtml.ToMarkdown(html, Context()).Text;

    [Test]
    public void Paragraphs_AreSeparatedByABlankLine_AndBreaksAreSingleNewlines()
    {
        Assert.That(Convert("<p>第一段<br>換行</p><p>第二段</p><div>第三段</div>"), Is.EqualTo("第一段\n換行\n\n第二段\n\n第三段"));
    }

    [Test]
    public void InvisibleCharacters_AreRemoved_AndBlankRunsCollapsed()
    {
        // Arrange
        var html = "<p>\uFEFFa\u200Bb\u200Cc\u200Dd\u2060e</p><p>\uFEFF</p><p></p><p>f</p><br><br><br><br><p>g</p>";

        // Act
        var markdown = Convert(html);

        // Assert
        Assert.That(markdown, Is.EqualTo("abcde\n\nf\n\ng"));
    }

    [Test]
    public void Whitespace_IsCollapsed_AndNonBreakingSpacesBecomeSpaces()
    {
        Assert.That(Convert("<p>  a \n  b&nbsp;&nbsp;c  </p>"), Is.EqualTo("a b c"));
    }

    [Test]
    public void Mention_OfAMatchedUser_BecomesAMentionLink()
    {
        Assert.That(
            Convert("<p>請 <a class=\"user_mention_editor router\" data-mention-id=\"1002\" data-mention-type=\"User\">@Dev</a> 看一下</p>"),
            Is.EqualTo($"請 @[Example Dev](mention:{DevId}) 看一下"));
    }

    [Test]
    public void Mention_OfAnUnmatchedUserOrATeam_IsPlainAtName()
    {
        // Arrange
        var html = "<p><a class=\"user_mention_editor\" data-mention-id=\"9\" data-mention-type=\"User\">@Former</a> "
            + "<a class=\"user_mention_editor\" data-mention-id=\"1002\" data-mention-type=\"Team\">@Backend</a></p>";

        // Act + Assert
        Assert.That(Convert(html), Is.EqualTo("@Former @Backend"));
    }

    [Test]
    public void Link_WithText_IsAMarkdownLink_AndWithTheUrlAsTextIsAnAngleBracketUrl()
    {
        Assert.That(
            Convert("<p><a href=\"https://example.com/a b(1)\">文件</a> <a href=\"https://example.com/x\">https://example.com/x</a></p>"),
            Is.EqualTo("[文件](https://example.com/a%20b%281%29) <https://example.com/x>"));
    }

    [Test]
    public void Link_WithOtherSchemes_KeepsOnlyTheText_AndMailtoIsKept()
    {
        Assert.That(
            Convert("<p><a href=\"javascript:alert(1)\">壞的</a> <a href=\"file:///etc/passwd\">檔案</a> <a href=\"mailto:dev@example.com\">寫信</a> <a>沒有網址</a></p>"),
            Is.EqualTo("壞的 檔案 [寫信](mailto:dev@example.com) 沒有網址"));
    }

    [Test]
    public void Image_WithAMovedAsset_PointsAtTheAttachment_AndIsReportedAsReferenced()
    {
        // Act
        var result = MondayHtml.ToMarkdown("<p><img data-asset_id=\"501\" src=\"https://files.example.test/x?sig=1\"></p>", Context());

        // Assert
        Assert.That(result.Text, Is.EqualTo($"![shot.png](attachment:{ImageId})"));
        Assert.That(result.ReferencedAssetIds, Is.EquivalentTo(new[] { "501" }));
    }

    [Test]
    public void Image_WithAnAssetThatWasNotMoved_SaysSoInsteadOfLoadingIt()
    {
        Assert.That(
            Convert("<p><img data-asset_id=\"502\"><img data-asset_id=\"999\" alt=\"舊圖\"></p>"),
            Is.EqualTo("［圖片：big.png，沒有搬過來，見 monday 原連結］［圖片：舊圖，沒有搬過來，見 monday 原連結］"));
    }

    [Test]
    public void Image_WithoutAnAssetId_IsAnEmptyAltImage_OnlyForWebUrls()
    {
        Assert.That(
            Convert("<p><img src=\"https://example.com/p.png\"><img src=\"data:image/png;base64,AAAA\"><img></p>"),
            Is.EqualTo("![](https://example.com/p.png)"));
    }

    [Test]
    public void InlineFormatting_UsesMarkdownMarkers_AndKeepsSpacesOutsideTheMarkers()
    {
        Assert.That(
            Convert("<p><strong>粗</strong> <b> 粗2 </b>x<em>斜</em><i>斜2</i> <s>刪</s><del>刪2</del> <code>a`b</code> <code>x</code></p>"),
            Is.EqualTo("**粗** **粗2** x*斜**斜2* ~~刪~~~~刪2~~ `` a`b `` `x`"));
    }

    [Test]
    public void Formatting_WithNoContent_IsDropped()
    {
        Assert.That(Convert("<p>a<strong> </strong><code></code>b</p>"), Is.EqualTo("a b"));
    }

    [Test]
    public void CodeBlock_KeepsTheTextAsIs_AndUsesALongerFenceWhenTheTextHasOne()
    {
        Assert.That(Convert("<pre>line *1*\n  line 2\n</pre>"), Is.EqualTo("```\nline *1*\n  line 2\n```"));
        Assert.That(Convert("<pre>```\nx\n```</pre>"), Is.EqualTo("````\n```\nx\n```\n````"));
    }

    [Test]
    public void Blockquote_PrefixesEveryLine()
    {
        Assert.That(Convert("<blockquote><p>引用一</p><p>引用二</p></blockquote><p>後面</p>"), Is.EqualTo("> 引用一\n>\n> 引用二\n\n後面"));
    }

    [Test]
    public void Headings_BecomeHashes()
    {
        Assert.That(Convert("<h1>大標</h1><h3> 小 <b>標</b></h3><h2></h2>"), Is.EqualTo("# 大標\n\n### 小 **標**"));
    }

    [Test]
    public void Lists_UseMarkers_AndNestedListsAreIndentedByTheMarkerWidth()
    {
        // Arrange
        var html = "<ul><li>甲<ul><li>甲一</li><li>甲二</li></ul></li><li>乙</li></ul>"
            + "<ol start=\"3\"><li>丙<ul><li>丙一</li></ul></li><li></li><li>丁</li></ol>";

        // Act
        var markdown = Convert(html);

        // Assert
        Assert.That(markdown, Is.EqualTo("- 甲\n  - 甲一\n  - 甲二\n- 乙\n\n3. 丙\n   - 丙一\n4. 丁"));
    }

    [Test]
    public void ListItem_WithSeveralParagraphs_IndentsTheContinuation()
    {
        Assert.That(Convert("<ul><li><p>一</p><p>二</p></li></ul>"), Is.EqualTo("- 一\n\n  二"));
    }

    [Test]
    public void Table_BecomesOneLinePerRow_JoinedWithBars()
    {
        Assert.That(
            Convert("<table><thead><tr><th>項目</th><th>時數</th></tr></thead><tbody><tr><td>甲</td><td>2</td></tr><tr><td></td><td></td></tr></tbody></table>"),
            Is.EqualTo("項目 | 時數\n甲 | 2"));
    }

    [Test]
    public void UnknownTags_KeepOnlyTheirContent_AndScriptsAreDropped()
    {
        Assert.That(Convert("<p><span style=\"color:red\">紅</span><u>底線</u><font>字</font><script>alert(1)</script></p>"), Is.EqualTo("紅底線字"));
    }

    [Test]
    public void Text_EscapesMarkdownSymbols()
    {
        Assert.That(
            Convert("<p>a*b* _c_ `d` [e] \\f &lt;g&gt; ~h~ &amp;copy; 5 * 3</p>"),
            Is.EqualTo("a\\*b\\* \\_c\\_ \\`d\\` \\[e\\] \\\\f \\<g> \\~h\\~ \\&copy; 5 \\* 3"));
    }

    [Test]
    public void Text_EscapesLineStartMarkers_OnlyAtTheStartOfALine()
    {
        Assert.That(
            Convert("<p># 不是標題<br>&gt; 不是引用<br>- 不是清單<br>+ 也不是<br>1. 不是有序<br>12) 也不是<br>a - b # c > d 1. e</p>"),
            Is.EqualTo("\\# 不是標題\n\\> 不是引用\n\\- 不是清單\n\\+ 也不是\n1\\. 不是有序\n12\\) 也不是\na - b # c > d 1. e"));
    }

    [Test]
    public void Text_AtTheStartOfAListItem_IsEscapedToo()
    {
        Assert.That(Convert("<ul><li>- 減號</li></ul>"), Is.EqualTo("- \\- 減號"));
    }

    [Test]
    public void EmptyOrBlankHtml_GivesAnEmptyResult()
    {
        Assert.That(MondayHtml.ToMarkdown(null, Context()).Text, Is.Empty);
        Assert.That(MondayHtml.ToMarkdown("  ", Context()).Text, Is.Empty);
        Assert.That(Convert("<p>\uFEFF</p>"), Is.Empty);
    }

    [Test]
    public void Truncate_ShortText_IsUnchanged_AndLongTextEndsWithTheNotice()
    {
        // Arrange
        var text = new string('字', 10_001);

        // Act
        var cut = MarkdownText.Truncate(text, out var truncated);

        // Assert
        Assert.That(MarkdownText.Truncate("短", out var shortTruncated), Is.EqualTo("短"));
        Assert.That(shortTruncated, Is.False);
        Assert.That(truncated, Is.True);
        Assert.That(cut, Is.EqualTo(new string('字', 9_800) + "\n\n" + MarkdownText.TruncationNotice));
        Assert.That(cut.Length, Is.LessThan(MarkdownText.MaxCommentLength));
    }

    [Test]
    public void Truncate_DoesNotCutInTheMiddleOfALink()
    {
        // Arrange
        var link = "[" + new string('文', 100) + "](https://example.com/" + new string('a', 100) + ")";
        var text = new string('字', 9_760) + link + new string('尾', 500);

        // Act
        var cut = MarkdownText.Truncate(text, out _);

        // Assert
        Assert.That(cut, Is.EqualTo(new string('字', 9_760) + "\n\n" + MarkdownText.TruncationNotice));
    }

    [Test]
    public void Truncate_DoesNotLeaveALoneBackslashOrHalfASurrogatePair()
    {
        // Arrange
        var afterBackslash = new string('字', 9_799) + "\\*" + new string('尾', 300);
        var inEmoji = new string('字', 9_799) + "😀" + new string('尾', 300);

        // Act + Assert
        Assert.That(MarkdownText.Truncate(afterBackslash, out _), Does.StartWith(new string('字', 9_799) + "\n\n"));
        Assert.That(MarkdownText.Truncate(inEmoji, out _), Does.StartWith(new string('字', 9_799) + "\n\n"));
    }

    [Test]
    public void FileMarkdown_WritesImagesAndFilesAndTheNotMovedNotice()
    {
        var id = Guid.Parse("0190a000-0000-7000-8000-0000000000b2");
        Assert.That(MondayHtml.FileMarkdown("a[1].png", id, isImage: true), Is.EqualTo($"![a\\[1\\].png](attachment:{id})"));
        Assert.That(MondayHtml.FileMarkdown("spec.pdf", id, isImage: false), Is.EqualTo($"[spec.pdf](attachment:{id})"));
        Assert.That(MondayHtml.FileMarkdown("spec.pdf", null, isImage: false), Is.EqualTo("［檔案：spec.pdf，沒有搬過來，見 monday 原連結］"));
        Assert.That(MondayHtml.FileMarkdown("p.png", null, isImage: true), Is.EqualTo("［圖片：p.png，沒有搬過來，見 monday 原連結］"));
    }
}
