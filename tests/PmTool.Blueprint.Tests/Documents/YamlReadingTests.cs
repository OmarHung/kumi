using System.Text.Json.Nodes;
using PmTool.Blueprint.Documents;

namespace PmTool.Blueprint.Tests.Documents;

[TestFixture]
public sealed class YamlReadingTests
{
    private const int OneHundredThousand = 100_000;

    [TestCase("a: [1, 2\n")]
    [TestCase("a:\n  b: 1\n c: 2\n")]
    [TestCase("a: 'x\n")]
    [TestCase("a: b: c\n")]
    [TestCase("- a\nb: c\n")]
    [TestCase("a:\n\tb: 1\n")]
    public void Read_YamlSyntaxError_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    // YamlDotNet 18.1.0 對這幾種輸入丟的不是 YamlException，是用模糊測試找到的。
    [TestCase("a: \"" + DocumentText.Backslash + "UFFFFFFFF\"\n")]
    [TestCase("!!b[")]
    [TestCase(":\r[\r7y0")]
    [TestCase("...\r" + DocumentText.Backslash + "2z]!73\r#&e")]
    public void Read_YamlThatMakesTheParserThrowOtherExceptions_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [TestCase("a: 1\n---\nb: 2\n")]
    [TestCase("---\na: 1\n---\n")]
    [TestCase("a: 1\n...\n---\nb: 2\n")]
    public void Read_YamlWithMoreThanOneDocument_ReportsMultipleDocuments(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentMultiple);
    }

    [TestCase("a: 1\n...\n")]
    [TestCase("---\na: 1\n")]
    public void Read_YamlWithDocumentMarkers_ReadsTheSingleDocument(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root["a"]!.GetValue<long>(), Is.EqualTo(1));
    }

    [Test]
    public void Read_YamlWithOnlyComments_ReportsDocumentEmpty()
    {
        // Arrange
        const string text = "# just a comment\n# and another\n";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentEmpty);
    }

    [TestCase("~")]
    [TestCase("null\n")]
    [TestCase("---\n")]
    [TestCase("--- ~\n...\n")]
    public void Read_YamlNullDocument_ReturnsNullRootWithoutIssues(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Is.Empty);
            Assert.That(result.Root, Is.Null);
        });
    }

    [TestCase("a: &x 1\n")]
    [TestCase("a: &x 1\nb: *x\n")]
    [TestCase("a: *undefined\n")]
    [TestCase("base: &b {k: 1}\nother:\n  <<: *b\n")]
    [TestCase("- &s [1]\n")]
    [TestCase("&m {a: 1}\n")]
    public void Read_YamlAnchorOrAlias_ReportsAliasNotAllowed(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentAliasNotAllowed);
    }

    [TestCase("a: !!str 12\n")]
    [TestCase("a: !custom x\n")]
    [TestCase("a: ! 12\n")]
    [TestCase("a: !!map {b: 1}\n")]
    [TestCase("!!seq [1]\n")]
    [TestCase("!!str key: v\n")]
    public void Read_YamlExplicitTag_ReportsSyntaxAndSaysTagsAreNotSupported(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
        Assert.That(result.Issues[0].Message, Does.Contain("標籤"));
    }

    [TestCase("? [a, b]\n: v\n")]
    [TestCase("[a]: v\n")]
    [TestCase("{a: 1}: v\n")]
    [TestCase("? {a: 1}\n: v\n")]
    [TestCase("x:\n  ? - a\n  : v\n")]
    public void Read_YamlKeyThatIsNotAScalar_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [TestCase("a: 1\na: 2\n")]
    [TestCase("{a: 1, a: 2}")]
    [TestCase("x:\n  a: 1\n  a: 2\n")]
    [TestCase("- {a: 1}\n- {b: 1, b: 2}\n")]
    public void Read_YamlDuplicateKey_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [TestCase("1: a\n\"1\": b\n")]
    [TestCase("a: 1\n'a': 2\n")]
    [TestCase("true: a\n'true': b\n")]
    [TestCase("~: a\n\"~\": b\n")]
    public void Read_YamlKeysThatBecomeTheSamePropertyName_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [Test]
    public void Read_YamlScalarKeys_UseTheirTextAsPropertyNames()
    {
        // Arrange
        const string text = "12: a\nnull: b\ntrue: c\nTrue: d\n1.5: e\n'quoted': f\n";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root.Select(property => property.Key), Is.EqualTo(new[] { "12", "null", "true", "True", "1.5", "quoted" }));
    }

    [Test]
    public void Read_YamlMappingsAndSequences_BecomeObjectsAndArraysInDocumentOrder()
    {
        // Arrange
        const string text = "z: [1, {b: 2}]\na:\n  - x\n  - - y\nm: {}\ns: []\n";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root.ToJsonString(), Is.EqualTo("{\"z\":[1,{\"b\":2}],\"a\":[\"x\",[\"y\"]],\"m\":{},\"s\":[]}"));
    }

    [Test]
    public void Read_YamlFlowNestingExactlyToTheDepthLimit_IsAccepted()
    {
        // Arrange
        var text = DocumentText.NestedArrays(DocumentLimits.MaxDepth);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        Assert.That(ReadAssert.Succeeds(result), Is.InstanceOf<JsonArray>());
    }

    [Test]
    public void Read_YamlFlowNestingOneLevelPastTheDepthLimit_ReportsTooDeep()
    {
        // Arrange
        var text = DocumentText.NestedArrays(DocumentLimits.MaxDepth + 1);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_YamlBlockMappingsNestedExactlyToTheDepthLimit_AreAccepted()
    {
        // Arrange
        var text = DocumentText.NestedBlockMappings(DocumentLimits.MaxDepth);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.SucceedsWithObject(result);
    }

    [Test]
    public void Read_YamlBlockMappingsNestedOneLevelPastTheDepthLimit_ReportsTooDeep()
    {
        // Arrange
        var text = DocumentText.NestedBlockMappings(DocumentLimits.MaxDepth + 1);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_YamlFlowNestedOneHundredThousandLevels_ReportsTooDeepWithoutCrashing()
    {
        // Arrange
        var text = DocumentText.NestedArrays(OneHundredThousand);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_YamlBlockSequencesNestedOneHundredThousandLevels_ReportsTooDeepWithoutCrashing()
    {
        // Arrange：「- - - x」每個破折號都開一層新的 block sequence。
        var text = DocumentText.Repeat("- ", OneHundredThousand) + "x\n";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_YamlUnclosedDeepNesting_ReportsTooDeepBeforeReachingTheEnd()
    {
        // Arrange
        var text = new string('[', OneHundredThousand);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_YamlWithExactlyTheNodeLimit_IsAccepted()
    {
        // Arrange：陣列本身算一個節點。
        var text = DocumentText.FlowArrayOfZeros(DocumentLimits.MaxNodes - 1);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        var root = ReadAssert.Succeeds(result);
        Assert.That(root!.AsArray(), Has.Count.EqualTo(DocumentLimits.MaxNodes - 1));
    }

    [Test]
    public void Read_YamlWithOneNodePastTheLimit_ReportsTooManyNodes()
    {
        // Arrange
        var text = DocumentText.YamlBlockSequenceOfZeros(DocumentLimits.MaxNodes);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooManyNodes);
    }

    [Test]
    public void Read_YamlKeys_DoNotCountAsNodes()
    {
        // Arrange：mapping 一個節點，加上 MaxNodes - 1 個值，剛好等於上限。
        var text = DocumentText.YamlMappingWithKeys(DocumentLimits.MaxNodes - 1);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root, Has.Count.EqualTo(DocumentLimits.MaxNodes - 1));
    }

    [Test]
    public void Read_YamlEmptyValues_CountAsNodes()
    {
        // Arrange：沒寫值的鍵讀成 null，null 也是一個節點，所以這份剛好超過上限。
        var text = string.Concat(Enumerable.Range(0, DocumentLimits.MaxNodes).Select(index => $"k{index}:\n"));

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooManyNodes);
    }
}
