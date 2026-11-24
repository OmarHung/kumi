using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Tests.Integration;

/// <summary>
/// 把範例檔的樹隨機改壞，用兩種格式走完整的流程。要守住的是各階段之間的約定，單一階段的測試看不到：
/// 任何形狀的文件都不會讓流程丟例外（例如通過結構檢查卻綁不進模型）、JSON 與 YAML 的結果一樣、
/// 沒有問題才會回傳模型。種子固定，失敗可以重現。
/// </summary>
[TestFixture]
public sealed class PipelineFuzzTests
{
    private const int Seed = 20261112;
    private const int DocumentsPerExample = 1_000;
    private const int MaxMutationsPerDocument = 3;

    private static readonly BlueprintValidator Validator = BlueprintValidator.CreateDefault();

    [TestCase(ExampleDocuments.BlueprintFile)]
    [TestCase(ExampleDocuments.ChangeSetFile)]
    public void Validate_RandomlyBrokenExamples_NeverThrowAndAgreeAcrossFormats(string fileName)
    {
        // Arrange
        var random = new Random(Seed);
        var problems = new List<string>();
        var invalid = 0;

        // Act
        for (var round = 0; round < DocumentsPerExample; round++)
        {
            var tree = ExampleDocuments.ReadTree(fileName);
            var mutations = random.Next(1, MaxMutationsPerDocument + 1);
            for (var mutation = 0; mutation < mutations; mutation++)
            {
                TreeMutator.Mutate(tree, random);
            }

            var outcome = Check(fileName, tree);
            invalid += outcome.IsValid ? 0 : 1;
            if (outcome.Problem is { } problem && problems.Count < 3)
            {
                problems.Add($"第 {round} 份：{problem}");
            }
        }

        // Assert：沒有任何一份出狀況；而且改壞的文件大多數真的被判定有問題，表示這個測試不是在空轉。
        Assert.That(problems, Is.Empty);
        Assert.That(invalid, Is.GreaterThan(DocumentsPerExample / 2));
    }

    private static (bool IsValid, string? Problem) Check(string fileName, JsonObject tree)
    {
        var json = ExampleDocuments.ToJson(tree);
        try
        {
            var (fromJson, jsonHasModel) = Validate(fileName, json, DocumentFormat.Json);
            var (fromYaml, yamlHasModel) = Validate(fileName, ExampleDocuments.ToYaml(tree), DocumentFormat.Yaml);
            var problem =
                !fromJson.Issues.SequenceEqual(fromYaml.Issues) || fromJson.Truncated != fromYaml.Truncated
                    ? $"JSON 與 YAML 的結果不同（{Summarize(fromJson)} ／ {Summarize(fromYaml)}）"
                : fromJson.IsValid != jsonHasModel || fromYaml.IsValid != yamlHasModel
                    ? "有沒有回傳模型，和有沒有問題對不上"
                : null;
            return (fromJson.IsValid, problem is null ? null : $"{problem}\n{json}");
        }
        catch (Exception exception)
        {
            // 這裡就是要抓「任何例外」：流程對任何文件都不該丟例外。
            return (false, $"{exception.GetType().Name}：{exception.Message}\n{json}");
        }
    }

    private static (ValidationReport Report, bool HasModel) Validate(string fileName, string text, DocumentFormat format)
    {
        if (fileName == ExampleDocuments.BlueprintFile)
        {
            var blueprint = Validator.ValidateBlueprint(text, format);
            return (blueprint.Report, blueprint.Blueprint is not null);
        }

        var changeSet = Validator.ValidateChangeSet(text, format);
        return (changeSet.Report, changeSet.ChangeSet is not null);
    }

    private static string Summarize(ValidationReport report) =>
        string.Join("、", report.Issues.Take(3).Select(issue => $"{issue.Code}@{issue.Path}")) + $"…共 {report.Issues.Count} 個";
}
