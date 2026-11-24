using System.Diagnostics;
using System.Text.RegularExpressions;
using PmTool.Work.Events;

namespace PmTool.Work.Tests.Events;

/// <summary>提及的解析在惡意或亂打的輸入下不能變慢，也不能讓儲存失敗。</summary>
[TestFixture]
public sealed class MentionRegexTests
{
    private const string PersonId = "3f2b7a10-0000-4000-8000-000000000001";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    // 一定會逾時的樣式，用來證明接住逾時的路徑。
    private static Regex AlwaysTimesOut() =>
        new(@"(a+)+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(1));

    [Test]
    public void Parse_FiftyThousandCharactersOfOpeningBrackets_FinishesQuicklyWithNoMentions()
    {
        // Arrange：每個 "@[" 都會往後找 "]"，名稱沒有長度上限時是二次方。
        var text = string.Concat(Enumerable.Repeat("@[", 25_000)) + "](mention:不是編號)";

        // Act
        var started = Stopwatch.StartNew();
        var found = Mentions.Parse(text);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Elapsed, Is.LessThan(Limit));
            Assert.That(found, Is.Empty);
        }
    }

    [Test]
    public void Excerpt_FiftyThousandCharactersOfOpeningBrackets_FinishesQuickly()
    {
        // Arrange
        var text = string.Concat(Enumerable.Repeat("@[", 25_000));

        // Act
        var started = Stopwatch.StartNew();
        var excerpt = Mentions.Excerpt(text);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Elapsed, Is.LessThan(Limit));
            Assert.That(excerpt, Has.Length.EqualTo(WorkEventRules.MaxExcerptLength));
        }
    }

    [Test]
    public void Parse_NameOfOneHundredCharacters_IsStillAMention()
    {
        // Arrange
        var text = $"@[{new string('名', 100)}](mention:{PersonId})";

        // Act
        var found = Mentions.Parse(text);

        // Assert
        Assert.That(found, Is.EqualTo(new[] { Guid.Parse(PersonId) }));
    }

    [Test]
    public void Parse_NameLongerThanTheLimit_IsNotAMention()
    {
        // Arrange
        var text = $"@[{new string('名', 101)}](mention:{PersonId})";

        // Act
        var found = Mentions.Parse(text);

        // Assert
        Assert.That(found, Is.Empty);
    }

    [Test]
    public void Parse_RegexTimesOut_CountsAsNoMentions()
    {
        // Arrange
        var text = new string('a', 50_000) + "!";

        // Act
        var found = Mentions.Parse(AlwaysTimesOut(), text);

        // Assert
        Assert.That(found, Is.Empty);
    }

    [Test]
    public void Excerpt_RegexTimesOut_KeepsTheTextWithoutReplacingMentions()
    {
        // Arrange
        var text = new string('a', 50_000) + "!";

        // Act
        var excerpt = Mentions.Excerpt(AlwaysTimesOut(), text);

        // Assert
        Assert.That(excerpt, Is.EqualTo(new string('a', WorkEventRules.MaxExcerptLength)));
    }
}
