using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Tests.Integration;

/// <summary>三個階段怎麼銜接：前一階段有問題就停、問題太多會截斷、通過結構檢查的值一定綁得進模型。</summary>
[TestFixture]
public sealed class PipelineTests
{
    private const string BaseVersionPlaceholder = "__BASE_VERSION__";

    private static readonly BlueprintValidator Validator = BlueprintValidator.CreateDefault();

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   \n\t")]
    public void ValidateBlueprint_NoContent_ReportsDocumentEmptyAndNothingElse(string? text)
    {
        // Act
        var result = Validator.ValidateBlueprint(text, DocumentFormat.Yaml);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CodesAndPaths(result.Report), Is.EqualTo(new[] { (IssueCodes.DocumentEmpty, string.Empty) }));
            Assert.That(result.Blueprint, Is.Null);
        }
    }

    [TestCase(DocumentFormat.Json, "{ \"apiVersion\": ")]
    [TestCase(DocumentFormat.Yaml, "apiVersion: [unclosed")]
    public void ValidateBlueprint_UnreadableDocument_StopsBeforeTheStructuralCheck(DocumentFormat format, string text)
    {
        // Act
        var result = Validator.ValidateBlueprint(text, format);

        // Assert：讀不進來就沒有樹可以檢查，不能再冒出「少了必要的屬性」這類問題。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CodesAndPaths(result.Report), Is.EqualTo(new[] { (IssueCodes.DocumentSyntax, string.Empty) }));
            Assert.That(result.Blueprint, Is.Null);
        }
    }

    [TestCase(DocumentFormat.Json, "null")]
    [TestCase(DocumentFormat.Yaml, "~")]
    [TestCase(DocumentFormat.Json, "[]")]
    [TestCase(DocumentFormat.Yaml, "just a sentence")]
    public void ValidateChangeSet_RootIsNotAnObject_ReportsStructureTypeAtTheRoot(DocumentFormat format, string text)
    {
        // Act
        var result = Validator.ValidateChangeSet(text, format);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CodesAndPaths(result.Report), Is.EqualTo(new[] { (IssueCodes.StructureType, string.Empty) }));
            Assert.That(result.ChangeSet, Is.Null);
        }
    }

    [Test]
    public void ValidateBlueprint_StructuralAndSemanticProblemsTogether_ReportsOnlyTheStructuralOnes()
    {
        // Arrange：同一份文件裡，bindings 清空會觸發語意問題，多出來的屬性是結構問題。
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.BlueprintFile);
        tree["bindings"] = new JsonArray();
        tree["fields"]![0]!["colour"] = "red";

        // Act
        var result = Validator.ValidateBlueprint(ExampleDocuments.ToJson(tree), DocumentFormat.Json);

        // Assert
        Assert.That(CodesAndPaths(result.Report), Is.EqualTo(new[]
        {
            (IssueCodes.StructureAdditionalProperty, "/fields/0/colour"),
        }));
    }

    [Test]
    public void ValidateBlueprint_MoreStructuralIssuesThanTheLimit_TruncatesTheReport()
    {
        // Arrange
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.BlueprintFile);
        for (var index = 0; index < ValidationReport.MaxIssues + 50; index++)
        {
            tree[$"extra_{index}"] = index;
        }

        // Act
        var result = Validator.ValidateBlueprint(ExampleDocuments.ToJson(tree), DocumentFormat.Json);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Report.Issues, Has.Count.EqualTo(ValidationReport.MaxIssues));
            Assert.That(result.Report.Truncated, Is.True);
            Assert.That(result.Report.Issues.Select(issue => issue.Code), Is.All.EqualTo(IssueCodes.StructureAdditionalProperty));
            Assert.That(result.Report.Issues[0].Path, Is.EqualTo("/extra_0"));
            Assert.That(result.Blueprint, Is.Null);
        }
    }

    [Test]
    public void ValidateBlueprint_MoreSemanticIssuesThanTheLimit_TruncatesTheReport()
    {
        // Arrange：每個欄位的 key 都不合格，數量又沒超過欄位上限，問題全部來自語意規則。
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.BlueprintFile);
        var options = tree["fields"]![3]!["options"]!.AsArray();
        for (var index = 0; index < ValidationReport.MaxIssues + 50; index++)
        {
            options.Add(new JsonObject { ["key"] = $"Bad-{index}", ["name"] = "x" });
        }

        // Act
        var result = Validator.ValidateBlueprint(ExampleDocuments.ToYaml(tree), DocumentFormat.Yaml);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Report.Issues, Has.Count.EqualTo(ValidationReport.MaxIssues));
            Assert.That(result.Report.Truncated, Is.True);
            Assert.That(result.Report.Issues.Select(issue => issue.Code), Is.All.EqualTo(IssueCodes.KeyFormat));
            Assert.That(result.Blueprint, Is.Null);
        }
    }

    [Test]
    public void ValidateBlueprint_VeryLongKey_KeepsTheResponseSmall()
    {
        // Arrange：key 很長又被引用很多次時，訊息如果原樣帶出 key，回應會比文件大很多倍。
        var longKey = new string('k', 20_000);
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.BlueprintFile);
        var columns = tree["views"]![1]!["columns"]!.AsArray();
        for (var index = 0; index < 10; index++)
        {
            columns.Add(longKey);
        }

        // Act
        var result = Validator.ValidateBlueprint(ExampleDocuments.ToJson(tree), DocumentFormat.Json);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Report.Issues, Has.Count.EqualTo(10));
            Assert.That(result.Report.Issues.Select(issue => issue.Code), Is.All.EqualTo(IssueCodes.ViewUnknownField));
            Assert.That(result.Report.Issues.Select(issue => issue.Message.Length), Is.All.LessThan(300));
            Assert.That(result.Report.Issues.Select(issue => issue.Path.Length), Is.All.LessThan(100));
        }
    }

    [TestCase(DocumentFormat.Json, "12", 12L)]
    [TestCase(DocumentFormat.Json, "12.0", 12L)]
    [TestCase(DocumentFormat.Json, "1.2e1", 12L)]
    [TestCase(DocumentFormat.Json, "0", 0L)]
    [TestCase(DocumentFormat.Json, "1E+18", 1_000_000_000_000_000_000L)]
    [TestCase(DocumentFormat.Json, "9223372036854775807", long.MaxValue)]
    [TestCase(DocumentFormat.Yaml, "12", 12L)]
    [TestCase(DocumentFormat.Yaml, "+12", 12L)]
    [TestCase(DocumentFormat.Yaml, "12.0", 12L)]
    [TestCase(DocumentFormat.Yaml, "1.2e1", 12L)]
    [TestCase(DocumentFormat.Yaml, "0o14", 12L)]
    [TestCase(DocumentFormat.Yaml, "0xC", 12L)]
    [TestCase(DocumentFormat.Yaml, "1e18", 1_000_000_000_000_000_000L)]
    [TestCase(DocumentFormat.Yaml, "9223372036854775807", long.MaxValue)]
    public void ValidateChangeSet_BaseVersionWrittenAsAnyIntegerValue_PassesAndBindsIt(
        DocumentFormat format, string literal, long expected)
    {
        // Arrange
        var text = ChangeSetWithBaseVersion(format, literal);

        // Act
        var result = Validator.ValidateChangeSet(text, format);

        // Assert
        Assert.That(result.Report.Issues, Is.Empty);
        Assert.That(result.ChangeSet!.BaseVersion, Is.EqualTo(expected));
    }

    [TestCase(DocumentFormat.Json, "12.5")]
    [TestCase(DocumentFormat.Json, "1e30")]
    [TestCase(DocumentFormat.Json, "1e400")]
    [TestCase(DocumentFormat.Json, "9223372036854775808")]
    [TestCase(DocumentFormat.Json, "\"12\"")]
    [TestCase(DocumentFormat.Json, "true")]
    [TestCase(DocumentFormat.Json, "null")]
    [TestCase(DocumentFormat.Yaml, "12.5")]
    [TestCase(DocumentFormat.Yaml, "1e30")]
    [TestCase(DocumentFormat.Yaml, "1e999")]
    [TestCase(DocumentFormat.Yaml, ".inf")]
    [TestCase(DocumentFormat.Yaml, "9223372036854775808")]
    [TestCase(DocumentFormat.Yaml, "9223372036854775807.0")]
    [TestCase(DocumentFormat.Yaml, "\"12\"")]
    [TestCase(DocumentFormat.Yaml, "yes")]
    [TestCase(DocumentFormat.Yaml, "~")]
    public void ValidateChangeSet_BaseVersionThatIsNotAnInt64_ReportsStructureTypeInsteadOfThrowing(
        DocumentFormat format, string literal)
    {
        // Arrange
        var text = ChangeSetWithBaseVersion(format, literal);

        // Act
        var result = Validator.ValidateChangeSet(text, format);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CodesAndPaths(result.Report), Is.EqualTo(new[] { (IssueCodes.StructureType, "/baseVersion") }));
            Assert.That(result.ChangeSet, Is.Null);
        }
    }

    [TestCase(DocumentFormat.Json, "-1")]
    [TestCase(DocumentFormat.Json, "-1.0")]
    [TestCase(DocumentFormat.Json, "-9223372036854775808")]
    [TestCase(DocumentFormat.Yaml, "-1")]
    [TestCase(DocumentFormat.Yaml, "-9223372036854775808")]
    [TestCase(DocumentFormat.Yaml, "-9223372036854775808.0")]
    public void ValidateChangeSet_NegativeBaseVersion_BindsItAndReportsTheSemanticIssue(DocumentFormat format, string literal)
    {
        // Arrange
        var text = ChangeSetWithBaseVersion(format, literal);

        // Act
        var result = Validator.ValidateChangeSet(text, format);

        // Assert：能走到語意規則，表示這個值通過了結構檢查，也綁進了模型。
        Assert.That(CodesAndPaths(result.Report), Is.EqualTo(new[]
        {
            (IssueCodes.ChangeSetInvalidBaseVersion, "/baseVersion"),
        }));
    }

    /// <summary>範例變更集的 baseVersion 換成指定的寫法，原樣放進文字裡（不經過序列化，才測得到各種記法）。</summary>
    private static string ChangeSetWithBaseVersion(DocumentFormat format, string literal)
    {
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.ChangeSetFile);
        tree["baseVersion"] = BaseVersionPlaceholder;
        var text = format == DocumentFormat.Json ? ExampleDocuments.ToJson(tree) : ExampleDocuments.ToYaml(tree);
        var quotedPlaceholder = $"\"{BaseVersionPlaceholder}\"";
        Assert.That(text, Does.Contain(quotedPlaceholder), "找不到要替換的位置");
        return text.Replace(quotedPlaceholder, literal, StringComparison.Ordinal);
    }

    private static (string Code, string Path)[] CodesAndPaths(ValidationReport report) =>
        [.. report.Issues.Select(issue => (issue.Code, issue.Path))];
}
