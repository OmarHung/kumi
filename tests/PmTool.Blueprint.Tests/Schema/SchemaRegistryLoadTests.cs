using System.Text.Json;
using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint.Tests.Schema;

[TestFixture]
public sealed class SchemaRegistryLoadTests
{
    [Test]
    public void Load_EmbeddedSchemas_Succeeds()
    {
        // Arrange
        var texts = EmbeddedSchemas.ReadAll();

        // Act & Assert
        Assert.That(() => SchemaRegistry.Load(texts), Throws.Nothing);
    }

    [Test]
    public void Load_EmbeddedSchemasInReverseOrder_Succeeds()
    {
        // Arrange：changeset 引用 project-blueprint，順序不同也要能解析。
        var texts = EmbeddedSchemas.ReadAll().Reverse().ToArray();

        // Act & Assert
        Assert.That(() => SchemaRegistry.Load(texts), Throws.Nothing);
    }

    [Test]
    public void Load_NullCollection_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.That(() => SchemaRegistry.Load(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Load_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        string[] texts = [null!];

        // Act & Assert
        Assert.That(() => SchemaRegistry.Load(texts), Throws.ArgumentNullException);
    }

    [TestCase("[]")]
    [TestCase("true")]
    [TestCase("\"urn:example:test\"")]
    public void Load_RootIsNotAnObject_ThrowsNotSupported(string text)
    {
        // Act & Assert
        Assert.That(() => SchemaRegistry.Load([text]), Throws.TypeOf<NotSupportedException>());
    }

    [TestCase("{}")]
    [TestCase("{\"type\": \"string\"}")]
    [TestCase("{\"$id\": 1}")]
    [TestCase("{\"$id\": \"\"}")]
    public void Load_WithoutStringId_ThrowsNotSupportedMentioningId(string text)
    {
        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("$id"));
    }

    [Test]
    public void Load_DuplicateId_ThrowsArgumentExceptionNamingTheId()
    {
        // Arrange
        var first = TestSchemas.WithId("""{"type": "string"}""");
        var second = TestSchemas.WithId("""{"type": "integer"}""");

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([first, second]),
            Throws.ArgumentException.With.Message.Contains(TestSchemas.Id));
    }

    [TestCase("{")]
    [TestCase("{\"$id\": \"urn:example:test\",}")]
    [TestCase("{\"$id\": \"urn:example:test\" /* 註解 */}")]
    public void Load_InvalidJson_ThrowsJsonException(string text)
    {
        // Act & Assert
        Assert.That(() => SchemaRegistry.Load([text]), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void Load_DuplicateKeyInSchema_ThrowsJsonException()
    {
        // Arrange
        const string text = """{"$id": "urn:example:test", "type": "string", "type": "integer"}""";

        // Act & Assert
        Assert.That(() => SchemaRegistry.Load([text]), Throws.InstanceOf<JsonException>());
    }

    [TestCase("minLength", "1")]
    [TestCase("maxLength", "1")]
    [TestCase("pattern", "\"^a\"")]
    [TestCase("anyOf", "[]")]
    [TestCase("oneOf", "[]")]
    [TestCase("allOf", "[]")]
    [TestCase("not", "{}")]
    [TestCase("if", "{}")]
    [TestCase("minimum", "0")]
    [TestCase("maxItems", "1")]
    [TestCase("uniqueItems", "true")]
    [TestCase("default", "\"x\"")]
    [TestCase("examples", "[]")]
    [TestCase("nullable", "true")]
    [TestCase("definitions", "{}")]
    [TestCase("patternProperties", "{}")]
    [TestCase("$anchor", "\"a\"")]
    [TestCase("$comment", "\"a\"")]
    [TestCase("$dynamicRef", "\"#a\"")]
    public void Load_UnsupportedKeyword_ThrowsNotSupportedNamingKeywordAndLocation(string keyword, string value)
    {
        // Arrange
        var text = TestSchemas.WithId($$$"""
            {"type": "object", "additionalProperties": false,
             "properties": {"name": {"type": "string", "{{{keyword}}}": {{{value}}} } } }
            """);

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains(keyword)
                .And.Message.Contains("#/properties/name"));
    }

    [Test]
    public void Load_UnsupportedKeywordAtRoot_NamesTheDocument()
    {
        // Arrange
        var text = TestSchemas.WithId("""{"type": "string", "minLength": 1}""");

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains("minLength")
                .And.Message.Contains(TestSchemas.Id));
    }

    [Test]
    public void Load_IgnoredAnnotations_AreAccepted()
    {
        // Arrange
        var text = TestSchemas.WithId("""
            {"$schema": "https://json-schema.org/draft/2020-12/schema",
             "title": "範例", "description": "說明",
             "type": "object", "additionalProperties": false,
             "properties": {"url": {"type": "string", "format": "uri", "title": "網址", "description": "說明"}},
             "$defs": {"unused": {"type": "string", "description": "沒有人引用也要檢查"}}}
            """);

        // Act & Assert
        Assert.That(() => SchemaRegistry.Load([text]), Throws.Nothing);
    }

    [TestCase("title", "1")]
    [TestCase("description", "{}")]
    [TestCase("format", "true")]
    public void Load_AnnotationWithNonStringValue_ThrowsNotSupported(string keyword, string value)
    {
        // Arrange
        var text = TestSchemas.WithId($$$"""{"type": "string", "{{{keyword}}}": {{{value}}}}""");

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains(keyword));
    }

    [Test]
    public void Load_SchemaKeywordWithNonStringValue_ThrowsNotSupported()
    {
        // Arrange
        var text = TestSchemas.WithId("""{"$schema": 1}""");

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("$schema"));
    }

    [TestCase("$id", "\"urn:example:inner\"")]
    [TestCase("$schema", "\"https://json-schema.org/draft/2020-12/schema\"")]
    [TestCase("$defs", "{}")]
    public void Load_RootOnlyKeywordBelowTheRoot_ThrowsNotSupported(string keyword, string value)
    {
        // Arrange
        var text = TestSchemas.WithId($$$"""
            {"type": "object", "additionalProperties": false,
             "properties": {"name": {"type": "string", "{{{keyword}}}": {{{value}}} } } }
            """);

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains(keyword)
                .And.Message.Contains("#/properties/name"));
    }

    [TestCase("[]")]
    [TestCase("\"a\"")]
    public void Load_DefsIsNotAnObject_ThrowsNotSupported(string value)
    {
        // Arrange
        var text = TestSchemas.WithId($$$"""{"$defs": {{{value}}}}""");

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("$defs"));
    }

    [Test]
    public void Load_DefinitionIsNotAnObject_ThrowsNotSupportedNamingIt()
    {
        // Arrange
        var text = TestSchemas.WithId("""{"$defs": {"flag": true}}""");

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("#/$defs/flag"));
    }

    [Test]
    public void Load_UnreferencedDefinitionWithUnsupportedKeyword_ThrowsNotSupported()
    {
        // Arrange：整份 Schema 在載入時都要檢查，沒被引用的定義也一樣。
        var text = TestSchemas.WithId("""
            {"type": "string", "$defs": {"unused": {"type": "string", "pattern": "^a"}}}
            """);

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([text]),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains("pattern")
                .And.Message.Contains("#/$defs/unused"));
    }

    [Test]
    public void Load_EmptySchema_AcceptsAnyInstance()
    {
        // Arrange
        var registry = SchemaRegistry.Load([TestSchemas.WithId("{}")]);
        var instance = Instances.From("""{"anything": [1, "two", null]}""", InstanceSource.Json);

        // Act
        var issues = StructuralValidator.Validate(instance, TestSchemas.Id, registry);

        // Assert
        Assert.That(issues, Is.Empty);
    }
}
