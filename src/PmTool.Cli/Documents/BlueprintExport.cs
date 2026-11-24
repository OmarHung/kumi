using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PmTool.Blueprint;
using YamlDotNet.Serialization;

namespace PmTool.Cli.Documents;

/// <summary>
/// 把專案目前的藍圖寫成變更集文件（baseVersion 加 blueprint）：改完直接 apply，不必另外描述差異。
/// YAML 的第一行是 `# project: 代號`，plan 與 apply 沒給 --project 時用它。
/// </summary>
internal static class BlueprintExport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Render(string projectKey, long baseVersion, JsonElement blueprint, DocumentFormat format)
    {
        if (format == DocumentFormat.Json)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["baseVersion"] = baseVersion,
                ["blueprint"] = blueprint,
            }, JsonOptions) + "\n";
        }

        var tree = new OrderedDictionary<string, object?>
        {
            ["baseVersion"] = baseVersion,
            ["blueprint"] = ToTree(blueprint),
        };

        // 字串要是長得像數字、true、null 的話，YamlDotNet 會加引號，讀回來才還是字串。
        var serializer = new SerializerBuilder()
            .WithQuotingNecessaryStrings()
            .WithIndentedSequences()
            .DisableAliases()
            .Build();
        return new StringBuilder().AppendLine($"# project: {projectKey}").Append(serializer.Serialize(tree)).ToString();
    }

    private static object? ToTree(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ObjectToTree(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ToTree).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
        _ => null,
    };

    private static OrderedDictionary<string, object?> ObjectToTree(JsonElement element)
    {
        var tree = new OrderedDictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            tree[property.Name] = ToTree(property.Value);
        }

        return tree;
    }
}
