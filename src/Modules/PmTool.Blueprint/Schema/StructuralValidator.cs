using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Schema;

/// <summary>依 Schema 檢查文件結構，回報 structure.* 問題。規格見交接文件的工作包 B。</summary>
/// <remarks>
/// 回報的順序固定，等於深度優先、依文件順序走一遍：
/// 同一個節點先看 type（不符就只回報這一個，也不往下走），再看 const，再看 enum；
/// 物件先回報自己缺少的 required（依 Schema 的順序），再依文件順序處理每個屬性，
/// 多出來的屬性就地回報、不往下走，有定義的屬性遞迴處理；陣列依索引順序處理每個元素。
/// <para>
/// 收集到 <see cref="ValidationReport.MaxIssues"/> + 1 個問題就停。回報只留 MaxIssues 個，多的那一個是讓它知道有沒有截斷；
/// 一份幾十 KB 的文件可以有好幾萬個問題，全部產生出來再丟掉，要配置的記憶體是文件的幾百倍。
/// </para>
/// </remarks>
internal static class StructuralValidator
{
    private const int IssueBudget = ValidationReport.MaxIssues + 1;

    public static IReadOnlyList<ValidationIssue> Validate(JsonNode? instance, string schemaId, SchemaRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var root = registry.GetRoot(schemaId);
        var issues = new IssueCollector();
        Visit(instance, root, string.Empty, issues);
        return issues.ToReadOnlyList();
    }

    // 每個 instance 節點只會對到一個 Schema 節點，所以總工作量和節點數成正比。
    // 遞迴深度跟著 Schema 走（$ref 不會繞圈、沒定義的屬性不往下走），不受 instance 的深度影響。
    private static void Visit(JsonNode? node, SchemaNode schema, string path, IssueCollector issues)
    {
        if (schema.Type is { } type && !InstanceTypes.Matches(node, type))
        {
            issues.Add(StructuralIssues.TypeMismatch(path, type, node));
            return;
        }

        if (schema.ConstValue is { } expected && !(ScalarValue.TryRead(node, out var actual) && actual == expected))
        {
            issues.Add(StructuralIssues.ConstMismatch(path, expected, node));
        }

        if (schema.EnumValues is { } allowed && !(ScalarValue.TryRead(node, out var value) && allowed.Contains(value)))
        {
            issues.Add(StructuralIssues.NotInEnum(path, allowed, node));
        }

        if (node is JsonObject obj && schema.Type == SchemaType.Object)
        {
            VisitObject(obj, schema, path, issues);
        }
        else if (node is JsonArray array && schema.Items is { } items)
        {
            VisitArray(array, items, path, issues);
        }
    }

    private static void VisitObject(JsonObject obj, SchemaNode schema, string path, IssueCollector issues)
    {
        foreach (var name in schema.Required)
        {
            if (!obj.ContainsKey(name))
            {
                issues.Add(StructuralIssues.MissingRequired(path, name));
            }
        }

        foreach (var (name, child) in obj)
        {
            if (issues.IsFull)
            {
                return;
            }

            var childPath = JsonPointer.Append(path, name);
            if (schema.Properties.TryGetValue(name, out var childSchema))
            {
                Visit(child, childSchema, childPath, issues);
            }
            else
            {
                issues.Add(StructuralIssues.AdditionalProperty(childPath, name, schema.PropertyNames));
            }
        }
    }

    private static void VisitArray(JsonArray array, SchemaNode items, string path, IssueCollector issues)
    {
        for (var index = 0; index < array.Count && !issues.IsFull; index++)
        {
            Visit(array[index], items, JsonPointer.Append(path, index), issues);
        }
    }

    /// <summary>滿了之後不再收，走訪的迴圈看到滿了就提早結束。</summary>
    private sealed class IssueCollector
    {
        private readonly List<ValidationIssue> _issues = [];

        public bool IsFull => _issues.Count >= IssueBudget;

        public void Add(ValidationIssue issue)
        {
            if (!IsFull)
            {
                _issues.Add(issue);
            }
        }

        public IReadOnlyList<ValidationIssue> ToReadOnlyList() => _issues.AsReadOnly();
    }
}
