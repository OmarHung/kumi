using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint.Tests.Schema;

[TestFixture]
public sealed class SchemaRegistryKeywordRuleTests
{
    [TestCase("""{"type": "object", "additionalProperties": false}""")]
    [TestCase("""{"type": "array"}""")]
    [TestCase("""{"type": "string"}""")]
    [TestCase("""{"type": "integer"}""")]
    [TestCase("""{"type": "number"}""")]
    [TestCase("""{"type": "boolean"}""")]
    public void Load_EachSupportedType_Succeeds(string schema)
    {
        // Act & Assert
        Assert.That(() => TestSchemas.Registry(schema), Throws.Nothing);
    }

    [TestCase("\"null\"")]
    [TestCase("\"Object\"")]
    [TestCase("\"\"")]
    [TestCase("[\"string\", \"null\"]")]
    [TestCase("1")]
    public void Load_TypeOutsideTheSubset_ThrowsNotSupported(string type)
    {
        // Arrange
        var schema = $$$"""{"type": {{{type}}}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("type"));
    }

    [Test]
    public void Load_ObjectWithoutAdditionalProperties_ThrowsNotSupported()
    {
        // Arrange
        const string schema = """{"type": "object", "properties": {"name": {"type": "string"}}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("additionalProperties"));
    }

    [Test]
    public void Load_NestedObjectWithoutAdditionalProperties_ThrowsNotSupportedNamingTheLocation()
    {
        // Arrange
        const string schema = """
            {"type": "object", "additionalProperties": false,
             "properties": {"meta": {"type": "object"}}}
            """;

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains("additionalProperties")
                .And.Message.Contains("#/properties/meta"));
    }

    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("{\"type\": \"string\"}")]
    [TestCase("\"false\"")]
    public void Load_AdditionalPropertiesOtherThanFalse_ThrowsNotSupported(string value)
    {
        // Arrange
        var schema = $$$"""{"type": "object", "additionalProperties": {{{value}}}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("additionalProperties"));
    }

    [TestCase("""{"type": "string", "additionalProperties": false}""", "additionalProperties")]
    [TestCase("""{"additionalProperties": false}""", "additionalProperties")]
    [TestCase("""{"properties": {}}""", "properties")]
    [TestCase("""{"type": "array", "properties": {}}""", "properties")]
    [TestCase("""{"required": []}""", "required")]
    public void Load_ObjectKeywordWithoutObjectType_ThrowsNotSupported(string schema, string keyword)
    {
        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains(keyword));
    }

    [TestCase("""{"items": {"type": "string"}}""")]
    [TestCase("""{"type": "object", "additionalProperties": false, "items": {}}""")]
    public void Load_ItemsWithoutArrayType_ThrowsNotSupported(string schema)
    {
        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("items"));
    }

    [TestCase("[{\"type\": \"string\"}]")]
    [TestCase("true")]
    public void Load_ItemsIsNotASchemaObject_ThrowsNotSupported(string items)
    {
        // Arrange
        var schema = $$$"""{"type": "array", "items": {{{items}}}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("#/items"));
    }

    [Test]
    public void Load_PropertiesIsNotAnObject_ThrowsNotSupported()
    {
        // Arrange
        const string schema = """{"type": "object", "additionalProperties": false, "properties": []}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("properties"));
    }

    [Test]
    public void Load_PropertySchemaIsNotAnObject_ThrowsNotSupportedNamingTheProperty()
    {
        // Arrange
        const string schema = """{"type": "object", "additionalProperties": false, "properties": {"a": true}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("#/properties/a"));
    }

    [Test]
    public void Load_PropertyNameWithSlash_IsEscapedInTheLocation()
    {
        // Arrange
        const string schema = """
            {"type": "object", "additionalProperties": false,
             "properties": {"a/b~c": {"type": "string", "minLength": 1}}}
            """;

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("#/properties/a~1b~0c"));
    }

    [TestCase("\"a\"")]
    [TestCase("[1]")]
    [TestCase("[\"a\", \"a\"]")]
    [TestCase("[\"missing\"]")]
    public void Load_InvalidRequired_ThrowsNotSupported(string required)
    {
        // Arrange
        var schema = $$$"""
            {"type": "object", "additionalProperties": false,
             "properties": {"a": {"type": "string"}}, "required": {{{required}}}}
            """;

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("required"));
    }

    [TestCase("[]")]
    [TestCase("\"a\"")]
    [TestCase("[null]")]
    [TestCase("[{}]")]
    [TestCase("[[]]")]
    [TestCase("[1e400]")]
    public void Load_InvalidEnum_ThrowsNotSupported(string values)
    {
        // Arrange
        var schema = $$$"""{"enum": {{{values}}}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("enum"));
    }

    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("1e400")]
    public void Load_InvalidConst_ThrowsNotSupported(string value)
    {
        // Arrange
        var schema = $$$"""{"const": {{{value}}}}""";

        // Act & Assert
        Assert.That(
            () => TestSchemas.Registry(schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("const"));
    }

    [TestCase("""{"enum": ["a", 1, 1.5, true, false]}""")]
    [TestCase("""{"const": "a"}""")]
    [TestCase("""{"const": 12}""")]
    [TestCase("""{"const": false}""")]
    [TestCase("""{"type": "string", "enum": ["a"], "const": "a"}""")]
    public void Load_ScalarEnumAndConst_Succeed(string schema)
    {
        // Act & Assert
        Assert.That(() => TestSchemas.Registry(schema), Throws.Nothing);
    }

    [Test]
    public void Load_SchemaThatIsNotAnObject_ThrowsNotSupported()
    {
        // Arrange：布林形式的 Schema（true／false）不在子集裡。
        const string schema = """{"type": "array", "items": false}""";

        // Act & Assert
        Assert.That(() => TestSchemas.Registry(schema), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Load_RegistryFromEmptyCollection_KnowsNoSchemaIds()
    {
        // Arrange
        var registry = SchemaRegistry.Load([]);

        // Act & Assert
        Assert.That(
            () => StructuralValidator.Validate(null, TestSchemas.Id, registry),
            Throws.ArgumentException.With.Message.Contains(TestSchemas.Id));
    }
}
