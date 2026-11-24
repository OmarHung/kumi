using System.Text.Json;
using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Tests.Integration;

/// <summary>兩個範例檔走完整的流程：讀檔、結構、綁定、語意。</summary>
[TestFixture]
public sealed class ExampleFilesTests
{
    private static readonly BlueprintValidator Validator = BlueprintValidator.CreateDefault();

    [Test]
    public void ValidateBlueprint_AgencyProjectExample_PassesAndReturnsTheModel()
    {
        // Arrange
        var yaml = ExampleDocuments.ReadYaml(ExampleDocuments.BlueprintFile);

        // Act
        var result = Validator.ValidateBlueprint(yaml, DocumentFormat.Yaml);

        // Assert
        Assert.That(result.Report.Issues, Is.Empty);
        Assert.That(result.Blueprint, Is.Not.Null);
        var blueprint = result.Blueprint!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Report.IsValid, Is.True);
            Assert.That(result.Report.Truncated, Is.False);
            Assert.That(blueprint.Metadata.Key, Is.EqualTo("agency_project"));
            Assert.That(blueprint.Metadata.BasedOn!.Version, Is.EqualTo("1.0.0"));
            Assert.That(blueprint.Fields.Select(f => f.Type), Is.EqualTo(new[]
            {
                FieldType.Currency, FieldType.Date, FieldType.LongText, FieldType.SingleSelect,
            }));
            Assert.That(blueprint.Fields[0].Currency, Is.EqualTo("TWD"));
            Assert.That(blueprint.Fields[0].Options, Is.Null);
            Assert.That(blueprint.Fields[3].Options!.Select(o => o.Color), Is.EqualTo(new OptionColor?[]
            {
                OptionColor.Gray, OptionColor.Yellow, OptionColor.Red,
            }));
            Assert.That(blueprint.Workflows[0].Transitions, Has.Count.EqualTo(8));
            Assert.That(blueprint.Workflows[0].Transitions![7].RequireFields, Is.EqualTo(new[] { "client_signoff_date" }));
            Assert.That(blueprint.Workflows[1].Transitions, Is.Null, "沒有列 transitions 要讀成 null，不是空清單");
            Assert.That(blueprint.ItemTypes[2].Parents, Is.EqualTo(new[] { "requirement", "change_request" }));
            Assert.That(blueprint.Views[0].VisibleTo, Is.Null);
            Assert.That(blueprint.Views[2].VisibleTo, Is.EqualTo(new[] { "client", "project_manager" }));
            Assert.That(blueprint.Automations[0].Trigger.Source, Is.EqualTo(TriggerSource.Gitlab));
            Assert.That(blueprint.Automations[0].Enabled, Is.True);
            Assert.That(blueprint.Roles.Select(r => r.Key), Is.EqualTo(new[] { "project_manager", "client" }));
            Assert.That(blueprint.Bindings[0].Connector, Is.EqualTo(ConnectorType.Gitlab));
        }
    }

    [Test]
    public void ValidateChangeSet_SplitAcceptanceExample_PassesAndReturnsTheModel()
    {
        // Arrange
        var yaml = ExampleDocuments.ReadYaml(ExampleDocuments.ChangeSetFile);

        // Act
        var result = Validator.ValidateChangeSet(yaml, DocumentFormat.Yaml);

        // Assert
        Assert.That(result.Report.Issues, Is.Empty);
        Assert.That(result.ChangeSet, Is.Not.Null);
        var changeSet = result.ChangeSet!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Report.IsValid, Is.True);
            Assert.That(changeSet.BaseVersion, Is.EqualTo(12L));
            Assert.That(changeSet.Summary, Is.Not.Empty);
            Assert.That(changeSet.Blueprint.Metadata.Key, Is.EqualTo("website_redesign"));
            Assert.That(changeSet.Blueprint.Fields, Is.Empty);
            Assert.That(changeSet.Blueprint.Workflows[0].States.Select(s => s.Key), Is.EqualTo(new[]
            {
                "todo", "doing", "internal_qa", "client_qa", "done",
            }));
            Assert.That(changeSet.Migrations!.MoveStates, Has.Count.EqualTo(1));
            Assert.That(changeSet.Migrations.MoveStates[0].Workflow, Is.EqualTo("main"));
            Assert.That(changeSet.Migrations.MoveStates[0].From, Is.EqualTo("qa"));
            Assert.That(changeSet.Migrations.MoveStates[0].To, Is.EqualTo("internal_qa"));
            Assert.That(changeSet.Migrations.DropFields, Is.Empty);
        }
    }

    [Test]
    public void ValidateBlueprint_SameContentAsJsonAndYaml_GivesTheSameResult()
    {
        // Arrange：範例檔的原文、同一棵樹寫成的 JSON、同一棵樹重新寫成的 YAML。
        var original = ExampleDocuments.ReadYaml(ExampleDocuments.BlueprintFile);
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.BlueprintFile);
        var json = ExampleDocuments.ToJson(tree);
        var yaml = ExampleDocuments.ToYaml(tree);

        // Act
        var fromOriginal = Validator.ValidateBlueprint(original, DocumentFormat.Yaml);
        var fromJson = Validator.ValidateBlueprint(json, DocumentFormat.Json);
        var fromYaml = Validator.ValidateBlueprint(yaml, DocumentFormat.Yaml);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fromOriginal.Report.Issues, Is.Empty);
            Assert.That(fromJson.Report.Issues, Is.Empty);
            Assert.That(fromYaml.Report.Issues, Is.Empty);
            Assert.That(Serialize(fromJson.Blueprint), Is.EqualTo(Serialize(fromOriginal.Blueprint)));
            Assert.That(Serialize(fromYaml.Blueprint), Is.EqualTo(Serialize(fromOriginal.Blueprint)));
            Assert.That(Serialize(fromOriginal.Blueprint), Does.Contain("agency_project"));
        }
    }

    [Test]
    public void ValidateChangeSet_SameContentAsJsonAndYaml_GivesTheSameResult()
    {
        // Arrange
        var original = ExampleDocuments.ReadYaml(ExampleDocuments.ChangeSetFile);
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.ChangeSetFile);
        var json = ExampleDocuments.ToJson(tree);
        var yaml = ExampleDocuments.ToYaml(tree);

        // Act
        var fromOriginal = Validator.ValidateChangeSet(original, DocumentFormat.Yaml);
        var fromJson = Validator.ValidateChangeSet(json, DocumentFormat.Json);
        var fromYaml = Validator.ValidateChangeSet(yaml, DocumentFormat.Yaml);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fromOriginal.Report.Issues, Is.Empty);
            Assert.That(fromJson.Report.Issues, Is.Empty);
            Assert.That(fromYaml.Report.Issues, Is.Empty);
            Assert.That(Serialize(fromJson.ChangeSet), Is.EqualTo(Serialize(fromOriginal.ChangeSet)));
            Assert.That(Serialize(fromYaml.ChangeSet), Is.EqualTo(Serialize(fromOriginal.ChangeSet)));
            Assert.That(Serialize(fromOriginal.ChangeSet), Does.Contain("website_redesign"));
        }
    }

    [Test]
    public void ValidateBlueprint_JsonTextReadAsYaml_GivesTheSameResult()
    {
        // Arrange：JSON 幾乎是 YAML 的子集，使用者把 JSON 內容標成 YAML 送來時結果也要一樣。
        var json = ExampleDocuments.ToJson(ExampleDocuments.ReadTree(ExampleDocuments.BlueprintFile));

        // Act
        var asJson = Validator.ValidateBlueprint(json, DocumentFormat.Json);
        var asYaml = Validator.ValidateBlueprint(json, DocumentFormat.Yaml);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(asJson.Report.Issues, Is.Empty);
            Assert.That(asYaml.Report.Issues, Is.Empty);
            Assert.That(Serialize(asYaml.Blueprint), Is.EqualTo(Serialize(asJson.Blueprint)));
        }
    }

    // 模型裡的清單沒有值相等，所以用序列化後的文字比較整個模型。
    private static string Serialize<T>(T? model) => JsonSerializer.Serialize(model);
}
