using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Tests.Schema;

/// <summary>
/// integer 的定義：JSON 數字、沒有小數部分、落在 Int64 的範圍內。
/// JSON 來源看原文；YAML 來源的值背後是 long 或 double，兩邊的結論要一致。
/// </summary>
[TestFixture]
public sealed class StructuralValidatorNumberTests
{
    private const string IntegerSchema = """{"type": "integer"}""";
    private const string NumberSchema = """{"type": "number"}""";

    [TestCase("12")]
    [TestCase("-12")]
    [TestCase("0")]
    [TestCase("-0")]
    [TestCase("12.0")]
    [TestCase("1.2e1")]
    [TestCase("1E2")]
    [TestCase("0.0")]
    [TestCase("9223372036854775807")]
    [TestCase("-9223372036854775808")]
    [TestCase("-0.0")]
    [TestCase("1200e-2")]
    [TestCase("0.0012e4")]
    [TestCase("1e18")]
    [TestCase("12.000000000000000000000000000000")]
    [TestCase("9223372036854775807.000")]
    [TestCase("922337203685477580.7e1")]
    [TestCase("-9223372036854775808.0")]
    [TestCase("0.00000000000000000009223372036854775807e38")]
    [TestCase("0e99999999999999999999")]
    [TestCase("0.000e-99999999999999999999")]
    public void Validate_IntegerValuedJsonNumber_IsAnInteger(string json)
    {
        // Act
        var issues = TestSchemas.Validate(IntegerSchema, json, InstanceSource.Json);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [TestCase("12.5")]
    [TestCase("-0.5")]
    [TestCase("1e-2")]
    [TestCase("1e30")]
    [TestCase("9223372036854775808")]
    [TestCase("-9223372036854775809")]
    [TestCase("1e400")]
    [TestCase("1e19")]
    [TestCase("9223372036854775807.5")]
    [TestCase("-9223372036854775808.5")]
    [TestCase("0.00000000000000000009223372036854775807e39")]
    [TestCase("1e99999999999999999999")]
    [TestCase("1e-99999999999999999999")]
    public void Validate_JsonNumberWithFractionOrOutsideInt64_IsNotAnInteger(string json)
    {
        // Act
        var issues = TestSchemas.Validate(IntegerSchema, json, InstanceSource.Json);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "") }));
    }

    [TestCase("1e-30")]
    [TestCase("0.00000000000000000000000000001")]
    [TestCase("12.000000000000000000000000000001")]
    [TestCase("0.9999999999999999999999999999999")]
    [TestCase("9223372036854775806.99999999999")]
    public void Validate_JsonNumberWhoseFractionIsBeyondDecimalPrecision_IsStillNotAnInteger(string json)
    {
        // Arrange：這些數的小數部分超出 decimal 與 double 的精度，用它們讀會被進位成整數。判斷要看原始的文字。

        // Act
        var issues = TestSchemas.Validate(IntegerSchema, json, InstanceSource.Json);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "") }));
    }

    private static IEnumerable<TestCaseData> IntegerClrValues() =>
    [
        new(12L),
        new(long.MaxValue),
        new(long.MinValue),
        new(12.0),
        new(-0.0),
        new(-9223372036854775808.0),
        new(4611686018427387904.0),
        new(12),
        new(12.0m),
        new(12f),
    ];

    [TestCaseSource(nameof(IntegerClrValues))]
    public void Validate_IntegerValuedClrNumber_IsAnInteger(object value)
    {
        // Act
        var issues = TestSchemas.Validate(IntegerSchema, ClrValue(value));

        // Assert
        Assert.That(issues, Is.Empty);
    }

    private static IEnumerable<TestCaseData> NonIntegerClrValues() =>
    [
        new(12.5),
        new(1e30),
        new(9223372036854775808.0),
        new(-9223372036854777856.0),
        new(double.NaN),
        new(double.PositiveInfinity),
        new(double.NegativeInfinity),
        new(float.NaN),
        new(12.5f),
        new(12.5m),
    ];

    [TestCaseSource(nameof(NonIntegerClrValues))]
    public void Validate_ClrNumberWithFractionOrOutsideInt64_IsNotAnInteger(object value)
    {
        // Act
        var issues = TestSchemas.Validate(IntegerSchema, ClrValue(value));

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "") }));
    }

    [TestCase("12")]
    [TestCase("12.5")]
    [TestCase("-1e-3")]
    [TestCase("1e30")]
    public void Validate_FiniteJsonNumber_IsANumber(string json)
    {
        // Act
        var issues = TestSchemas.Validate(NumberSchema, json, InstanceSource.Json);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [TestCase("1e400")]
    [TestCase("-1e400")]
    public void Validate_JsonNumberTooLargeForDouble_IsNotANumber(string json)
    {
        // Arrange：YAML 來源的同一個值會變成無限大，兩邊都要擋。

        // Act
        var issues = TestSchemas.Validate(NumberSchema, json, InstanceSource.Json);

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "") }));
    }

    private static IEnumerable<TestCaseData> NumberClrValues() =>
    [
        new(12L),
        new(12.5),
        new(1e30),
        new(12),
        new(12.5m),
    ];

    [TestCaseSource(nameof(NumberClrValues))]
    public void Validate_FiniteClrNumber_IsANumber(object value)
    {
        // Act
        var issues = TestSchemas.Validate(NumberSchema, ClrValue(value));

        // Assert
        Assert.That(issues, Is.Empty);
    }

    private static IEnumerable<TestCaseData> NonFiniteClrValues() =>
    [
        new(double.NaN),
        new(double.PositiveInfinity),
        new(double.NegativeInfinity),
        new(float.PositiveInfinity),
    ];

    [TestCaseSource(nameof(NonFiniteClrValues))]
    public void Validate_NonFiniteClrNumber_IsNotANumber(object value)
    {
        // Act
        var issues = TestSchemas.Validate(NumberSchema, ClrValue(value));

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "") }));
    }

    [Test]
    public void Validate_NonFiniteClrNumberAgainstConst_ReportsStructureConst()
    {
        // Act
        var issues = TestSchemas.Validate("""{"const": 1}""", ClrValue(double.NaN));

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureConst, "") }));
    }

    private static IEnumerable<TestCaseData> EqualClrValues() =>
    [
        new("12", 12L),
        new("12", 12.0),
        new("12", 12),
        new("12", 12.0m),
        new("12.5", 12.5),
        new("12.5", 12.5m),
        new("0.1", 0.1),
        new("1e30", 1e30),
    ];

    [TestCaseSource(nameof(EqualClrValues))]
    public void Validate_ClrNumberEqualToConst_ReportsNothing(string constJson, object value)
    {
        // Arrange
        var schema = $$$"""{"const": {{{constJson}}}}""";

        // Act
        var issues = TestSchemas.Validate(schema, ClrValue(value));

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_TwoToThe63FromJsonAndFromDouble_AreTheSameNumber()
    {
        // Arrange：2^63 不是 Int64，但仍然是同一個數值。
        const string schema = """{"enum": [9223372036854775808]}""";

        // Act
        var fromJson = TestSchemas.Validate(schema, "9223372036854775808", InstanceSource.Json);
        var fromDouble = TestSchemas.Validate(schema, ClrValue(9223372036854775808.0));

        // Assert
        Assert.That(fromJson, Is.Empty);
        Assert.That(fromDouble, Is.Empty);
    }

    private static JsonValue ClrValue(object value) => value switch
    {
        long integer => JsonValue.Create(integer),
        int small => JsonValue.Create(small),
        double real => JsonValue.Create(real),
        float single => JsonValue.Create(single),
        decimal exact => JsonValue.Create(exact),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
