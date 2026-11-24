using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Planning;

/// <summary>變更集是照某一版藍圖寫的。那一版已經不是目前的版本時，算出來的差異不可信，要重新產生。</summary>
[TestFixture]
public sealed class PlanBaseVersionTests
{
    [Test]
    public void Plan_BaseVersionIsNotTheCurrentVersion_IsBlockedAndSaysBothVersions()
    {
        // Arrange
        var changeSet = ChangeSetFor(Agency(), baseVersion: 11);

        // Act
        var plan = Planner.Plan(Agency(), currentVersion: 12, changeSet);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanBaseVersionMismatch, "/baseVersion"),
        }));
        Assert.That(plan.Report.Issues[0].Message, Does.Contain("11").And.Contain("12"));
    }

    [Test]
    public void Plan_NewProjectWithBaseVersionZero_IsNotBlocked()
    {
        // Arrange：新專案還沒有藍圖，版本是 0。
        var changeSet = ChangeSetFor(Agency(), baseVersion: 0);

        // Act
        var plan = Planner.Plan(current: null, currentVersion: 0, changeSet);

        // Assert
        Assert.That(plan.Report.Issues, Is.Empty);
    }

    [Test]
    public void Plan_NewProjectWithAnotherBaseVersion_IsBlocked()
    {
        // Arrange
        var changeSet = ChangeSetFor(Agency(), baseVersion: 3);

        // Act
        var plan = Planner.Plan(current: null, currentVersion: 0, changeSet);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanBaseVersionMismatch, "/baseVersion"),
        }));
    }

    [Test]
    public void Plan_NoCurrentBlueprintButAVersionNumber_IsACallerError()
    {
        // Act：有版本就一定有藍圖。這是呼叫端傳錯，不是變更集的問題。
        TestDelegate plan = () => Planner.Plan(current: null, currentVersion: 5, ChangeSetFor(Agency(), baseVersion: 5));

        // Assert
        Assert.That(plan, Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void Plan_CurrentBlueprintWithoutAPositiveVersion_IsACallerError(long version)
    {
        // Act：有藍圖就表示套用過，版本從 1 起算。
        TestDelegate plan = () => Planner.Plan(Agency(), version, ChangeSetFor(Agency(), baseVersion: version));

        // Assert
        Assert.That(plan, Throws.InstanceOf<ArgumentException>());
    }
}
