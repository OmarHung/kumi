using System.Text.Json.Nodes;
using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint.Tests.Schema;

internal static class TestSchemas
{
    public const string Id = "urn:example:test";

    /// <summary>大部分測試只有一份 Schema；$id 由這裡補上，讓每個測試只寫和它有關的部分。</summary>
    public static string WithId(string schemaJson, string id = Id)
    {
        var schema = JsonNode.Parse(schemaJson)!.AsObject();
        schema["$id"] = id;
        return schema.ToJsonString();
    }

    public static SchemaRegistry Registry(string schemaJson) => SchemaRegistry.Load([WithId(schemaJson)]);

    public static IReadOnlyList<ValidationIssue> Validate(string schemaJson, JsonNode? instance) =>
        StructuralValidator.Validate(instance, Id, Registry(schemaJson));

    public static IReadOnlyList<ValidationIssue> Validate(string schemaJson, string instanceJson, InstanceSource source) =>
        Validate(schemaJson, Instances.From(instanceJson, source));

    public static (string Code, string Path)[] Summary(IEnumerable<ValidationIssue> issues) =>
        [.. issues.Select(issue => (issue.Code, issue.Path))];
}
