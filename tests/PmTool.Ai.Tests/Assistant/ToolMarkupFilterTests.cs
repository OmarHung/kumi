using PmTool.Ai.Assistant.Transcript;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>
/// 模型偶爾把工具呼叫的標記當成文字吐出來（同時呼叫好幾個工具時，後面幾個變成 <c>&lt;invoke …&gt;</c> 的文字）：
/// 過濾器拿掉 Kumi 自己的工具的標記，其他的文字（含一般的角括號）照原樣，文字被切成幾段送來也一樣。
/// </summary>
[TestFixture]
public sealed class ToolMarkupFilterTests
{
    // 2026-11-26 本機代理（Claude Code）實際吐出來的文字：第一個工具呼叫正常，後面兩個變成文字。
    private const string ObservedLeak =
        "</invoke> <invoke name=\"mcp__kumi__get_item\"> <parameter name=\"itemKey\">APP-5</parameter> </invoke> "
        + "<invoke name=\"mcp__kumi__get_blueprint\"> <parameter name=\"projectKey\">APP</parameter> </invoke>";

    private static string Run(IEnumerable<string> chunks)
    {
        var filter = new ToolMarkupFilter();
        var output = string.Concat(chunks.Select(filter.Push));
        return output + filter.Flush();
    }

    private static string RunWhole(string text) => Run([text]);

    private static string RunCharByChar(string text) => Run(text.Select(c => c.ToString()));

    [Test]
    public void Push_PlainText_PassesThroughAtOnce()
    {
        // Arrange
        var filter = new ToolMarkupFilter();

        // Act
        var output = filter.Push("我先讀設定。");

        // Assert：沒有角括號的文字不必等，串流照樣即時。
        Assert.That(output, Is.EqualTo("我先讀設定。"));
    }

    [Test]
    public void Filter_ObservedLeak_RemovesEverything()
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RunWhole(ObservedLeak), Is.Empty);
            Assert.That(RunCharByChar(ObservedLeak), Is.Empty, "串流把標記切成一個字一個字送來也要認得出來。");
        }
    }

    [Test]
    public void Filter_TextAroundTheMarkup_IsKept()
    {
        // Arrange
        const string text = "我先讀這兩個項目。<invoke name=\"get_item\"><parameter name=\"itemKey\">APP-5</parameter></invoke>\n接著看設定。";

        // Act & Assert：標記後面緊接的空白一起拿掉，前後的話接起來。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RunWhole(text), Is.EqualTo("我先讀這兩個項目。接著看設定。"));
            Assert.That(RunCharByChar(text), Is.EqualTo("我先讀這兩個項目。接著看設定。"));
        }
    }

    [Test]
    public void Filter_FunctionCallsWrapperAndNamespacedTags_AreRemoved()
    {
        // Arrange
        const string text = "<function_calls>\n<x:invoke name=\"propose_item_changes\">"
            + "<x:parameter name=\"itemKey\">APP-4</x:parameter></x:invoke>\n</function_calls>好了。";

        // Act & Assert
        Assert.That(RunCharByChar(text), Is.EqualTo("好了。"));
    }

    [Test]
    public void Filter_InvokeOfAToolKumiDoesNotHave_IsKept()
    {
        // Arrange：只認 Kumi 自己的工具，使用者問的、或模型寫來解釋的其他 XML 不動。
        const string text = "範例：<invoke name=\"Read\"><parameter name=\"path\">a.txt</parameter></invoke>";

        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RunWhole(text), Is.EqualTo(text));
            Assert.That(RunCharByChar(text), Is.EqualTo(text));
        }
    }

    [Test]
    public void Filter_OrdinaryAngleBrackets_AreKept()
    {
        // Arrange
        const string text = "估計 < 5 的項目，用 <br> 換行，x<y 時、<b>粗體</b> 與 <invokes> 都照原樣。";

        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RunWhole(text), Is.EqualTo(text));
            Assert.That(RunCharByChar(text), Is.EqualTo(text));
        }
    }

    [Test]
    public void Flush_UnfinishedTagThatTurnedOutToBeText_IsReturnedAsIs()
    {
        // Arrange
        var filter = new ToolMarkupFilter();

        // Act
        var pushed = filter.Push("比較 a <inv");
        var flushed = filter.Flush();

        // Assert：可能是標記的開頭時先留著，結束了還不是標記就照原樣交出去。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pushed, Is.EqualTo("比較 a "));
            Assert.That(flushed, Is.EqualTo("<inv"));
        }
    }

    [Test]
    public void Flush_UnclosedInvokeOfAKumiTool_IsDropped()
    {
        // Arrange
        var filter = new ToolMarkupFilter();

        // Act
        var pushed = filter.Push("好的。<invoke name=\"mcp__kumi__get_item\"><parameter name=\"itemKey\">APP");
        var flushed = filter.Flush();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pushed, Is.EqualTo("好的。"));
            Assert.That(flushed, Is.Empty);
        }
    }

    [Test]
    public void Push_KumiInvokeThatNeverCloses_GivesUpAfterTheLimitAndLetsTheTextThrough()
    {
        // Arrange：開頭看得出是 Kumi 的工具，可是一直沒有 </invoke>，後面是真正的回答。
        var filter = new ToolMarkupFilter();
        var answer = new string('答', ToolMarkupFilter.MaxHeldLength + 10);

        // Act
        var output = filter.Push("<invoke name=\"get_item\">" + answer);

        // Assert：不會一直留著（串流卡住）、也不會整段丟掉，只拿掉開頭的標籤。
        Assert.That(output, Is.EqualTo(answer));
    }

    [Test]
    public void Filter_BlankLineAfterTheMarkup_IsKept()
    {
        // Arrange
        const string text = "第一段\n<invoke name=\"get_item\"><parameter name=\"itemKey\">APP-5</parameter></invoke>\n\n第二段";

        // Act & Assert：只吃掉標記後面的空格與一個換行，段落之間的空行還在。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RunWhole(text), Is.EqualTo("第一段\n\n第二段"));
            Assert.That(RunCharByChar(text), Is.EqualTo("第一段\n\n第二段"));
        }
    }

    [Test]
    public void Filter_SingleQuotedName_IsRecognized()
    {
        // Act & Assert
        Assert.That(RunCharByChar("<invoke name='get_blueprint'><parameter name='projectKey'>APP</parameter></invoke>好。"), Is.EqualTo("好。"));
    }

    [Test]
    public void Filter_SelfClosingInvokeOfAnotherTool_DoesNotProtectALaterStrayClose()
    {
        // Arrange：自己結尾的 <invoke …/> 沒有對應的 </invoke>，後面零散的那個照樣拿掉。
        const string text = "<invoke name=\"Read\"/>說明</invoke>";

        // Act & Assert
        Assert.That(RunWhole(text), Is.EqualTo("<invoke name=\"Read\"/>說明"));
    }

    [Test]
    public void Flush_StartsAFreshRun_ForTheTextAfterATool()
    {
        // Arrange
        var filter = new ToolMarkupFilter();
        filter.Push("</invoke>");
        filter.Flush();

        // Act：前一段結尾拿掉了標記，下一段開頭的空白是新的一段的，要留著。
        var output = filter.Push("\n\n讀好了。");

        // Assert
        Assert.That(output, Is.EqualTo("\n\n讀好了。"));
    }
}
