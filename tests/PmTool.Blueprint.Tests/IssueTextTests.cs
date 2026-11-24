namespace PmTool.Blueprint.Tests;

[TestFixture]
public sealed class IssueTextTests
{
    [Test]
    public void Quote_ShortText_WrapsItInCornerBrackets()
    {
        // Arrange
        const string key = "quote_amount";

        // Act
        var quoted = IssueText.Quote(key);

        // Assert
        Assert.That(quoted, Is.EqualTo("「quote_amount」"));
    }

    [Test]
    public void Quote_TextExactlyAtTheLimit_KeepsAllOfIt()
    {
        // Arrange
        var text = new string('a', IssueText.MaxQuotedLength);

        // Act
        var quoted = IssueText.Quote(text);

        // Assert
        Assert.That(quoted, Is.EqualTo($"「{text}」"));
    }

    [Test]
    public void Quote_TextOverTheLimit_CutsItAndMarksTheCut()
    {
        // Arrange
        var text = new string('a', IssueText.MaxQuotedLength) + "bcd";

        // Act
        var quoted = IssueText.Quote(text);

        // Assert
        Assert.That(quoted, Is.EqualTo($"「{new string('a', IssueText.MaxQuotedLength)}…」"));
    }

    [Test]
    public void Quote_VeryLongText_StaysSmallNoMatterHowLongTheInputIs()
    {
        // Arrange
        var text = new string('x', 262_144);

        // Act
        var quoted = IssueText.Quote(text);

        // Assert
        Assert.That(quoted, Has.Length.EqualTo(IssueText.MaxQuotedLength + 3));
    }

    [Test]
    public void Quote_CutLandsInsideASurrogatePair_DropsTheWholePair()
    {
        // Arrange：表情符號佔兩個 char，讓切點剛好落在它的中間。
        var prefix = new string('a', IssueText.MaxQuotedLength - 1);
        var text = prefix + "😀" + "tail";

        // Act
        var quoted = IssueText.Quote(text);

        // Assert
        Assert.That(quoted, Is.EqualTo($"「{prefix}…」"));
    }

    [TestCase(0x1B, TestName = "Quote_EscapeCharacter_IsReplaced")]
    [TestCase(0x0A, TestName = "Quote_LineFeed_IsReplaced")]
    [TestCase(0x00, TestName = "Quote_Nul_IsReplaced")]
    [TestCase(0x7F, TestName = "Quote_Delete_IsReplaced")]
    [TestCase(0x202E, TestName = "Quote_RightToLeftOverride_IsReplaced")]
    [TestCase(0x2066, TestName = "Quote_LeftToRightIsolate_IsReplaced")]
    [TestCase(0x200B, TestName = "Quote_ZeroWidthSpace_IsReplaced")]
    [TestCase(0x2028, TestName = "Quote_LineSeparator_IsReplaced")]
    public void Quote_CharacterThatCanRewriteWhatTheReaderSees_IsReplaced(int codeUnit)
    {
        // Arrange：訊息會被印在終端機或畫面上。跳脫字元能改寫終端機的內容，方向控制字元能讓文字倒著顯示；
        // 文件來自使用者或語言模型，這些字元不能原樣帶進訊息。字元用數值寫，原始檔裡才不會出現看不見的字元。
        var text = "ab" + (char)codeUnit + "cd";

        // Act
        var quoted = IssueText.Quote(text);

        // Assert
        Assert.That(quoted, Is.EqualTo("「ab" + (char)0xFFFD + "cd」"));
    }

    [Test]
    public void Quote_OrdinaryTextInAnyScript_IsKeptAsIs()
    {
        // Arrange：空白、標點、中文、表情符號都是一般的文字，不該被動到。
        const string text = "報價 金額-v2 (quote_amount) 😀";

        // Act
        var quoted = IssueText.Quote(text);

        // Assert
        Assert.That(quoted, Is.EqualTo($"「{text}」"));
    }

    [TestCase(null)]
    [TestCase("")]
    public void Quote_NullOrEmpty_ReturnsEmptyBrackets(string? text)
    {
        // Act
        var quoted = IssueText.Quote(text);

        // Assert
        Assert.That(quoted, Is.EqualTo("「」"));
    }
}
