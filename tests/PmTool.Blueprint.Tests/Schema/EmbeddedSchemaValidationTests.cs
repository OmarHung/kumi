using System.Text.Json.Nodes;
using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint.Tests.Schema;

/// <summary>用內嵌的兩份真正的 Schema 檢查路徑與 $ref 跨文件的行為。</summary>
[TestFixture]
public sealed class EmbeddedSchemaValidationTests
{
    private const string MinimalBlueprint = """
        {
          "apiVersion": "blueprint/v0",
          "kind": "ProjectBlueprint",
          "metadata": {"key": "example_project", "name": "Example"},
          "fields": [
            {"key": "quote_amount", "name": "Quote", "type": "currency", "currency": "TWD"}
          ],
          "workflows": [
            {"key": "delivery", "name": "Delivery",
             "states": [
               {"key": "todo", "name": "To do", "category": "not_started"},
               {"key": "done", "name": "Done", "category": "done"}
             ],
             "transitions": [{"from": "todo", "to": "done", "allowedRoles": ["admin"]}]}
          ],
          "itemTypes": [{"key": "task", "name": "Task", "workflow": "delivery", "fields": ["quote_amount"]}]
        }
        """;

    private static readonly SchemaRegistry Registry = SchemaRegistry.Load(EmbeddedSchemas.ReadAll());

    [Test]
    public void Validate_MinimalBlueprint_ReportsNothing([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateBlueprint(MinimalBlueprint, source, _ => { });

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_RoleWithItemReadOwnPermission_ReportsNothing([Values] InstanceSource source)
    {
        // Act：只看自己的項目（item.read_own）是角色可以勾的權限。
        var issues = ValidateBlueprint(MinimalBlueprint, source, root =>
            root["roles"] = new JsonArray(new JsonObject
            {
                ["key"] = "outside",
                ["name"] = "Outside",
                ["permissions"] = new JsonArray("item.read_own", "item.create"),
            }));

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_RoleWithUnknownPermission_ReportsStructureEnum([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateBlueprint(MinimalBlueprint, source, root =>
            root["roles"] = new JsonArray(new JsonObject
            {
                ["key"] = "outside",
                ["name"] = "Outside",
                ["permissions"] = new JsonArray("item.read_mine"),
            }));

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureEnum, "/roles/0/permissions/0") }));
    }

    [Test]
    public void Validate_StateCategoryNotInEnum_ReportsThePathToTheCategory([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateBlueprint(MinimalBlueprint, source,
            root => root["workflows"]![0]!["states"]![0]!["category"] = "waiting");

        // Assert
        Assert.That(
            TestSchemas.Summary(issues),
            Is.EqualTo(new[] { (IssueCodes.StructureEnum, "/workflows/0/states/0/category") }));
    }

    [Test]
    public void Validate_WrongApiVersion_ReportsStructureConst([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateBlueprint(MinimalBlueprint, source, root => root["apiVersion"] = "blueprint/v9");

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureConst, "/apiVersion") }));
    }

    [Test]
    public void Validate_FieldWithUndefinedProperty_ReportsStructureAdditionalProperty([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateBlueprint(MinimalBlueprint, source, root => root["fields"]![0]!["colour"] = "red");

        // Assert
        Assert.That(
            TestSchemas.Summary(issues),
            Is.EqualTo(new[] { (IssueCodes.StructureAdditionalProperty, "/fields/0/colour") }));
    }

    [Test]
    public void Validate_ItemTypeWithoutWorkflow_ReportsStructureRequired([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateBlueprint(MinimalBlueprint, source,
            root => root["itemTypes"]![0]!.AsObject().Remove("workflow"));

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureRequired, "/itemTypes/0") }));
        Assert.That(issues[0].Message, Does.Contain("workflow"));
    }

    [Test]
    public void Validate_AllowedRoleThatIsNotAString_ReportsTheArrayElement([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateBlueprint(MinimalBlueprint, source,
            root => root["workflows"]![0]!["transitions"]![0]!["allowedRoles"]![0] = 7);

        // Assert
        Assert.That(
            TestSchemas.Summary(issues),
            Is.EqualTo(new[] { (IssueCodes.StructureType, "/workflows/0/transitions/0/allowedRoles/0") }));
    }

    [Test]
    public void Validate_MinimalChangeSet_ReportsNothing([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateChangeSet(source, _ => { });

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_ChangeSetWithoutBaseVersion_ReportsStructureRequired([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateChangeSet(source, root => root.Remove("baseVersion"));

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureRequired, "") }));
        Assert.That(issues[0].Message, Does.Contain("baseVersion"));
    }

    [Test]
    public void Validate_ChangeSetBlueprintProblem_IsReportedUnderTheBlueprintPath([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateChangeSet(source,
            root => root["blueprint"]!["workflows"]![0]!["states"]![1]!["category"] = "waiting");

        // Assert
        Assert.That(
            TestSchemas.Summary(issues),
            Is.EqualTo(new[] { (IssueCodes.StructureEnum, "/blueprint/workflows/0/states/1/category") }));
    }

    [Test]
    public void Validate_ChangeSetMigrationProblem_FollowsTheLocalDefinition([Values] InstanceSource source)
    {
        // Act
        var issues = ValidateChangeSet(source,
            root => root["migrations"]!["convertFields"]![0]!["onFailure"] = "retry");

        // Assert
        Assert.That(
            TestSchemas.Summary(issues),
            Is.EqualTo(new[] { (IssueCodes.StructureEnum, "/migrations/convertFields/0/onFailure") }));
    }

    [TestCase("12.5")]
    [TestCase("\"12\"")]
    [TestCase("1e30")]
    public void Validate_ChangeSetBaseVersionThatIsNotAnInteger_ReportsStructureType(string baseVersion)
    {
        // Act
        var issues = ValidateChangeSet(InstanceSource.Json, root => root["baseVersion"] = JsonNode.Parse(baseVersion));

        // Assert
        Assert.That(TestSchemas.Summary(issues), Is.EqualTo(new[] { (IssueCodes.StructureType, "/baseVersion") }));
    }

    private static IReadOnlyList<ValidationIssue> ValidateBlueprint(
        string json, InstanceSource source, Action<JsonObject> breakIt)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        breakIt(root);
        var instance = Instances.From(root.ToJsonString(), source);
        return StructuralValidator.Validate(instance, SchemaIds.ProjectBlueprint, Registry);
    }

    private static IReadOnlyList<ValidationIssue> ValidateChangeSet(InstanceSource source, Action<JsonObject> breakIt)
    {
        var root = new JsonObject
        {
            ["baseVersion"] = 12,
            ["summary"] = "Example change",
            ["blueprint"] = JsonNode.Parse(MinimalBlueprint),
            ["migrations"] = JsonNode.Parse("""
                {"moveStates": [{"workflow": "delivery", "from": "review", "to": "done"}],
                 "dropFields": ["legacy_note"],
                 "convertFields": [{"field": "quote_amount", "onFailure": "leave_empty"}]}
                """),
        };
        breakIt(root);
        var instance = Instances.From(root.ToJsonString(), source);
        return StructuralValidator.Validate(instance, SchemaIds.ChangeSet, Registry);
    }
}
