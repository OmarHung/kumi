using System.Text;
using PmTool.Ai.Assistant.Transcript;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>
/// 回覆的格式限定：模型把給使用者看的文字放在單獨一行的 <c>[REPLY]</c> 之後，之前混進來的推理與控制標記的碎片一律丟掉。
/// 樣本是 2026-11-26 在 Claude Code 上實測拿到的原文（答案截短，標記前後的結構照原樣；當時試的標記是另一個字串，這裡換成現在的）。
/// </summary>
[TestFixture]
public sealed class ReplyMarkerFilterTests
{
    private const string Mark = ReplyMarkerFilter.Marker;

    private static IEnumerable<TestCaseData> RealSamples()
    {
        yield return new TestCaseData(
            "Off-topic: investment. One sentence decline plus what I can help with.\n\n[REPLY]\n投資理財超出我能幫的範圍；我可以幫你調整專案的設定。",
            "投資理財超出我能幫的範圍；我可以幫你調整專案的設定。").SetName("一個標記");
        yield return new TestCaseData(
            "內容】\n[REPLY]\n投資操作超出我能幫的範圍。我可以幫你整理這個專案的項目。",
            "投資操作超出我能幫的範圍。我可以幫你整理這個專案的項目。").SetName("開頭是殘缺的「內容】」");
        yield return new TestCaseData(
            "Agile concept question, in scope. Answer briefly, with [REPLY] header.[REPLY]\n故事點數估的是**相對大小**，不是時數。\n\n- **先定基準**：挑一件大家都熟的故事。",
            "故事點數估的是**相對大小**，不是時數。\n\n- **先定基準**：挑一件大家都熟的故事。").SetName("推理自己提到標記（後面接空格和文字）");
        yield return new TestCaseData(
            ". Agile concept question — in scope. Answer briefly in Traditional Chinese, starting with [REPLY].[REPLY]\n**估故事點數的幾個要點：**\n\n- **估相對大小，不估時間**：先挑基準。",
            "**估故事點數的幾個要點：**\n\n- **估相對大小，不估時間**：先挑基準。").SetName("推理自己提到標記（後面接句點）");
        yield return new TestCaseData(
            "Agile concept question, in scope. Answer briefly, with [REPLY] format. Could mention Kumi uses estimate in hours per reference; note that.\n\n[REPLY]\n**故事點數怎麼估：**\n\n- **估相對大小，不估時間**：先挑基準。",
            "**故事點數怎麼估：**\n\n- **估相對大小，不估時間**：先挑基準。").SetName("推理提到標記、後面還有一整段");
        yield return new TestCaseData(
            "Agile concept question; work-related, answer briefly. Format: start with [REPLY].\n\n[REPLY]\n幾個實用的原則：\n\n- **估相對大小，不估時間**：挑基準。",
            "幾個實用的原則：\n\n- **估相對大小，不估時間**：挑基準。").SetName("推理提到標記、句點接換行");
    }

    private static string Run(IEnumerable<string> deltas, bool isLast = true)
    {
        var filter = new ReplyMarkerFilter();
        return Run(filter, deltas, isLast);
    }

    private static string Run(ReplyMarkerFilter filter, IEnumerable<string> deltas, bool isLast = true)
    {
        var output = new StringBuilder();
        foreach (var delta in deltas)
        {
            output.Append(filter.Push(delta));
        }

        return output.Append(filter.EndSegment(isLast).Output).ToString();
    }

    private static IEnumerable<string> Chars(string text) => text.Select(c => c.ToString());

    [TestCaseSource(nameof(RealSamples))]
    public void Filter_RealSamples_KeepOnlyTheAnswerAfterTheRealMarker(string raw, string expected)
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Run([raw]), Is.EqualTo(expected));
            Assert.That(Run(Chars(raw)), Is.EqualTo(expected), "一個字元一個 delta，標記被切在任何位置都要認得出來。");
        }
    }

    [Test]
    public void Push_AfterTheMarker_TextGoesOutAtOnce()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();

        // Act
        var before = filter.Push("想一想。\n\n[REPLY]\n");
        var first = filter.Push("第一句，");
        var second = filter.Push("第二句。");

        // Assert：串流不卡在過濾器裡。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.Empty);
            Assert.That(first, Is.EqualTo("第一句，"));
            Assert.That(second, Is.EqualTo("第二句。"));
            Assert.That(filter.SawMarker, Is.True);
        }
    }

    [Test]
    public void Filter_MarkerFollowedByCrLf_SkipsTheWholeLineBreak()
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Run(["推理\r\n[REPLY]\r\n答案"]), Is.EqualTo("答案"));
            Assert.That(Run(Chars("推理\r\n[REPLY]\r\n答案")), Is.EqualTo("答案"));
        }
    }

    [Test]
    public void Filter_SpacesTabsAndOneNewlineAfterTheMarker_AreDroppedButASecondNewlineStays()
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Run(["推理\n[REPLY] \t \n\n答案"]), Is.EqualTo("\n答案"));
            Assert.That(Run(Chars("推理\n[REPLY] \t \n\n答案")), Is.EqualTo("\n答案"));
        }
    }

    [Test]
    public void Filter_MarkerAtTheVeryStart_CountsEvenWhenContentFollowsDirectly()
    {
        // Act & Assert：(b) 開頭的標記，後面直接接內容（前面只有空白）。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Run(["[REPLY]答案"]), Is.EqualTo("答案"));
            Assert.That(Run(["  \n[REPLY] 答案"]), Is.EqualTo("答案"));
            Assert.That(Run(Chars("\n[REPLY]答案")), Is.EqualTo("答案"));
        }
    }

    [Test]
    public void Filter_MarkerAtTheEndOfTheSegment_MeansAnEmptyAnswer()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();

        // Act：(c) 標記之後到這一段結束只有空白。
        var output = Run(filter, ["推理一下。\n", "[REPLY]", "  "]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(output, Is.Empty);
            Assert.That(filter.SawMarker, Is.True);
        }
    }

    [Test]
    public void Filter_MarkerMentionedInTheMiddleOfASentence_DoesNotCount()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();
        const string text = "請在 [REPLY] 後面寫答案，不是這裡。";

        // Act
        var output = Run(filter, Chars(text));

        // Assert：沒有算數的標記，最後一段照原樣。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(output, Is.EqualTo(text));
            Assert.That(filter.SawMarker, Is.False);
        }
    }

    [Test]
    public void Filter_MarkerAgainAfterTheRealOne_IsKeptAsText()
    {
        // Act & Assert
        Assert.That(Run(["推理\n[REPLY]\n答案裡提到 [REPLY]\n這個字。"]), Is.EqualTo("答案裡提到 [REPLY]\n這個字。"));
    }

    [Test]
    public void EndSegment_LastSegmentWithoutMarker_GivesTheTextBackAsItWas()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();

        // Act
        var pushed = filter.Push("ba");
        var end = filter.EndSegment(isLast: true);

        // Assert：已知的殘餘情形，模型忘了寫標記時照原樣。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pushed, Is.Empty);
            Assert.That(end.Output, Is.EqualTo("ba"));
            Assert.That(end.Withheld, Is.Null);
            Assert.That(filter.SawMarker, Is.False);
        }
    }

    [Test]
    public void EndSegment_SegmentBeforeAToolWithoutMarker_IsWithheld()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();
        filter.Push("Thinking... ");
        filter.Push("</thinking_mode>");

        // Act
        var end = filter.EndSegment(isLast: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(end.Output, Is.Empty);
            Assert.That(end.Withheld, Is.EqualTo("Thinking... </thinking_mode>"));
            Assert.That(filter.SawMarker, Is.False);
        }
    }

    [Test]
    public void EndSegment_SegmentBeforeAToolWithTheMarker_IsNotWithheld()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();
        var shown = filter.Push("雜訊\n[REPLY]\n我先讀設定。");

        // Act
        var end = filter.EndSegment(isLast: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shown, Is.EqualTo("我先讀設定。"));
            Assert.That(end.Output, Is.Empty);
            Assert.That(end.Withheld, Is.Null);
        }
    }

    [Test]
    public void SawMarker_StaysTrueForTheRestOfTheReplyAndEachSegmentStartsOver()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();
        filter.Push("[REPLY]\n第一段");
        filter.EndSegment(isLast: false);

        // Act：下一段重新判斷，沒有標記就留著。
        filter.Push("第二段沒標記");
        var end = filter.EndSegment(isLast: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.SawMarker, Is.True);
            Assert.That(end.Withheld, Is.EqualTo("第二段沒標記"));
        }
    }

    [Test]
    public void Filter_NoMarkerAfterTheGiveUpLength_HandsEverythingOverAndLetsTheRestThrough()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();
        var early = new string('甲', ReplyMarkerFilter.GiveUpLength);

        // Act
        var heldBack = filter.Push(early);
        var released = filter.Push("乙");
        var later = filter.Push("\n[REPLY]\n之後出現的標記不處理");
        var end = filter.EndSegment(isLast: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(heldBack, Is.Empty, "剛好等於上限還在等。");
            Assert.That(released, Is.EqualTo(early + "乙"));
            Assert.That(later, Is.EqualTo("\n[REPLY]\n之後出現的標記不處理"));
            Assert.That(end.Withheld, Is.Null, "放棄的那一段已經交出去了，不再留著。");
            Assert.That(filter.SawMarker, Is.False, "放棄的這一段算沒有標記。");
        }
    }

    [Test]
    public void Filter_AfterGivingUp_TheNextSegmentIsJudgedAgain()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();
        filter.Push(new string('甲', ReplyMarkerFilter.GiveUpLength + 1));
        filter.EndSegment(isLast: false);

        // Act
        var shown = filter.Push("推理\n[REPLY]\n答案");

        // Assert
        Assert.That(shown, Is.EqualTo("答案"));
    }

    [Test]
    public void Filter_MarkerFoundJustBeforeTheGiveUpLength_StillCounts()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();
        var prefix = new string('甲', ReplyMarkerFilter.GiveUpLength - 20);

        // Act
        var output = Run(filter, Chars(prefix + "\n[REPLY]\n答案"));

        // Assert
        Assert.That(output, Is.EqualTo("答案"));
    }

    [Test]
    public void EndSegment_UnmarkedAnswerThatHappensToEndWithTheMarkerWord_IsKeptWhole()
    {
        // Arrange：沒照格式寫的回答，最後一句剛好提到標記。
        var filter = new ReplyMarkerFilter();
        const string text = "格式是先寫單獨一行的 [REPLY]";

        // Act
        var pushed = filter.Push(text);
        var end = filter.EndSegment(isLast: true);

        // Assert：句尾提到標記不算；算了的話整段回答都會被當成標記之前的東西丟掉。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pushed + end.Output, Is.EqualTo(text));
            Assert.That(filter.SawMarker, Is.False);
        }
    }

    [Test]
    public void EndSegment_MarkerAloneOnTheLastLine_StillCountsAndLeavesAnEmptyAnswer()
    {
        // Arrange
        var filter = new ReplyMarkerFilter();

        // Act
        var pushed = filter.Push("Thinking about it.\n[REPLY]");
        var end = filter.EndSegment(isLast: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pushed + end.Output, Is.Empty);
            Assert.That(filter.SawMarker, Is.True);
        }
    }
}
