using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint.Documents;

namespace PmTool.Blueprint.Tests.Documents;

[TestFixture]
public sealed class DocumentReaderTests
{
    private const string BlueprintExample = "agency-project.blueprint.yaml";
    private const string ChangeSetExample = "split-acceptance.changeset.yaml";

    [Test]
    public void Read_Null_ReportsDocumentEmpty([Values] DocumentFormat format)
    {
        // Act
        var result = DocumentReader.Read(null, format);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentEmpty);
    }

    [Test]
    public void Read_EmptyString_ReportsDocumentEmpty([Values] DocumentFormat format)
    {
        // Act
        var result = DocumentReader.Read(string.Empty, format);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentEmpty);
    }

    [Test]
    public void Read_WhitespaceOnly_ReportsDocumentEmpty([Values] DocumentFormat format)
    {
        // Arrange
        const string text = "  \n\t\r\n  ";

        // Act
        var result = DocumentReader.Read(text, format);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentEmpty);
    }

    [Test]
    public void Read_ByteOrderMarkOnly_ReportsDocumentEmpty([Values] DocumentFormat format)
    {
        // Arrange
        var text = DocumentText.ByteOrderMark + "\n";

        // Act
        var result = DocumentReader.Read(text, format);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentEmpty);
    }

    [Test]
    public void Read_TextOverTheCharacterLimit_ReportsTooLargeWithoutParsing([Values] DocumentFormat format)
    {
        // Arrange：內容本身也不合法，回報 too_large 而不是 syntax 才表示沒有去解析。
        var text = new string('x', DocumentLimits.MaxCharacters + 1);

        // Act
        var result = DocumentReader.Read(text, format);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooLarge);
    }

    [Test]
    public void Read_TextExactlyAtTheCharacterLimit_IsAccepted([Values] DocumentFormat format)
    {
        // Arrange：一個雙引號字串在 JSON 與 YAML 裡都是合法的文件。
        var text = "\"" + new string('a', DocumentLimits.MaxCharacters - 2) + "\"";

        // Act
        var result = DocumentReader.Read(text, format);

        // Assert
        var root = ReadAssert.Succeeds(result);
        Assert.That(root!.GetValue<string>(), Has.Length.EqualTo(DocumentLimits.MaxCharacters - 2));
    }

    [TestCase(DocumentFormat.Json, "{\"name\":1}")]
    [TestCase(DocumentFormat.Yaml, "name: 1\n")]
    public void Read_LeadingByteOrderMark_IsIgnored(DocumentFormat format, string body)
    {
        // Arrange
        var text = DocumentText.ByteOrderMark + body;

        // Act
        var result = DocumentReader.Read(text, format);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root.Select(property => property.Key), Is.EqualTo(new[] { "name" }));
    }

    [Test]
    public void Read_JsonWithTwoByteOrderMarks_ParsesTheSecondOneAsContent()
    {
        // Arrange
        var text = new string(DocumentText.ByteOrderMark, 2) + "{}";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    // 參數用字碼而不是 char：測試名稱裡出現落單的代理字元，dotnet test 的 --filter 會默默跳過這個案例。
    [TestCase(DocumentFormat.Json, (int)DocumentText.HighSurrogate)]
    [TestCase(DocumentFormat.Json, (int)DocumentText.LowSurrogate)]
    [TestCase(DocumentFormat.Yaml, (int)DocumentText.HighSurrogate)]
    [TestCase(DocumentFormat.Yaml, (int)DocumentText.LowSurrogate)]
    public void Read_UnpairedSurrogate_ReportsDocumentSyntax(DocumentFormat format, int codeUnit)
    {
        // Arrange
        var surrogate = (char)codeUnit;
        var text = format == DocumentFormat.Json ? $"{{\"a\":\"x{surrogate}\"}}" : $"a: x{surrogate}\n";

        // Act
        var result = DocumentReader.Read(text, format);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [TestCase(DocumentFormat.Json, "{\"a\":\"%\"}")]
    [TestCase(DocumentFormat.Yaml, "a: %\n")]
    public void Read_SurrogatePair_KeepsTheCharacter(DocumentFormat format, string template)
    {
        // Arrange
        var emoji = char.ConvertFromUtf32(0x1F600);
        var text = template.Replace("%", emoji, StringComparison.Ordinal);

        // Act
        var result = DocumentReader.Read(text, format);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root["a"]!.GetValue<string>(), Is.EqualTo(emoji));
    }

    [TestCase(DocumentFormat.Json, "{\"a\": example_unquoted_word}")]
    [TestCase(DocumentFormat.Yaml, "a: [example_unquoted_word\n")]
    public void Read_SyntaxError_DoesNotEchoTheDocumentText(DocumentFormat format, string text)
    {
        // Act
        var result = DocumentReader.Read(text, format);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
        Assert.That(result.Issues[0].Message, Does.Not.Contain("example_unquoted_word"));
    }

    [Test]
    public void Read_UndefinedFormat_ThrowsBecauseItIsAProgrammingError()
    {
        // Arrange
        const DocumentFormat undefined = (DocumentFormat)99;

        // Act & Assert
        Assert.That(() => DocumentReader.Read("{}", undefined), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(BlueprintExample)]
    [TestCase(ChangeSetExample)]
    public void Read_ExampleFile_ReadsIntoAnObject(string fileName)
    {
        // Arrange
        var text = DocumentText.ReadExample(fileName);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        ReadAssert.SucceedsWithObject(result);
    }

    [TestCase(BlueprintExample)]
    [TestCase(ChangeSetExample)]
    public void Read_ExampleFileConvertedToJson_ReadsIntoTheSameTree(string fileName)
    {
        // Arrange
        var fromYaml = ReadAssert.SucceedsWithObject(DocumentReader.Read(DocumentText.ReadExample(fileName), DocumentFormat.Yaml));
        var json = fromYaml.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // Act
        var result = DocumentReader.Read(json, DocumentFormat.Json);

        // Assert
        var fromJson = ReadAssert.SucceedsWithObject(result);
        Assert.That(JsonNode.DeepEquals(fromJson, fromYaml), Is.True);
    }

    [TestCase(BlueprintExample)]
    [TestCase(ChangeSetExample)]
    public void Read_ExampleFileWithWindowsLineEndings_ReadsIntoTheSameTree(string fileName)
    {
        // Arrange
        var text = DocumentText.ReadExample(fileName).ReplaceLineEndings("\n");
        var expected = ReadAssert.SucceedsWithObject(DocumentReader.Read(text, DocumentFormat.Yaml));

        // Act
        var result = DocumentReader.Read(text.Replace("\n", "\r\n", StringComparison.Ordinal), DocumentFormat.Yaml);

        // Assert
        var actual = ReadAssert.SucceedsWithObject(result);
        Assert.That(actual.ToJsonString(), Is.EqualTo(expected.ToJsonString()));
    }

    [Test]
    public void Read_ExampleChangeSet_ReadsBaseVersionAsInteger()
    {
        // Arrange
        var text = DocumentText.ReadExample(ChangeSetExample);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root["baseVersion"]!.AsValue().TryGetValue<long>(out var baseVersion) ? baseVersion : (long?)null, Is.EqualTo(12));
    }
}
