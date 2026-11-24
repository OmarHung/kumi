using System.Text.Json.Nodes;
using PmTool.Cli.Import.Monday;
using PmTool.Cli.Import.Monday.Run;
using static PmTool.Cli.Tests.ImportFixtures;

namespace PmTool.Cli.Tests;

/// <summary>monday 的欄 → 自訂欄位的轉換（交接第 6.3 節）：每種欄位種類、收得下哪些欄、值不能用時怎麼處理。</summary>
[TestFixture]
public sealed class MondayFieldConverterTests
{
    private static readonly ServerPerson Dev = new(FakePmBackend.Dev, "Example Dev", "dev@example.com");
    private static readonly FieldMapping Plain = new() { Field = "f" };

    private static FieldConversion Convert(string fieldType, MondayColumnValue value, FieldMapping? mapping = null, params string[] options) =>
        FieldConverter.Convert("欄位", fieldType, mapping ?? Plain, value, options.ToHashSet(), id => id == "1002" ? Dev : null);

    [TestCase("number", "numbers", true)]
    [TestCase("number", "text", true)]
    [TestCase("number", "date", false)]
    [TestCase("text", "link", true)]
    [TestCase("long_text", "numbers", true)]
    [TestCase("url", "link", true)]
    [TestCase("url", "numbers", false)]
    [TestCase("date", "timeline", true)]
    [TestCase("single_select", "board_relation", true)]
    [TestCase("single_select", "people", false)]
    [TestCase("multi_select", "dropdown", true)]
    [TestCase("person", "people", true)]
    [TestCase("person", "status", false)]
    [TestCase("checkbox", "checkbox", true)]
    [TestCase("currency", "numbers", false)]
    public void IsSupported_FollowsTheTable(string fieldType, string columnType, bool expected)
    {
        Assert.That(FieldConverter.IsSupported(fieldType, columnType), Is.EqualTo(expected));
    }

    [Test]
    public void Number_ReadsPlainAndThousandSeparatedNumbers_AndRemindsOnTheRest()
    {
        Assert.That(Convert("number", Text("c", "numbers", "1,234.5")).Value!.GetValue<decimal>(), Is.EqualTo(1234.5m));
        var bad = Convert("number", Text("c", "text", "約三小時"));
        Assert.That(bad.Value, Is.Null);
        Assert.That(bad.Reminder, Is.EqualTo("欄位「欄位」的值「約三小時」不是數字，略過"));
        Assert.That(Convert("number", Text("c", "numbers", "  ")).Value, Is.Null);
    }

    [Test]
    public void Text_UsesTheLinkUrlForLinks_AndDropsInvisibleCharacters()
    {
        Assert.That(Convert("text", Text("c", "link", "文件", "{\"url\":\"https://example.com/a\",\"text\":\"文件\"}")).Value!.GetValue<string>(), Is.EqualTo("https://example.com/a"));
        Assert.That(Convert("long_text", Text("c", "long_text", "\uFEFF第一行")).Value!.GetValue<string>(), Is.EqualTo("第一行"));
    }

    [Test]
    public void Url_OnlyAcceptsHttpAndHttps()
    {
        Assert.That(Convert("url", Text("c", "text", "http://example.com")).Value!.GetValue<string>(), Is.EqualTo("http://example.com"));
        Assert.That(Convert("url", Text("c", "text", "javascript:alert(1)")).Reminder, Does.Contain("不是 http 或 https 網址"));
    }

    [Test]
    public void Date_ReadsDateColumnsAndTheEndOfATimeline()
    {
        Assert.That(Convert("date", Date("c", "2026-11-04")).Value!.GetValue<string>(), Is.EqualTo("2026-11-04"));
        Assert.That(Convert("date", Timeline("c", "2026-11-01", "2026-11-09")).Value!.GetValue<string>(), Is.EqualTo("2026-11-09"));
        Assert.That(Convert("date", Text("c", "date", "2026-11-12 10:00", "{\"date\":\"2026-11-12\",\"time\":\"10:00:00\"}")).Value!.GetValue<string>(), Is.EqualTo("2026-11-12"));
        Assert.That(Convert("date", Text("c", "date", null)).Value, Is.Null);
    }

    [Test]
    public void SingleSelect_TakesTheFirstValueThatMapsToAnExistingOption()
    {
        // Arrange
        var mapping = new FieldMapping { Field = "f", Values = new Dictionary<string, string> { ["B"] = "b", ["C"] = "gone" } };

        // Act
        var fromRelation = Convert("single_select", Relation("c", "A", "B"), mapping, "a", "b");
        var fromStatus = Convert("single_select", Status("c", "B"), mapping, "b");
        var unmapped = Convert("single_select", Status("c", "Z"), mapping, "b");
        var unknownOption = Convert("single_select", Status("c", "C"), mapping, "b");

        // Assert
        Assert.That(fromRelation.Value!.GetValue<string>(), Is.EqualTo("b"));
        Assert.That(fromStatus.Value!.GetValue<string>(), Is.EqualTo("b"));
        Assert.That((unmapped.Value, unmapped.Reminder), Is.EqualTo(((JsonNode?)null, "欄位「欄位」的值「Z」沒有對應的選項，略過")));
        Assert.That(unknownOption.Value, Is.Null);
        Assert.That(Convert("single_select", Status("c", null), mapping, "b").Reminder, Is.Null);
    }

    [Test]
    public void MultiSelect_TakesEveryMappedValue_WithoutDuplicates()
    {
        // Arrange
        var mapping = new FieldMapping { Field = "f", Values = new Dictionary<string, string> { ["x"] = "t1", ["y"] = "t1", ["z"] = "t2" } };

        // Act
        var result = Convert("multi_select", Text("c", "dropdown", "x, y, z, w"), mapping, "t1", "t2");

        // Assert
        Assert.That(result.Value!.AsArray().Select(option => option!.GetValue<string>()), Is.EqualTo(new[] { "t1", "t2" }));
    }

    [Test]
    public void Person_TakesTheFirstEligiblePerson_AndRemindsWhenNoneIs()
    {
        var found = Convert("person", People("c", "1003", "1002"));
        Assert.That((found.Value!.GetValue<string>(), found.PersonId), Is.EqualTo((Dev.Id.ToString(), (Guid?)Dev.Id)));
        var none = Convert("person", People("c", "1003"));
        Assert.That(none.Value, Is.Null);
        Assert.That(none.Reminder, Does.Contain("對不上這裡的成員"));
        Assert.That(Convert("person", People("c")).Reminder, Is.Null);
    }

    [Test]
    public void Checkbox_IsTrueOnlyWhenChecked_AndAbsentWhenThereIsNoValue()
    {
        Assert.That(Convert("checkbox", Text("c", "checkbox", null, "{\"checked\":\"true\"}")).Value!.GetValue<bool>(), Is.True);
        Assert.That(Convert("checkbox", Text("c", "checkbox", null, "{\"checked\":\"false\"}")).Value!.GetValue<bool>(), Is.False);
        Assert.That(Convert("checkbox", Text("c", "checkbox", null, "not json")).Value, Is.Null);
        Assert.That(Convert("checkbox", Text("c", "checkbox", null)).Value, Is.Null);
    }
}
