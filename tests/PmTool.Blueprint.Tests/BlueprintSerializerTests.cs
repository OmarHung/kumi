using System.Text.Json;
using PmTool.Blueprint.Tests.Validation;

namespace PmTool.Blueprint.Tests;

/// <summary>模型寫回文件的形式。存進資料庫、回給用戶端的藍圖都走這裡，所以寫出來的東西必須能再通過驗證。</summary>
[TestFixture]
public sealed class BlueprintSerializerTests
{
    private static readonly BlueprintValidator Validator = BlueprintValidator.CreateDefault();

    [Test]
    public void ToJson_ExampleBlueprint_PassesValidationAndReadsBackAsTheSameModel()
    {
        // Arrange
        var blueprint = SampleBlueprints.Agency();

        // Act
        var json = BlueprintSerializer.ToJson(blueprint);
        var readBack = Validator.ValidateBlueprint(json, DocumentFormat.Json);

        // Assert
        Assert.That(readBack.Report.Issues, Is.Empty);
        Assert.That(JsonSerializer.Serialize(readBack.Blueprint), Is.EqualTo(JsonSerializer.Serialize(blueprint)));
    }

    [Test]
    public void ToJson_ExampleChangeSet_PassesValidationAndReadsBackAsTheSameModel()
    {
        // Arrange
        var changeSet = SampleBlueprints.SplitAcceptance();

        // Act
        var json = BlueprintSerializer.ToJson(changeSet);
        var readBack = Validator.ValidateChangeSet(json, DocumentFormat.Json);

        // Assert
        Assert.That(readBack.Report.Issues, Is.Empty);
        Assert.That(JsonSerializer.Serialize(readBack.ChangeSet), Is.EqualTo(JsonSerializer.Serialize(changeSet)));
    }

    [Test]
    public void ToJson_Blueprint_UsesTheNamesWrittenInDocuments()
    {
        // Act
        var json = BlueprintSerializer.ToJson(SampleBlueprints.Agency());

        // Assert：屬性是 camelCase、列舉是 snake_case；沒寫的東西不出現，算出來的屬性（isSelect 之類）也不出現。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"itemTypes\"").And.Contain("\"single_select\"").And.Contain("\"not_started\""));
            Assert.That(json, Does.Contain("接案專案"), "中文照原樣寫出來，不轉成跳脫序列");
            Assert.That(json, Does.Not.Contain("null").And.Not.Contain("isSelect").And.Not.Contain("isTerminal"));
        }
    }

    [Test]
    public void ToElement_Blueprint_IsTheSameDocumentAsToJson()
    {
        // Arrange
        var blueprint = SampleBlueprints.Agency();

        // Act
        var element = BlueprintSerializer.ToElement(blueprint);

        // Assert
        Assert.That(element.GetProperty("metadata").GetProperty("key").GetString(), Is.EqualTo("agency_project"));
        Assert.That(element.GetRawText(), Is.EqualTo(BlueprintSerializer.ToJson(blueprint)));
    }
}
