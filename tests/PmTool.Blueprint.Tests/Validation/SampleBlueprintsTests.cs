using PmTool.Blueprint.Validation;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class SampleBlueprintsTests
{
    [Test]
    public void Validate_AgencySampleBlueprint_ReportsNoIssues()
    {
        // Arrange
        var blueprint = SampleBlueprints.Agency();

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ValidateChangeSet_SplitAcceptanceSample_ReportsNoIssues()
    {
        // Arrange
        var changeSet = SampleBlueprints.SplitAcceptance();

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_SplitAcceptanceBlueprintOnItsOwn_ReportsNoIssues()
    {
        // Arrange
        var blueprint = SampleBlueprints.SplitAcceptance().Blueprint;

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_SameBrokenBlueprintTwice_ReturnsTheSameIssuesInTheSameOrder()
    {
        // Arrange
        var blueprint = SampleBlueprints.Agency() with { Roles = [], Bindings = [] };

        // Act
        var first = SemanticValidator.Validate(blueprint);
        var second = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.EqualTo(first));
        }
    }
}
