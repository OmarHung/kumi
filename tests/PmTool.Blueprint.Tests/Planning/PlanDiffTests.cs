using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Planning;

/// <summary>計畫的第一部分：新藍圖相對於目前的藍圖，哪些資源新增、移除、修改了。</summary>
[TestFixture]
public sealed class PlanDiffTests
{
    [Test]
    public void Plan_ChangeSetIdenticalToTheCurrentBlueprint_HasNothingToDo()
    {
        // Arrange：兩份各自組出來的藍圖，內容相同但不是同一個物件。
        var current = Agency();
        var next = Agency();

        // Act
        var plan = PlanFrom(current, next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Changes, Is.Empty);
            Assert.That(plan.DataImpacts, Is.Empty);
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_FieldAdded_ListsItWithItsPositionInTheNewBlueprint()
    {
        // Arrange
        var next = Agency() with { Fields = [.. Agency().Fields, TextField("notes")] };

        // Act
        var change = PlanFrom(Agency(), next).Changes.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.Added));
            Assert.That(change.Resource, Is.EqualTo(ResourceKind.Field));
            Assert.That(change.Key, Is.EqualTo("notes"));
            Assert.That(change.ParentKey, Is.Null);
            Assert.That(change.CurrentPath, Is.Null);
            Assert.That(change.NewPath, Is.EqualTo("/fields/4"));
            Assert.That(change.ChangedProperties, Is.Empty);
        }
    }

    [Test]
    public void Plan_FieldRemoved_ListsItWithItsPositionInTheCurrentBlueprint()
    {
        // Arrange
        var next = Agency() with { Fields = Agency().Fields.WithoutAt(ChangeReason) };

        // Act
        var change = PlanFrom(Agency(), next).Changes.Single(c => c.Resource == ResourceKind.Field);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.Removed));
            Assert.That(change.Key, Is.EqualTo("change_reason"));
            Assert.That(change.CurrentPath, Is.EqualTo("/fields/2"));
            Assert.That(change.NewPath, Is.Null);
        }
    }

    [Test]
    public void Plan_FieldRenamed_IsAModificationOfTheSameField()
    {
        // Arrange：顯示名稱改了，key 沒變，所以還是同一個欄位。
        var next = Agency().WithField(ClientSignoffDate, field => field with { Name = "驗收日期", Description = "客戶簽名的日期" });

        // Act
        var change = PlanFrom(Agency(), next).Changes.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.Modified));
            Assert.That(change.Resource, Is.EqualTo(ResourceKind.Field));
            Assert.That(change.Key, Is.EqualTo("client_signoff_date"));
            Assert.That(change.CurrentPath, Is.EqualTo("/fields/1"));
            Assert.That(change.NewPath, Is.EqualTo("/fields/1"));
            Assert.That(change.ChangedProperties, Is.EqualTo(new[] { "name", "description" }));
        }
    }

    [Test]
    public void Plan_FieldMovedByAnEarlierInsertion_KeepsBothPositions()
    {
        // Arrange：前面插入一個新欄位，原本的欄位位置往後移，內容沒變。
        var next = Agency() with { Fields = [TextField("notes"), .. Agency().Fields] };
        next = next.WithField(Impact + 1, field => field with { Name = "衝擊" });

        // Act
        var changes = PlanFrom(Agency(), next).Changes;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changes.Describe(), Is.EqualTo(new[] { "Added Field notes", "Modified Field impact [name]" }));
            Assert.That(changes[1].CurrentPath, Is.EqualTo("/fields/3"));
            Assert.That(changes[1].NewPath, Is.EqualTo("/fields/4"));
        }
    }

    [Test]
    public void Plan_NewProject_ListsEveryTopLevelResourceAsAddedAndNothingBelowThem()
    {
        // Act：新專案沒有目前的藍圖。
        var plan = Planner.Plan(current: null, currentVersion: 0, ChangeSetFor(Agency(), baseVersion: 0));

        // Assert
        Assert.That(plan.Changes.Describe(), Is.EqualTo(new[]
        {
            "Added Metadata agency_project",
            "Added Field quote_amount", "Added Field client_signoff_date", "Added Field change_reason", "Added Field impact",
            "Added Workflow delivery", "Added Workflow task_flow",
            "Added ItemType requirement", "Added ItemType change_request", "Added ItemType task",
            "Added View progress_board", "Added View requirement_table", "Added View client_view",
            "Added Automation mr_merged_to_internal_qa",
            "Added Role project_manager", "Added Role client",
            "Added Binding gitlab:example-group/web-app",
        }));
        Assert.That(plan.DataImpacts, Is.Empty);
    }
}
