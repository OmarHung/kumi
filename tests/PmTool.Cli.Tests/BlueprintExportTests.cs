using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint;
using PmTool.Cli.Documents;

namespace PmTool.Cli.Tests;

/// <summary>匯出的檔案要讀得回來：伺服器用的驗證器認得、內容和藍圖一樣。</summary>
[TestFixture]
public sealed class BlueprintExportTests
{
    private static readonly string[] Examples = ["agency-project.blueprint.yaml", "website-redesign.blueprint.yaml"];

    private static JsonElement BlueprintOf(string example)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", example));
        var node = new DocumentText(text, DocumentFormat.Yaml, example).ToJson();
        return JsonSerializer.SerializeToElement(node);
    }

    [TestCaseSource(nameof(Examples))]
    public void Render_Yaml_IsAValidChangeSetWithTheSameBlueprintAndAProjectComment(string example)
    {
        // Arrange
        var blueprint = BlueprintOf(example);

        // Act
        var yaml = BlueprintExport.Render("WEB", 4, blueprint, DocumentFormat.Yaml);
        var report = BlueprintValidator.CreateDefault().ValidateChangeSet(yaml, DocumentFormat.Yaml);
        var reread = new DocumentText(yaml, DocumentFormat.Yaml, "匯出").ToJson()!.AsObject();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(yaml, Does.StartWith("# project: WEB\n"));
            Assert.That(report.Report.Issues, Is.Empty, string.Join("；", report.Report.Issues.Select(i => $"{i.Path} {i.Message}")));
            Assert.That((long)reread["baseVersion"]!, Is.EqualTo(4));
            Assert.That(JsonNode.DeepEquals(reread["blueprint"], JsonNode.Parse(blueprint.GetRawText())), Is.True);
        }
    }

    [TestCaseSource(nameof(Examples))]
    public void Render_Json_IsAValidChangeSet(string example)
    {
        // Act
        var json = BlueprintExport.Render("WEB", 4, BlueprintOf(example), DocumentFormat.Json);
        var report = BlueprintValidator.CreateDefault().ValidateChangeSet(json, DocumentFormat.Json);

        // Assert
        Assert.That(report.Report.Issues, Is.Empty);
        Assert.That(json, Does.Not.Contain("# project").And.Contain("\"baseVersion\": 4"));
    }

    [Test]
    public void Render_Yaml_StringsThatLookLikeOtherTypesStayStrings()
    {
        // Arrange
        var tricky = new[] { "true", "null", "123", "1.5", "~", "", "a: b", "# not a comment", "  padded  ", "多行\n文字", "yes", "0x1F", "- dash", "\"quoted\"" };
        var blueprint = JsonSerializer.SerializeToElement(new { values = tricky, nested = new { flag = true, count = 3, ratio = 0.5, none = (string?)null } });

        // Act
        var yaml = BlueprintExport.Render("WEB", 1, blueprint, DocumentFormat.Yaml);
        var reread = new DocumentText(yaml, DocumentFormat.Yaml, "匯出").ToJson()!["blueprint"]!;

        // Assert
        Assert.That(reread["values"]!.AsArray().Select(v => (string?)v), Is.EqualTo(tricky));
        Assert.That((bool?)reread["nested"]!["flag"], Is.True);
        Assert.That((long?)reread["nested"]!["count"], Is.EqualTo(3));
        Assert.That((double?)reread["nested"]!["ratio"], Is.EqualTo(0.5));
        Assert.That(reread["nested"]!["none"], Is.Null);
    }

    [Test]
    public void ProjectKeyFromComment_ReadsTheHeaderOfYamlOnly()
    {
        // Arrange
        var yaml = new DocumentText("# 說明\n# project: AGY\nbaseVersion: 1\n", DocumentFormat.Yaml, "x");
        var json = new DocumentText("{ \"baseVersion\": 1 }", DocumentFormat.Json, "x");
        var none = new DocumentText("baseVersion: 1\n", DocumentFormat.Yaml, "x");

        // Assert
        Assert.That(yaml.ProjectKeyFromComment(), Is.EqualTo("AGY"));
        Assert.That(json.ProjectKeyFromComment(), Is.Null);
        Assert.That(none.ProjectKeyFromComment(), Is.Null);
    }

    [TestCase("baseVersion: [1")]
    [TestCase("a: 1\na: 2")]
    [TestCase("a: *x")]
    [TestCase("---\na: 1\n---\nb: 2")]
    public void TopLevelKeys_UnreadableYaml_IsNullSoValidationReportsTheRealProblem(string text) =>
        Assert.That(new DocumentText(text, DocumentFormat.Yaml, "x").TopLevelKeys(), Is.Null);

    [Test]
    public void ToJson_TooDeeplyNestedYaml_IsRefusedInsteadOfOverflowingTheStack()
    {
        // Arrange
        var text = string.Concat(Enumerable.Repeat("[", 200)) + string.Concat(Enumerable.Repeat("]", 200));

        // Act and Assert
        Assert.That(new DocumentText(text, DocumentFormat.Yaml, "x").ToJson(), Is.Null);
    }

    [Test]
    public void ToJson_PlainScalars_FollowTheYamlCoreSchemaTheServerUses()
    {
        // Arrange
        var yaml = "a: true\nb: ~\nc: 12\nd: 1.5\ne: yes\nf: \"12\"\ng: 2026-12-01\nh: .inf\n";

        // Act
        var node = new DocumentText(yaml, DocumentFormat.Yaml, "x").ToJson()!;

        // Assert
        Assert.That(node.ToJsonString(), Is.EqualTo("{\"a\":true,\"b\":null,\"c\":12,\"d\":1.5,\"e\":\"yes\",\"f\":\"12\",\"g\":\"2026-12-01\",\"h\":\".inf\"}"));
    }
}
