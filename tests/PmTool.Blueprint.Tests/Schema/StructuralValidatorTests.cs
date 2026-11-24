using System.Text.Json.Nodes;
using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint.Tests.Schema;

[TestFixture]
public sealed class StructuralValidatorTests
{
    private const string PersonSchema = """
        {"type": "object", "additionalProperties": false, "required": ["key", "name"],
         "properties": {
           "key": {"type": "string"},
           "name": {"type": "string"},
           "meta": {"type": "object", "additionalProperties": false, "required": ["owner"],
                    "properties": {"owner": {"type": "string"}}},
           "tags": {"type": "array", "items": {"type": "string"}}
         }}
        """;

    private static IEnumerable<TestCaseData> MatchingTypeCases() => SourceCases.WithEachSource(
        ["""{"type": "object", "additionalProperties": false}""", "{}"],
        ["""{"type": "array"}""", "[]"],
        ["""{"type": "string"}""", "\"text\""],
        ["""{"type": "string"}""", "\"\""],
        ["""{"type": "integer"}""", "12"],
        ["""{"type": "number"}""", "12.5"],
        ["""{"type": "number"}""", "12"],
        ["""{"type": "boolean"}""", "true"],
        ["""{"type": "boolean"}""", "false"]);

    [TestCaseSource(nameof(MatchingTypeCases))]
    public void Validate_ValueMatchesType_ReportsNothing(string schema, string instance, InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(schema, instance, source);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    private static IEnumerable<TestCaseData> MismatchedTypeCases() => SourceCases.WithEachSource(
        ["""{"type": "object", "additionalProperties": false}""", "[]"],
        ["""{"type": "object", "additionalProperties": false}""", "\"{}\""],
        ["""{"type": "object", "additionalProperties": false}""", "null"],
        ["""{"type": "array"}""", "{}"],
        ["""{"type": "array"}""", "\"[]\""],
        ["""{"type": "string"}""", "1"],
        ["""{"type": "string"}""", "true"],
        ["""{"type": "string"}""", "null"],
        ["""{"type": "string"}""", "{}"],
        ["""{"type": "integer"}""", "12.5"],
        ["""{"type": "integer"}""", "\"12\""],
        ["""{"type": "integer"}""", "true"],
        ["""{"type": "number"}""", "\"1\""],
        ["""{"type": "number"}""", "[]"],
        ["""{"type": "boolean"}""", "\"true\""],
        ["""{"type": "boolean"}""", "0"],
        ["""{"type": "boolean"}""", "null"]);

    [TestCaseSource(nameof(MismatchedTypeCases))]
    public void Validate_ValueDoesNotMatchType_ReportsStructureTypeAtTheRoot(
        string schema, string instance, InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(schema, instance, source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "") }));
    }

    [Test]
    public void Validate_NullInstanceAgainstEmptySchema_ReportsNothing([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate("{}", "null", source);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_NullInstanceAgainstConst_ReportsStructureConst([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate("""{"const": "a"}""", "null", source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureConst, "") }));
    }

    [Test]
    public void Validate_NullInstanceAgainstEnum_ReportsStructureEnum([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate("""{"enum": ["a", "b"]}""", "null", source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureEnum, "") }));
    }

    [Test]
    public void Validate_TypeMismatch_DoesNotCheckEnumOrConstOfTheSameNode([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate("""{"type": "string", "enum": ["a"], "const": "a"}""", "1", source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "") }));
    }

    [Test]
    public void Validate_TypeMismatch_DoesNotDescendIntoTheNode([Values] InstanceSource source)
    {
        // Arrange：meta 應該是物件；給陣列時，陣列裡的東西不再檢查。
        const string instance = """{"key": "k", "name": "n", "meta": [{"bogus": 1}, 2]}""";

        // Act
        var issues = TestSchemas.Validate(PersonSchema, instance, source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "/meta") }));
    }

    [Test]
    public void Validate_PropertyWithNullValue_ReportsStructureTypeAtTheProperty([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(PersonSchema, """{"key": "k", "name": null}""", source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "/name") }));
    }

    [Test]
    public void Validate_ValidInstance_ReportsNothing([Values] InstanceSource source)
    {
        // Arrange
        const string instance = """{"key": "k", "name": "n", "meta": {"owner": "o"}, "tags": ["a", "b"]}""";

        // Act
        var issues = TestSchemas.Validate(PersonSchema, instance, source);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_MissingRequiredProperty_ReportsStructureRequiredOnTheObject([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(PersonSchema, """{"key": "k"}""", source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureRequired, "") }));
        Assert.That(issues[0].Message, Does.Contain("name"));
    }

    [Test]
    public void Validate_SeveralMissingRequiredProperties_ReportsThemInSchemaOrder([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(PersonSchema, "{}", source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[]
        {
            (IssueCodes.StructureRequired, ""),
            (IssueCodes.StructureRequired, ""),
        }));
        Assert.That(issues[0].Message, Does.Contain("key"));
        Assert.That(issues[1].Message, Does.Contain("name"));
    }

    [Test]
    public void Validate_MissingRequiredPropertyInNestedObject_ReportsTheNestedPath([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(PersonSchema, """{"key": "k", "name": "n", "meta": {}}""", source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureRequired, "/meta") }));
        Assert.That(issues[0].Message, Does.Contain("owner"));
    }

    [Test]
    public void Validate_UndefinedProperty_ReportsStructureAdditionalPropertyAtThatProperty(
        [Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(PersonSchema, """{"key": "k", "name": "n", "colour": "red"}""", source);

        // Assert
        Assert.That(
            TestSchemas.Summary(issues),
            Is.EqualTo(new[] { (IssueCodes.StructureAdditionalProperty, "/colour") }));
        Assert.That(issues[0].Message, Does.Contain("colour"));
    }

    [Test]
    public void Validate_UndefinedPropertyInNestedObject_ReportsTheNestedPath([Values] InstanceSource source)
    {
        // Arrange
        const string instance = """{"key": "k", "name": "n", "meta": {"owner": "o", "colour": {"deep": [1]}}}""";

        // Act
        var issues = TestSchemas.Validate(PersonSchema, instance, source);

        // Assert：多出來的屬性只回報一次，不往裡面檢查。
        Assert.That(
            TestSchemas.Summary(issues),
            Is.EqualTo(new[] { (IssueCodes.StructureAdditionalProperty, "/meta/colour") }));
    }

    [Test]
    public void Validate_PropertyNameWithSlashAndTilde_IsEscapedInThePath([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(PersonSchema, """{"key": "k", "name": "n", "a/b~c": 1}""", source);

        // Assert
        Assert.That(
            TestSchemas.Summary(issues),
            Is.EqualTo(new[] { (IssueCodes.StructureAdditionalProperty, "/a~1b~0c") }));
    }

    [Test]
    public void Validate_VeryLongUndefinedPropertyName_KeepsTheFullPathButTruncatesTheMessage()
    {
        // Arrange
        var name = new string('x', 100_000);
        var instance = new JsonObject { ["key"] = "k", ["name"] = "n", [name] = 1 };

        // Act
        var issues = TestSchemas.Validate(PersonSchema, instance);

        // Assert
        Assert.That(issues.Single().Path, Is.EqualTo("/" + name));
        Assert.That(issues.Single().Message, Has.Length.LessThan(1_000));
    }

    [Test]
    public void Validate_ArrayItems_ReportsEachBadItemByIndex([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(
            PersonSchema, """{"key": "k", "name": "n", "tags": ["a", 1, "b", true]}""", source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[]
        {
            (IssueCodes.StructureType, "/tags/1"),
            (IssueCodes.StructureType, "/tags/3"),
        }));
    }

    [Test]
    public void Validate_ArrayWithoutItems_AcceptsAnyElements([Values] InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate("""{"type": "array"}""", """[1, "a", {}, null, [true]]""", source);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    private static IEnumerable<TestCaseData> EnumCases() => SourceCases.WithEachSource(
        ["\"red\"", true],
        ["\"green\"", true],
        ["\"blue\"", false],
        ["\"Red\"", false],
        ["1", true],
        ["1.0", true],
        ["1.5", true],
        ["2", false],
        ["\"1\"", false],
        ["true", true],
        ["false", false],
        ["\"true\"", false],
        ["{}", false],
        ["[]", false]);

    [TestCaseSource(nameof(EnumCases))]
    public void Validate_Enum_AcceptsOnlyListedValues(string instance, bool expectedValid, InstanceSource source)
    {
        // Arrange
        const string schema = """{"enum": ["red", "green", 1, 1.5, true]}""";

        // Act
        var issues = TestSchemas.Validate(schema, instance, source);

        // Assert
        var expected = expectedValid ? [] : new[] { (IssueCodes.StructureEnum, "") };
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(expected));
    }

    private static IEnumerable<TestCaseData> ConstCases() => SourceCases.WithEachSource(
        ["""{"const": "blueprint/v0"}""", "\"blueprint/v0\"", true],
        ["""{"const": "blueprint/v0"}""", "\"blueprint/v9\"", false],
        ["""{"const": "blueprint/v0"}""", "{}", false],
        ["""{"const": 12}""", "12", true],
        ["""{"const": 12}""", "12.0", true],
        ["""{"const": 12}""", "1.2e1", true],
        ["""{"const": 12}""", "12.5", false],
        ["""{"const": 12}""", "\"12\"", false],
        ["""{"const": 0.1}""", "0.1", true],
        ["""{"const": false}""", "false", true],
        ["""{"const": false}""", "true", false],
        ["""{"const": false}""", "0", false]);

    [TestCaseSource(nameof(ConstCases))]
    public void Validate_Const_AcceptsOnlyTheSameValue(
        string schema, string instance, bool expectedValid, InstanceSource source)
    {
        // Act
        var issues = TestSchemas.Validate(schema, instance, source);

        // Assert
        var expected = expectedValid ? [] : new[] { (IssueCodes.StructureConst, "") };
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(expected));
    }

    [Test]
    public void Validate_VeryLongValueNotInEnum_TruncatesTheMessage()
    {
        // Arrange
        var instance = JsonValue.Create(new string('x', 100_000));

        // Act
        var issues = TestSchemas.Validate("""{"enum": ["red"]}""", instance);

        // Assert
        Assert.That(issues.Single().Message, Has.Length.LessThan(1_000));
    }

    [Test]
    public void Validate_StringBackedByAnotherClrType_IsComparedByItsJsonText()
    {
        // Arrange：Guid 的 JsonValue 背後不是 string，但在 JSON 裡是字串。
        var instance = JsonValue.Create(Guid.Empty);

        // Act
        var issues = TestSchemas.Validate("""{"type": "string", "const": "00000000-0000-0000-0000-000000000000"}""", instance);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_ManyProblems_ReportsThemInAFixedOrder([Values] InstanceSource source)
    {
        // Arrange：物件本身的 required 先回報，接著依文件順序處理每個屬性（深度優先）。
        const string instance = """
            {"meta": {"colour": 1}, "extra": 1, "key": 5, "tags": [1, "ok", 2]}
            """;

        // Act
        var issues = TestSchemas.Validate(PersonSchema, instance, source);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[]
        {
            (IssueCodes.StructureRequired, ""),
            (IssueCodes.StructureRequired, "/meta"),
            (IssueCodes.StructureAdditionalProperty, "/meta/colour"),
            (IssueCodes.StructureAdditionalProperty, "/extra"),
            (IssueCodes.StructureType, "/key"),
            (IssueCodes.StructureType, "/tags/0"),
            (IssueCodes.StructureType, "/tags/2"),
        }));
    }

    [Test]
    public void Validate_SameInputTwice_ReturnsTheSameIssues([Values] InstanceSource source)
    {
        // Arrange
        var registry = TestSchemas.Registry(PersonSchema);
        const string instance = """{"meta": {"colour": 1}, "extra": 1, "tags": [1]}""";

        // Act
        var first = StructuralValidator.Validate(Instances.From(instance, source), TestSchemas.Id, registry);
        var second = StructuralValidator.Validate(Instances.From(instance, source), TestSchemas.Id, registry);

        // Assert
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Validate_UnknownSchemaId_ThrowsArgumentExceptionNamingTheId()
    {
        // Arrange
        var registry = TestSchemas.Registry("{}");

        // Act & Assert
        Assert.That(
            () => StructuralValidator.Validate(null, "urn:example:missing", registry),
            Throws.ArgumentException.With.Message.Contains("urn:example:missing"));
    }

    [Test]
    public void Validate_NullRegistry_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.That(() => StructuralValidator.Validate(null, TestSchemas.Id, null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Validate_NullSchemaId_ThrowsArgumentNullException()
    {
        // Arrange
        var registry = TestSchemas.Registry("{}");

        // Act & Assert
        Assert.That(() => StructuralValidator.Validate(null, null!, registry), Throws.ArgumentNullException);
    }
}
