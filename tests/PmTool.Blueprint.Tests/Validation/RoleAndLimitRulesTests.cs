using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class RoleAndLimitRulesTests
{
    [TestCase("admin")]
    [TestCase("editor")]
    [TestCase("contributor")]
    [TestCase("commenter")]
    [TestCase("viewer")]
    public void Validate_CustomRoleNamedAfterBuiltInRole_ReportsRoleBuiltInConflict(string key)
    {
        // Arrange
        var blueprint = Agency() with { Roles = [.. Agency().Roles, Role(key)] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.RoleBuiltInConflict, "/roles/2/key") }));
            Assert.That(issues[0].Message, Does.Contain($"「{key}」"));
        }
    }

    [Test]
    public void Validate_FieldsOverTheLimit_ReportsLimitExceededAtTheFieldList()
    {
        // Arrange
        var extra = Enumerable.Range(0, BlueprintVocabulary.MaxFields - Agency().Fields.Count + 1)
            .Select(i => TextField($"extra_{i}"));
        var blueprint = Agency() with { Fields = [.. Agency().Fields, .. extra] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.LimitExceeded, "/fields") }));
            Assert.That(issues[0].Message, Does.Contain(BlueprintVocabulary.MaxFields.ToString()));
        }
    }

    [Test]
    public void Validate_FieldsExactlyAtTheLimit_ReportsNoIssues()
    {
        // Arrange
        var extra = Enumerable.Range(0, BlueprintVocabulary.MaxFields - Agency().Fields.Count)
            .Select(i => TextField($"extra_{i}"));
        var blueprint = Agency() with { Fields = [.. Agency().Fields, .. extra] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_StatesOverTheLimit_ReportsLimitExceededAtThatWorkflowsStates()
    {
        // Arrange：task_flow 沒有列轉換，多加的狀態不會觸發可達性規則。
        var extra = Enumerable.Range(0, BlueprintVocabulary.MaxStatesPerWorkflow - 2)
            .Select(i => State($"extra_{i}", StateCategory.InProgress));
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with { States = [.. w.States, .. extra] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.LimitExceeded, "/workflows/1/states") }));
            Assert.That(issues[0].Message, Does.Contain("「task_flow」"));
        }
    }

    [Test]
    public void Validate_StatesExactlyAtTheLimit_ReportsNoIssues()
    {
        // Arrange
        var extra = Enumerable.Range(0, BlueprintVocabulary.MaxStatesPerWorkflow - 3)
            .Select(i => State($"extra_{i}", StateCategory.InProgress));
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with { States = [.. w.States, .. extra] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_ItemTypesOverTheLimit_ReportsLimitExceededAtTheItemTypeList()
    {
        // Arrange
        var extra = Enumerable.Range(0, BlueprintVocabulary.MaxItemTypes - Agency().ItemTypes.Count + 1)
            .Select(i => ItemTypeOf($"extra_{i}", "task_flow"));
        var blueprint = Agency() with { ItemTypes = [.. Agency().ItemTypes, .. extra] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.LimitExceeded, "/itemTypes") }));
    }

    [Test]
    public void Validate_AutomationsOverTheLimit_ReportsLimitExceededAtTheAutomationList()
    {
        // Arrange
        var template = Agency().Automations[0];
        var automations = Enumerable.Range(0, BlueprintVocabulary.MaxAutomations + 1)
            .Select(i => template with { Key = $"rule_{i}" });
        var blueprint = Agency() with { Automations = [.. automations] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.LimitExceeded, "/automations") }));
    }

    [Test]
    public void Validate_LimitAndOtherProblems_ReportsLimitsFirst()
    {
        // Arrange：上限放在最前面，問題很多被截斷時也不會被藏起來。
        var extra = Enumerable.Range(0, BlueprintVocabulary.MaxFields).Select(i => TextField($"extra_{i}"));
        var blueprint = Agency() with { Fields = [TextField("Bad"), .. extra] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths().Take(2), Is.EqualTo(new[]
        {
            (IssueCodes.LimitExceeded, "/fields"),
            (IssueCodes.KeyFormat, "/fields/0/key"),
        }));
    }
}
