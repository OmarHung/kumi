using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint.Documents;

namespace PmTool.Blueprint.Tests.Documents;

[TestFixture]
public sealed class YamlScalarTests
{
    [Test]
    public void Read_BaseVersionTwelve_ReadsAsInteger()
    {
        // Act
        var value = ReadValue("baseVersion", "12");

        // Assert
        Assert.That(AsLong(value), Is.EqualTo(12));
    }

    [Test]
    public void Read_QuotedVersion_ReadsAsString()
    {
        // Act
        var value = ReadValue("version", "\"1.0.0\"");

        // Assert
        Assert.That(AsString(value), Is.EqualTo("1.0.0"));
    }

    [Test]
    public void Read_EnabledTrue_ReadsAsBoolean()
    {
        // Act
        var value = ReadValue("enabled", "true");

        // Assert
        Assert.That(value!.GetValueKind(), Is.EqualTo(JsonValueKind.True));
    }

    [TestCase("12", 12L)]
    [TestCase("-3", -3L)]
    [TestCase("+7", 7L)]
    [TestCase("007", 7L)]
    [TestCase("0", 0L)]
    [TestCase("0o17", 15L)]
    [TestCase("0x1F", 31L)]
    [TestCase("0x1f", 31L)]
    [TestCase("9223372036854775807", long.MaxValue)]
    [TestCase("-9223372036854775808", long.MinValue)]
    [TestCase("0x7FFFFFFFFFFFFFFF", long.MaxValue)]
    [TestCase("0o777777777777777777777", long.MaxValue)]
    public void Read_PlainInteger_BecomesLong(string scalar, long expected)
    {
        // Act
        var value = ReadValue("a", scalar);

        // Assert
        Assert.That(AsLong(value), Is.EqualTo(expected));
    }

    [TestCase("9223372036854775808")]
    [TestCase("-9223372036854775809")]
    [TestCase("99999999999999999999999")]
    [TestCase("0x8000000000000000")]
    [TestCase("0o1000000000000000000000")]
    public void Read_PlainIntegerThatOverflowsLong_StaysString(string scalar)
    {
        // Act
        var value = ReadValue("a", scalar);

        // Assert
        Assert.That(AsString(value), Is.EqualTo(scalar));
    }

    [TestCase("1.5", 1.5)]
    [TestCase("-.5", -0.5)]
    [TestCase("+2.", 2.0)]
    [TestCase("1e3", 1000.0)]
    [TestCase("6.02E+23", 6.02e23)]
    [TestCase("1.0", 1.0)]
    [TestCase("2.5e-3", 0.0025)]
    public void Read_PlainFloat_BecomesDouble(string scalar, double expected)
    {
        // Act
        var value = ReadValue("a", scalar);

        // Assert
        Assert.That(value!.AsValue().TryGetValue<double>(out var number) ? number : (double?)null, Is.EqualTo(expected));
    }

    [TestCase(".inf")]
    [TestCase("-.inf")]
    [TestCase("+.INF")]
    [TestCase(".Inf")]
    [TestCase(".nan")]
    [TestCase(".NaN")]
    [TestCase(".NAN")]
    [TestCase("1e999")]
    [TestCase("-1e999")]
    [TestCase("1.8e308")]
    public void Read_PlainFloatThatJsonCannotRepresent_StaysString(string scalar)
    {
        // Act
        var value = ReadValue("a", scalar);

        // Assert
        Assert.That(AsString(value), Is.EqualTo(scalar));
    }

    [TestCase("true", true)]
    [TestCase("True", true)]
    [TestCase("TRUE", true)]
    [TestCase("false", false)]
    [TestCase("False", false)]
    [TestCase("FALSE", false)]
    public void Read_PlainBoolean_BecomesBoolean(string scalar, bool expected)
    {
        // Act
        var value = ReadValue("a", scalar);

        // Assert
        Assert.That(value!.AsValue().TryGetValue<bool>(out var flag) ? flag : (bool?)null, Is.EqualTo(expected));
    }

    [TestCase("yes")]
    [TestCase("no")]
    [TestCase("on")]
    [TestCase("off")]
    [TestCase("y")]
    [TestCase("n")]
    [TestCase("Yes")]
    [TestCase("OFF")]
    [TestCase("tRUE")]
    public void Read_WordsThatYaml11TreatedAsBooleans_StayStrings(string scalar)
    {
        // Act
        var value = ReadValue("a", scalar);

        // Assert
        Assert.That(AsString(value), Is.EqualTo(scalar));
    }

    [TestCase("null")]
    [TestCase("Null")]
    [TestCase("NULL")]
    [TestCase("~")]
    [TestCase("")]
    public void Read_PlainNull_BecomesNull(string scalar)
    {
        // Arrange
        var text = scalar.Length == 0 ? "a:\n" : $"a: {scalar}\n";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(root.TryGetPropertyValue("a", out var value) && value is null, Is.True);
    }

    [TestCase("'12'", "12")]
    [TestCase("\"true\"", "true")]
    [TestCase("'null'", "null")]
    [TestCase("\"~\"", "~")]
    [TestCase("''", "")]
    [TestCase("\"1e3\"", "1e3")]
    [TestCase("'.inf'", ".inf")]
    [TestCase("|\n  12\n", "12\n")]
    [TestCase(">\n  true\n", "true\n")]
    [TestCase("|-\n  ~\n", "~")]
    public void Read_QuotedOrBlockScalar_StaysString(string scalar, string expected)
    {
        // Act
        var value = ReadValue("a", scalar);

        // Assert
        Assert.That(AsString(value), Is.EqualTo(expected));
    }

    [TestCase("1.0.0")]
    [TestCase("0x")]
    [TestCase("0o8")]
    [TestCase("1_000")]
    [TestCase("0b101")]
    [TestCase("1e")]
    [TestCase("e3")]
    [TestCase("-0x1")]
    [TestCase(".5.")]
    [TestCase("12abc")]
    [TestCase("~a")]
    [TestCase("nulls")]
    [TestCase("agency_project")]
    public void Read_OtherPlainText_StaysString(string scalar)
    {
        // Act
        var value = ReadValue("a", scalar);

        // Assert
        Assert.That(AsString(value), Is.EqualTo(scalar));
    }

    [Test]
    public void Read_TreeWithEveryKindOfScalar_SerializesToJson()
    {
        // Arrange
        const string text = "i: 12\nf: 1.5\nb: true\nn: ~\ns: x\ninf: .inf\nnan: .nan\nbig: 1e999\nhuge: 9223372036854775808\n";

        // Act
        var result = DocumentReader.Read(text, DocumentFormat.Yaml);

        // Assert
        var root = ReadAssert.SucceedsWithObject(result);
        Assert.That(
            root.ToJsonString(),
            Is.EqualTo("{\"i\":12,\"f\":1.5,\"b\":true,\"n\":null,\"s\":\"x\",\"inf\":\".inf\",\"nan\":\".nan\",\"big\":\"1e999\",\"huge\":\"9223372036854775808\"}"));
    }

    private static JsonNode? ReadValue(string key, string scalar)
    {
        var root = ReadAssert.SucceedsWithObject(DocumentReader.Read($"{key}: {scalar}\n", DocumentFormat.Yaml));
        Assert.That(root.ContainsKey(key), Is.True);
        return root[key];
    }

    private static long? AsLong(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
