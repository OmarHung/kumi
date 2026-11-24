using PmTool.Work.Events;

namespace PmTool.Work.Tests.Events;

/// <summary>通知的摘要是純文字：Markdown 的符號去掉，收件匣才不會露出 <c>**</c> 與 <c>-</c>。</summary>
[TestFixture]
public sealed class ExcerptPlainTextTests
{
    private const string PersonId = "3f2b7a10-0000-4000-8000-000000000001";

    [TestCase("**重點**要先看", "重點要先看")]
    [TestCase("這是 *強調* 與 _斜體_ 與 ~~刪除~~", "這是 強調 與 斜體 與 刪除")]
    [TestCase("# 標題\n內容", "標題 內容")]
    [TestCase("### 小標題", "小標題")]
    [TestCase("- 第一項\n- 第二項", "第一項 第二項")]
    [TestCase("* 星號清單\n+ 加號清單", "星號清單 加號清單")]
    [TestCase("1. 先做\n2) 再做", "先做 再做")]
    [TestCase("- [ ] 待辦\n- [x] 完成", "待辦 完成")]
    [TestCase("> 引用的話\n>> 再引用", "引用的話 再引用")]
    [TestCase("看 [說明文件](https://example.com/docs) 吧", "看 說明文件 吧")]
    [TestCase("圖：![流程圖](https://example.com/a.png)", "圖：流程圖")]
    [TestCase("執行 `dotnet test` 看看", "執行 dotnet test 看看")]
    [TestCase("```\nvar a = 1;\n```", "var a = 1;")]
    [TestCase("<https://example.com>", "https://example.com")]
    [TestCase("第一行\n\n\n   第二行\t\t結尾  ", "第一行 第二行 結尾")]
    [TestCase("---\n文字\n***", "文字")]
    [TestCase("snake_case_name 不是斜體", "snake_case_name 不是斜體")]
    [TestCase("2 * 3 * 4", "2 * 3 * 4")]
    public void Excerpt_MarkdownSyntax_BecomesPlainText(string markdown, string expected)
    {
        // Act
        var excerpt = Mentions.Excerpt(markdown);

        // Assert
        Assert.That(excerpt, Is.EqualTo(expected));
    }

    [Test]
    public void Excerpt_Mention_BecomesAtName()
    {
        // Arrange
        var markdown = $"**請** @[王小明](mention:{PersonId}) 看一下 [連結](https://example.com)";

        // Act
        var excerpt = Mentions.Excerpt(markdown);

        // Assert
        Assert.That(excerpt, Is.EqualTo("請 @王小明 看一下 連結"));
    }

    [Test]
    public void Excerpt_LongText_IsCutAfterTheMarkersAreRemoved()
    {
        // Arrange：每個字都包在粗體裡，去掉符號之後剛好是上限。
        var markdown = string.Concat(Enumerable.Repeat("**字**", WorkEventRules.MaxExcerptLength + 20));

        // Act
        var excerpt = Mentions.Excerpt(markdown);

        // Assert
        Assert.That(excerpt, Is.EqualTo(new string('字', WorkEventRules.MaxExcerptLength)));
    }

    [Test]
    public void Excerpt_CutLandingInTheMiddleOfAnEmoji_DoesNotSplitTheSurrogatePair()
    {
        // Arrange
        var markdown = "**" + new string('字', WorkEventRules.MaxExcerptLength - 1) + "**😀後面還有";

        // Act
        var excerpt = Mentions.Excerpt(markdown);

        // Assert
        Assert.That(excerpt, Is.EqualTo(new string('字', WorkEventRules.MaxExcerptLength - 1)));
    }

    [Test]
    public void Excerpt_HugeInput_FinishesQuickly()
    {
        // Arrange：大量的符號。上限寬一點，只擋住指數或二次方的退化。
        var markdown = string.Concat(Enumerable.Repeat("*_`[![", 20_000));

        // Act
        var started = System.Diagnostics.Stopwatch.StartNew();
        var excerpt = Mentions.Excerpt(markdown);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
            Assert.That(excerpt.Length, Is.LessThanOrEqualTo(WorkEventRules.MaxExcerptLength));
        }
    }
}
