using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Tests;

[TestFixture]
public sealed class ModelBinderTests
{
    private const string MinimalBlueprint =
        """{"apiVersion":"blueprint/v0","kind":"ProjectBlueprint","metadata":{"key":"demo","name":"Demo"},"fields":[],"workflows":[],"itemTypes":[]}""";

    [TestCase("12", 12L)]
    [TestCase("12.0", 12L)]
    [TestCase("1.2e1", 12L)]
    [TestCase("-0.0", 0L)]
    [TestCase("1E+18", 1_000_000_000_000_000_000L)]
    [TestCase("9223372036854775807", long.MaxValue)]
    [TestCase("9223372036854775807.0", long.MaxValue)]
    [TestCase("-9223372036854775808", long.MinValue)]
    public void Bind_IntegerValuedJsonNumberInAnyNotation_ReadsItAsLong(string literal, long expected)
    {
        // Arrange：結構檢查把這些寫法都算成 integer，綁定必須讀得進去。
        var node = JsonNode.Parse($$"""{"baseVersion":{{literal}},"blueprint":{{MinimalBlueprint}}}""")!;

        // Act
        var changeSet = ModelBinder.Bind<ChangeSetRequest>(node);

        // Assert
        Assert.That(changeSet.BaseVersion, Is.EqualTo(expected));
    }

    [TestCase(12.0, 12L)]
    [TestCase(1000.0, 1000L)]
    [TestCase(1e18, 1_000_000_000_000_000_000L)]
    [TestCase(-9223372036854775808.0, long.MinValue)]
    public void Bind_IntegerValuedDoubleFromYaml_ReadsItAsLong(double value, long expected)
    {
        // Arrange：YAML 的浮點寫法會變成背後是 double 的值，寫成 JSON 文字時可能是科學記號。
        var node = new JsonObject
        {
            ["baseVersion"] = JsonValue.Create(value),
            ["blueprint"] = JsonNode.Parse(MinimalBlueprint),
        };

        // Act
        var changeSet = ModelBinder.Bind<ChangeSetRequest>(node);

        // Assert
        Assert.That(changeSet.BaseVersion, Is.EqualTo(expected));
    }

    [TestCase("12.5")]
    [TestCase("1e30")]
    [TestCase("9223372036854775808")]
    [TestCase("\"12\"")]
    public void Bind_ValueThatIsNotAnInt64_ThrowsInsteadOfGuessing(string literal)
    {
        // Arrange：這些值過不了結構檢查，正常流程到不了綁定；真的到了就要丟例外，不能悄悄捨去或截斷。
        var node = JsonNode.Parse($$"""{"baseVersion":{{literal}},"blueprint":{{MinimalBlueprint}}}""")!;

        // Act
        TestDelegate bind = () => ModelBinder.Bind<ChangeSetRequest>(node);

        // Assert
        Assert.That(bind, Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void Bind_EnumValues_ReadsTheSnakeCaseNamesUsedInDocuments()
    {
        // Arrange
        var node = JsonNode.Parse(
            """
            {
              "apiVersion": "blueprint/v0",
              "kind": "ProjectBlueprint",
              "metadata": { "key": "demo", "name": "Demo" },
              "fields": [{ "key": "impact", "name": "Impact", "type": "single_select", "options": [] }],
              "workflows": [{ "key": "main", "name": "Main", "states": [{ "key": "todo", "name": "Todo", "category": "not_started" }] }],
              "itemTypes": []
            }
            """)!;

        // Act
        var blueprint = ModelBinder.Bind<ProjectBlueprint>(node);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blueprint.Fields[0].Type, Is.EqualTo(FieldType.SingleSelect));
            Assert.That(blueprint.Fields[0].Options, Is.Empty);
            Assert.That(blueprint.Workflows[0].States[0].Category, Is.EqualTo(StateCategory.NotStarted));
            Assert.That(blueprint.Workflows[0].Transitions, Is.Null);
        }
    }
}
