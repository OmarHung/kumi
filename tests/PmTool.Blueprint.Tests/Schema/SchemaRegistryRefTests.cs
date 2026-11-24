using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint.Tests.Schema;

[TestFixture]
public sealed class SchemaRegistryRefTests
{
    private const string OtherId = "urn:example:other";

    [Test]
    public void Validate_RefToLocalDefinition_UsesTheDefinition()
    {
        // Arrange
        const string schema = """
            {"type": "object", "additionalProperties": false,
             "properties": {"name": {"$ref": "#/$defs/name"}},
             "$defs": {"name": {"type": "string"}}}
            """;
        var instance = Instances.From("""{"name": 1}""", InstanceSource.Json);

        // Act
        var issues = TestSchemas.Validate(schema, instance);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "/name") }));
    }

    [Test]
    public void Validate_RefToAnotherDocumentId_UsesThatDocument()
    {
        // Arrange
        var root = TestSchemas.WithId($$$"""
            {"type": "object", "additionalProperties": false,
             "properties": {"inner": {"$ref": "{{{OtherId}}}" } } }
            """);
        var other = TestSchemas.WithId("""
            {"type": "object", "additionalProperties": false, "required": ["key"],
             "properties": {"key": {"type": "string"}}}
            """, OtherId);
        var registry = SchemaRegistry.Load([root, other]);
        var instance = Instances.From("""{"inner": {"extra": 1}}""", InstanceSource.Json);

        // Act
        var issues = StructuralValidator.Validate(instance, TestSchemas.Id, registry);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[]
        {
            (IssueCodes.StructureRequired, "/inner"),
            (IssueCodes.StructureAdditionalProperty, "/inner/extra"),
        }));
    }

    [Test]
    public void Load_RefToDocumentListedLater_Succeeds()
    {
        // Arrange
        var root = TestSchemas.WithId($$$"""{"type": "array", "items": {"$ref": "{{{OtherId}}}"}}""");
        var other = TestSchemas.WithId("""{"type": "string"}""", OtherId);

        // Act & Assert
        Assert.That(() => SchemaRegistry.Load([root, other]), Throws.Nothing);
    }

    [Test]
    public void Validate_RefChain_FollowsEveryHop()
    {
        // Arrange
        const string schema = """
            {"type": "array", "items": {"$ref": "#/$defs/first"},
             "$defs": {"first": {"$ref": "#/$defs/second"}, "second": {"type": "string"}}}
            """;
        var instance = Instances.From("""["ok", 2]""", InstanceSource.Json);

        // Act
        var issues = TestSchemas.Validate(schema, instance);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "/1") }));
    }

    [Test]
    public void Load_SameDefinitionUsedTwice_IsNotACycle()
    {
        // Arrange
        const string schema = """
            {"type": "object", "additionalProperties": false,
             "properties": {"a": {"$ref": "#/$defs/key"}, "b": {"$ref": "#/$defs/key"}},
             "$defs": {"key": {"type": "string"}}}
            """;

        // Act & Assert
        Assert.That(() => TestSchemas.Registry(schema), Throws.Nothing);
    }

    [Test]
    public void Load_RefWithAnnotationSibling_Succeeds()
    {
        // Arrange
        const string schema = """
            {"type": "array", "items": {"$ref": "#/$defs/key", "description": "說明"},
             "$defs": {"key": {"type": "string"}}}
            """;

        // Act & Assert
        Assert.That(() => TestSchemas.Registry(schema), Throws.Nothing);
    }

    [TestCase("type", "\"string\"")]
    [TestCase("enum", "[\"a\"]")]
    [TestCase("const", "\"a\"")]
    public void Load_RefWithAnotherCheckingKeyword_ThrowsNotSupported(string keyword, string value)
    {
        // Arrange
        var schema = $$$"""
            {"type": "array", "items": {"$ref": "#/$defs/key", "{{{keyword}}}": {{{value}}}},
             "$defs": {"key": {"type": "string"} } }
            """;

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains("$ref")
                .And.Message.Contains("#/items"));
    }

    [Test]
    public void Load_RefIsNotAString_ThrowsNotSupported()
    {
        // Arrange
        const string schema = """{"type": "array", "items": {"$ref": 1}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("$ref"));
    }

    [Test]
    public void Load_RefToMissingDefinition_ThrowsNotSupportedNamingTheRef()
    {
        // Arrange
        const string schema = """
            {"type": "array", "items": {"$ref": "#/$defs/missing"}, "$defs": {"key": {"type": "string"}}}
            """;

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains("#/$defs/missing")
                .And.Message.Contains("#/items"));
    }

    [Test]
    public void Load_RefToDefinitionWhenThereAreNoDefinitions_ThrowsNotSupported()
    {
        // Arrange
        const string schema = """{"type": "array", "items": {"$ref": "#/$defs/missing"}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("#/$defs/missing"));
    }

    [Test]
    public void Load_RefToDocumentThatIsNotLoaded_ThrowsNotSupportedNamingTheId()
    {
        // Arrange
        const string schema = """{"type": "array", "items": {"$ref": "urn:example:missing"}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("urn:example:missing"));
    }

    [TestCase("#")]
    [TestCase("")]
    [TestCase("#/properties/a")]
    [TestCase("#/definitions/key")]
    [TestCase("#/$defs/")]
    [TestCase("#/$defs/key/type")]
    [TestCase("#/$defs/a~1b")]
    [TestCase("#/$defs/a%20b")]
    [TestCase("urn:example:other#/$defs/key")]
    [TestCase("urn:example:other#")]
    public void Load_RefInAnUnsupportedForm_ThrowsNotSupported(string reference)
    {
        // Arrange
        var root = TestSchemas.WithId($$$"""
            {"type": "array", "items": {"$ref": "{{{reference}}}"}, "$defs": {"key": {"type": "string"} } }
            """);
        var other = TestSchemas.WithId("""{"$defs": {"key": {"type": "string"}}}""", OtherId);

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([root, other]),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("$ref"));
    }

    [Test]
    public void Load_RefToItsOwnDocumentId_ThrowsNotSupported()
    {
        // Arrange
        var schema = $$$"""{"type": "array", "items": {"$ref": "{{{TestSchemas.Id}}}"}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("$ref"));
    }

    [Test]
    public void Load_DefinitionsReferringToEachOther_ThrowsNotSupported()
    {
        // Arrange
        const string schema = """
            {"$defs": {"a": {"$ref": "#/$defs/b"}, "b": {"$ref": "#/$defs/a"}}}
            """;

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("#/$defs/a"));
    }

    [Test]
    public void Load_DefinitionReferringToItself_ThrowsNotSupported()
    {
        // Arrange
        const string schema = """{"$defs": {"a": {"$ref": "#/$defs/a"}}}""";

        // Act & Assert
        Assert.That(() => TestSchemas.Registry(schema), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Load_RecursionThroughProperties_ThrowsNotSupported()
    {
        // Arrange：樹狀遞迴的 Schema 也算繞圈。
        const string schema = """
            {"$ref": "#/$defs/node",
             "$defs": {"node": {"type": "object", "additionalProperties": false,
                                "properties": {"child": {"$ref": "#/$defs/node"}}}}}
            """;

        // Act & Assert
        Assert.That(() => TestSchemas.Registry(schema), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Load_CycleAcrossDocuments_ThrowsNotSupported()
    {
        // Arrange
        var root = TestSchemas.WithId($$$"""{"type": "array", "items": {"$ref": "{{{OtherId}}}"}}""");
        var other = TestSchemas.WithId($$$"""{"type": "array", "items": {"$ref": "{{{TestSchemas.Id}}}"}}""", OtherId);

        // Act & Assert
        Assert.That(
            () => SchemaRegistry.Load([root, other]),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains(TestSchemas.Id)
                .And.Message.Contains(OtherId));
    }
}
