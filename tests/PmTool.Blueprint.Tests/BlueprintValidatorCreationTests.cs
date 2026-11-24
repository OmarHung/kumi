using System.Text.Json;
using PmTool.Blueprint.Schema;

namespace PmTool.Blueprint.Tests;

/// <summary>
/// Schema 是用萬用字元內嵌進組件的，路徑對不到時建置不會失敗，只會少內嵌。
/// 這裡確認少了 Schema 時，驗證器在建立的當下就失敗，而不是等到第一份文件進來。
/// </summary>
[TestFixture]
public sealed class BlueprintValidatorCreationTests
{
    [Test]
    public void CreateDefault_EmbeddedSchemas_CreatesAValidator()
    {
        // Act
        var validator = BlueprintValidator.CreateDefault();

        // Assert
        Assert.That(validator, Is.Not.Null);
    }

    [Test]
    public void Create_NoSchemaLoaded_FailsAndNamesTheMissingSchema()
    {
        // Arrange
        var empty = SchemaRegistry.Load([]);

        // Act
        TestDelegate create = () => BlueprintValidator.Create(empty);

        // Assert
        Assert.That(create, Throws.InvalidOperationException.With.Message.Contains(SchemaIds.ProjectBlueprint));
    }

    [Test]
    public void Create_ChangeSetSchemaMissing_FailsAndNamesTheMissingSchema()
    {
        // Arrange：只載入藍圖那一份。
        var blueprintOnly = SchemaRegistry.Load(
            EmbeddedSchemas.ReadAll().Where(text => IdOf(text) == SchemaIds.ProjectBlueprint));

        // Act
        TestDelegate create = () => BlueprintValidator.Create(blueprintOnly);

        // Assert
        Assert.That(create, Throws.InvalidOperationException.With.Message.Contains(SchemaIds.ChangeSet));
    }

    private static string? IdOf(string schemaText)
    {
        using var document = JsonDocument.Parse(schemaText);
        return document.RootElement.GetProperty("$id").GetString();
    }
}
