using System.Text.Json.Nodes;
using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint.Tests.Schema;

[TestFixture]
public sealed class StructuralValidatorLimitsTests
{
    [Test]
    public void Validate_VeryDeepInstanceUnderAnUnconstrainedSchema_DoesNotRecurseIntoIt()
    {
        // Arrange：遞迴深度跟著 Schema 走；Schema 沒有往下描述的地方，instance 再深也不會走進去。
        JsonNode deepest = new JsonArray();
        for (var depth = 0; depth < 100_000; depth++)
        {
            deepest = new JsonArray(deepest);
        }

        var instance = new JsonObject { ["payload"] = deepest };
        const string schema = """{"type": "object", "additionalProperties": false, "properties": {"payload": {}}}""";

        // Act
        var issues = TestSchemas.Validate(schema, instance);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_TwentyThousandBadItems_StopsAtOneMoreThanTheReportKeeps()
    {
        // Arrange：回報只留 MaxIssues 個，多的那一個是用來判斷有沒有截斷。再多收只是白白配置記憶體。
        const int count = 20_000;
        var instance = new JsonArray([.. Enumerable.Range(0, count).Select(index => (JsonNode)JsonValue.Create(index))]);

        // Act
        var issues = TestSchemas.Validate("""{"type": "array", "items": {"type": "string"}}""", instance);

        // Assert：留下的是最前面的那些，順序不變。
        Assert.That(issues, Has.Count.EqualTo(ValidationReport.MaxIssues + 1));
        Assert.That(issues[0].Path, Is.EqualTo("/0"));
        Assert.That(issues[^1].Path, Is.EqualTo($"/{ValidationReport.MaxIssues}"));
    }

    [Test]
    public void Validate_ManyObjectsEachMissingSeveralProperties_StopsInTheMiddleOfAnObject()
    {
        // Arrange：每個物件少三個必要的屬性、多一個沒定義的屬性；上限不會剛好落在物件的邊界上。
        const string schema = """
            {"type": "array", "items": {"type": "object", "additionalProperties": false,
             "required": ["key", "name", "type"],
             "properties": {"key": {"type": "string"}, "name": {"type": "string"}, "type": {"type": "string"}}}}
            """;
        var instance = new JsonArray([.. Enumerable.Range(0, 5_000).Select(_ => (JsonNode)new JsonObject { ["extra"] = 1 })]);

        // Act
        var issues = TestSchemas.Validate(schema, instance);

        // Assert：每個物件 4 個問題，第 201 個是第 51 個物件（索引 50）的第一個。
        Assert.That(issues, Has.Count.EqualTo(ValidationReport.MaxIssues + 1));
        Assert.That(issues[^2].Path, Is.EqualTo("/49/extra"));
        Assert.That(issues[^1].Path, Is.EqualTo("/50"));
    }

    [Test]
    public void Validate_SharedRegistryUsedConcurrently_GivesTheSameResultEverywhere()
    {
        // Arrange：單例的驗證器會在多個請求之間共用同一個 registry。
        var registry = SchemaRegistry.Load(EmbeddedSchemas.ReadAll());
        const string instance = """
            {"apiVersion": "blueprint/v9", "kind": "ProjectBlueprint", "metadata": {"key": "k"},
             "fields": [{"key": "a", "name": "A", "type": "text", "colour": "red"}],
             "workflows": [], "itemTypes": [{"key": "t", "name": "T"}]}
            """;
        var expected = TestSchemas.Summary(
            StructuralValidator.Validate(Instances.From(instance, InstanceSource.Json), SchemaIds.ProjectBlueprint, registry));

        // Act
        var results = new (string Code, string Path)[64][];
        Parallel.For(0, results.Length, index =>
        {
            var source = index % 2 == 0 ? InstanceSource.Json : InstanceSource.Clr;
            var issues = StructuralValidator.Validate(Instances.From(instance, source), SchemaIds.ProjectBlueprint, registry);
            results[index] = TestSchemas.Summary(issues);
        });

        // Assert
        Assert.That(expected, Has.Length.EqualTo(4));
        Assert.That(results, Has.All.EqualTo(expected));
    }
}
