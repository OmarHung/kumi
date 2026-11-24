using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint.Documents;

namespace PmTool.Blueprint.Tests.Documents;

[TestFixture]
public sealed class JsonReadingTests
{
    private const int OneHundredThousand = 100_000;

    [TestCase("{\"a\":}")]
    [TestCase("{\"a\":1")]
    [TestCase("[1 2]")]
    [TestCase("{a:1}")]
    [TestCase("'a'")]
    [TestCase("tru")]
    [TestCase("{} {}")]
    [TestCase("{\"a\":\"x\ty\"}")]
    public void Read_JsonSyntaxError_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [TestCase("{\"a\":1} // comment")]
    [TestCase("/* comment */ {\"a\":1}")]
    public void Read_JsonWithComment_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [TestCase("[1,]")]
    [TestCase("{\"a\":1,}")]
    public void Read_JsonWithTrailingComma_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [TestCase("{\"a\":1,\"a\":2}")]
    [TestCase("[{\"b\":{\"a\":1,\"a\":2}}]")]
    [TestCase("{\"a\":1,\"" + DocumentText.Backslash + "u0061\":2}")]
    public void Read_JsonDuplicateKey_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [Test]
    public void Read_JsonSameKeyInDifferentObjects_IsAccepted()
    {
        // Arrange
        const string text = "{\"a\":{\"a\":1},\"b\":[{\"a\":2},{\"a\":3}]}";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.SucceedsWithObject(result);
    }

    [TestCase("{\"a\":\"" + DocumentText.Backslash + "uD800\"}")]
    [TestCase("{\"a\":\"" + DocumentText.Backslash + "uDC00x\"}")]
    [TestCase("{\"" + DocumentText.Backslash + "uD800\":1}")]
    public void Read_JsonEscapedUnpairedSurrogate_ReportsDocumentSyntax(string text)
    {
        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentSyntax);
    }

    [Test]
    public void Read_JsonNull_ReturnsNullRootWithoutIssues()
    {
        // Act
        var result = DocumentReader.Read(" null ", DocumentFormat.Json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Is.Empty);
            Assert.That(result.Root, Is.Null);
        });
    }

    [Test]
    public void Read_JsonValues_KeepTheirKinds()
    {
        // Arrange
        const string text = "{\"baseVersion\":12,\"version\":\"1.0.0\",\"enabled\":true,\"ratio\":1.5,\"note\":null,\"tags\":[]}";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.Multiple(() =>
        {
            Assert.That(root["baseVersion"]!.GetValue<long>(), Is.EqualTo(12));
            Assert.That(root["version"]!.GetValue<string>(), Is.EqualTo("1.0.0"));
            Assert.That(root["enabled"]!.GetValueKind(), Is.EqualTo(JsonValueKind.True));
            Assert.That(root["ratio"]!.GetValue<double>(), Is.EqualTo(1.5));
            Assert.That(root.ContainsKey("note") && root["note"] is null, Is.True);
            Assert.That(root["tags"], Is.InstanceOf<JsonArray>());
        });
    }

    [Test]
    public void Read_JsonNumberBeyondDoubleRange_StaysSerializable()
    {
        // Act
        var result = DocumentReader.Read("[1e999]", DocumentFormat.Json);

        // Assert
        var root = ReadAssert.Succeeds(result);
        Assert.That(root!.ToJsonString(), Is.EqualTo("[1e999]"));
    }

    [Test]
    public void Read_JsonArraysNestedExactlyToTheDepthLimit_AreAccepted()
    {
        // Arrange
        var text = DocumentText.NestedArrays(DocumentLimits.MaxDepth);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.Succeeds(result);
    }

    [Test]
    public void Read_JsonArraysNestedOneLevelPastTheDepthLimit_ReportsTooDeep()
    {
        // Arrange
        var text = DocumentText.NestedArrays(DocumentLimits.MaxDepth + 1);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_JsonObjectsNestedExactlyToTheDepthLimit_AreAccepted()
    {
        // Arrange
        var text = DocumentText.NestedJsonObjects(DocumentLimits.MaxDepth);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.SucceedsWithObject(result);
    }

    [Test]
    public void Read_JsonObjectsNestedOneLevelPastTheDepthLimit_ReportsTooDeep()
    {
        // Arrange
        var text = DocumentText.NestedJsonObjects(DocumentLimits.MaxDepth + 1);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_JsonNestedOneHundredThousandLevels_ReportsTooDeepWithoutCrashing()
    {
        // Arrange
        var text = DocumentText.NestedArrays(OneHundredThousand);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_JsonUnclosedDeepNesting_ReportsTooDeepBeforeReachingTheEnd()
    {
        // Arrange
        var text = new string('[', OneHundredThousand);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooDeep);
    }

    [Test]
    public void Read_JsonWithExactlyTheNodeLimit_IsAccepted()
    {
        // Arrange：陣列本身算一個節點。
        var text = DocumentText.FlowArrayOfZeros(DocumentLimits.MaxNodes - 1);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        var root = ReadAssert.Succeeds(result);
        Assert.That(root!.AsArray(), Has.Count.EqualTo(DocumentLimits.MaxNodes - 1));
    }

    [Test]
    public void Read_JsonWithOneNodePastTheLimit_ReportsTooManyNodes()
    {
        // Arrange
        var text = DocumentText.FlowArrayOfZeros(DocumentLimits.MaxNodes);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        ReadAssert.FailsWith(result, IssueCodes.DocumentTooManyNodes);
    }

    [Test]
    public void Read_JsonPropertyNames_DoNotCountAsNodes()
    {
        // Arrange：物件一個節點，加上 MaxNodes - 1 個值，剛好等於上限。
        var text = DocumentText.JsonObjectWithProperties(DocumentLimits.MaxNodes - 1);

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Json);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root, Has.Count.EqualTo(DocumentLimits.MaxNodes - 1));
    }
}
