using System.Text;
using PmTool.Work.Projects;

namespace PmTool.Work.Tests.Projects;

/// <summary>專案搜尋的摘錄：第一個比對到的行、去掉行首符號、截斷（internal/HANDOFF-project-info.md 第 6.3 節）。</summary>
[TestFixture]
public sealed class ProjectSnippetTests
{
    [TestCase("# 主機：web01.example.com", "主機：web01.example.com")]
    [TestCase("### 主機：web01.example.com", "主機：web01.example.com")]
    [TestCase("- 主機：web01.example.com", "主機：web01.example.com")]
    [TestCase("* 主機：web01.example.com", "主機：web01.example.com")]
    [TestCase("+ 主機：web01.example.com", "主機：web01.example.com")]
    [TestCase("> 主機：web01.example.com", "主機：web01.example.com")]
    [TestCase("1. 主機：web01.example.com", "主機：web01.example.com")]
    [TestCase("12) 主機：web01.example.com", "主機：web01.example.com")]
    [TestCase("  - > 1. 主機：web01.example.com  ", "主機：web01.example.com")]
    [TestCase("\t- 主機：web01.example.com", "主機：web01.example.com")]
    public void From_LineWithMarkdownPrefix_StripsPrefixAndWhitespace(string line, string expected)
    {
        // Act
        var snippet = ProjectSnippet.From($"## 主機\n{line}\n", "web01");

        // Assert
        Assert.That(snippet, Is.EqualTo(expected));
    }

    [TestCase("**web01** 是正式機")]
    [TestCase("#web01 不是標題")]
    [TestCase("-web01 沒有空白")]
    [TestCase("2026.web01")]
    public void From_SymbolsThatAreNotMarkdownPrefix_AreKept(string line)
    {
        // Act
        var snippet = ProjectSnippet.From(line, "web01");

        // Assert
        Assert.That(snippet, Is.EqualTo(line));
    }

    [Test]
    public void From_MatchOnLaterLine_ReturnsOnlyTheFirstMatchingLine()
    {
        // Arrange
        var text = "## 網址\n- 正式：https://www.example.com\n- 測試：https://test.example.com\n- 主機：web01.example.com";

        // Act
        var snippet = ProjectSnippet.From(text, "EXAMPLE.com");

        // Assert
        Assert.That(snippet, Is.EqualTo("正式：https://www.example.com"));
    }

    [Test]
    public void From_CrLfText_DoesNotLeakCarriageReturn()
    {
        // Act
        var snippet = ProjectSnippet.From("a\r\n- web01\r\nc", "web01");

        // Assert
        Assert.That(snippet, Is.EqualTo("web01"));
    }

    [Test]
    public void From_QueryOverlapsThePrefix_KeepsThePrefixSoTheMatchIsVisible()
    {
        // Act
        var snippet = ProjectSnippet.From("  - 主機", "- ");

        // Assert
        Assert.That(snippet, Is.EqualTo("- 主機"));
    }

    [TestCase("1.", "1. ")]
    [TestCase("# 主", "# 主機")]
    public void From_QueryInsideThePrefixOfALongLine_StartsAtTheMatchAndStaysUnderTheLimit(string query, string head)
    {
        // Arrange
        var line = head + new string('字', 200);

        // Act
        var snippet = ProjectSnippet.From(line, query)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet, Does.StartWith(query));
            Assert.That(snippet, Does.EndWith("…"));
            Assert.That(snippet.Length, Is.EqualTo(ProjectDescriptionRules.MaxSnippetLength));
        }
    }

    [Test]
    public void From_QueryInsideThePrefixOfALongLine_AfterIndentation_StillContainsTheMatch()
    {
        // Arrange
        var line = "    1. " + new string('字', 200);

        // Act
        var snippet = ProjectSnippet.From(line, "1.")!;

        // Assert
        Assert.That(snippet, Does.StartWith("1. "));
    }

    [Test]
    public void From_LineIsOnlyAPrefix_KeepsTheTrimmedLine()
    {
        // Act
        var snippet = ProjectSnippet.From("a\n- \nb", "-");

        // Assert
        Assert.That(snippet, Is.EqualTo("-"));
    }

    [Test]
    public void From_ShortLine_IsNotTruncated()
    {
        // Arrange
        var line = new string('字', ProjectDescriptionRules.MaxSnippetLength);

        // Act
        var snippet = ProjectSnippet.From(line, "字");

        // Assert
        Assert.That(snippet, Is.EqualTo(line));
    }

    [Test]
    public void From_MatchAtStartOfLongLine_CutsOnlyTheEnd()
    {
        // Arrange
        var line = "web01 " + new string('字', 200);

        // Act
        var snippet = ProjectSnippet.From(line, "web01")!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet, Does.StartWith("web01 "));
            Assert.That(snippet, Does.EndWith("…"));
            Assert.That(snippet.Length, Is.EqualTo(ProjectDescriptionRules.MaxSnippetLength));
        }
    }

    [Test]
    public void From_MatchAtEndOfLongLine_CutsOnlyTheStart()
    {
        // Arrange
        var line = new string('字', 200) + " web01";

        // Act
        var snippet = ProjectSnippet.From(line, "web01")!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet, Does.StartWith("…"));
            Assert.That(snippet, Does.EndWith(" web01"));
            Assert.That(snippet.Length, Is.EqualTo(ProjectDescriptionRules.MaxSnippetLength));
        }
    }

    [Test]
    public void From_MatchInMiddleOfLongLine_CutsBothSidesAndKeepsTheMatchNearTheCenter()
    {
        // Arrange
        var line = new string('左', 150) + "web01" + new string('右', 150);

        // Act
        var snippet = ProjectSnippet.From(line, "web01")!;

        // Assert
        var at = snippet.IndexOf("web01", StringComparison.Ordinal);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet, Does.StartWith("…").And.EndWith("…"));
            Assert.That(snippet.Length, Is.EqualTo(ProjectDescriptionRules.MaxSnippetLength));
            Assert.That(at, Is.InRange(30, 45));
        }
    }

    [Test]
    public void From_MixedChineseAndEnglish_CountsEveryCharacterOnce()
    {
        // Arrange
        var line = string.Concat(Enumerable.Repeat("主機 host ", 30)) + "web01.example.com" + string.Concat(Enumerable.Repeat(" 資料庫 db", 30));

        // Act
        var snippet = ProjectSnippet.From(line, "web01.example.com")!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet, Does.Contain("web01.example.com"));
            Assert.That(snippet.Length, Is.LessThanOrEqualTo(ProjectDescriptionRules.MaxSnippetLength));
        }
    }

    [Test]
    public void From_MatchLongerThanTheLimit_StartsAtTheMatchAndCutsTheEnd()
    {
        // Arrange
        var query = new string('x', 120);
        var line = "前綴 " + query + " 後綴";

        // Act
        var snippet = ProjectSnippet.From(line, query)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet, Does.StartWith("…xxxx"));
            Assert.That(snippet, Does.EndWith("x…"));
            Assert.That(snippet.Length, Is.EqualTo(ProjectDescriptionRules.MaxSnippetLength));
        }
    }

    [Test]
    public void From_CutPointInsideASurrogatePair_DoesNotLeaveALoneSurrogate()
    {
        // Arrange
        var line = string.Concat(Enumerable.Repeat("😀", 100)) + "web01" + string.Concat(Enumerable.Repeat("😀", 100));

        // Act
        var snippet = ProjectSnippet.From(line, "web01")!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippet.EnumerateRunes().Any(rune => rune == Rune.ReplacementChar), Is.False);
            Assert.That(snippet, Does.Contain("web01"));
        }
    }

    [Test]
    public void From_CaseDiffers_StillMatches()
    {
        // Act
        var snippet = ProjectSnippet.From("- Host: WEB01", "web01");

        // Assert
        Assert.That(snippet, Is.EqualTo("Host: WEB01"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("沒有相關的內容")]
    public void From_NoTextOrNoMatchingLine_FallsBackToTheFirstNonBlankLine(string? text)
    {
        // Act
        var snippet = ProjectSnippet.From(text, "web01");

        // Assert
        Assert.That(snippet, Is.EqualTo(string.IsNullOrEmpty(text) ? null : text));
    }
}
