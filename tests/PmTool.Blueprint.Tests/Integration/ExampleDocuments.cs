using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint.Documents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PmTool.Blueprint.Tests.Integration;

/// <summary>
/// 整合測試的素材都從 schemas/blueprint/v0/examples 的範例檔來：讀成樹、改一處、再寫回 JSON 與 YAML 兩種文字，
/// 讓同一份內容可以用兩種格式各送一次。
/// </summary>
internal static class ExampleDocuments
{
    public const string BlueprintFile = "agency-project.blueprint.yaml";
    public const string ChangeSetFile = "split-acceptance.changeset.yaml";

    /// <summary>ChangeSetFile 的基準：「官網改版」的第 12 版藍圖。</summary>
    public const string CurrentBlueprintFile = "website-redesign.blueprint.yaml";

    private static readonly JsonSerializerOptions ReadableJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static string ReadYaml(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Examples", fileName));

    /// <summary>把範例讀成可以修改的樹。每次呼叫都是新的一棵，測試之間不會互相影響。</summary>
    public static JsonObject ReadTree(string fileName)
    {
        var read = DocumentReader.Read(ReadYaml(fileName), DocumentFormat.Yaml);
        Assert.That(read.Issues, Is.Empty, $"範例檔 {fileName} 應該讀得進來");
        return read.Root!.AsObject();
    }

    public static string ToJson(JsonNode tree) => tree.ToJsonString(ReadableJson);

    /// <summary>寫成區塊樣式的 YAML。字串一律加引號，才不會被讀回成數字、布林或 null。</summary>
    public static string ToYaml(JsonNode tree)
    {
        using var writer = new StringWriter();
        new YamlStream(new YamlDocument(ToYamlNode(tree))).Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static YamlNode ToYamlNode(JsonNode? node) => node switch
    {
        JsonObject obj => new YamlMappingNode(
            obj.Select(property => new KeyValuePair<YamlNode, YamlNode>(Quoted(property.Key), ToYamlNode(property.Value)))),
        JsonArray array => new YamlSequenceNode(array.Select(ToYamlNode)),
        JsonValue value when value.GetValueKind() == JsonValueKind.String => Quoted(value.GetValue<string>()),
        null => new YamlScalarNode("null"),
        _ => new YamlScalarNode(node.ToJsonString()),
    };

    private static YamlScalarNode Quoted(string text) => new(text) { Style = ScalarStyle.DoubleQuoted };
}
