using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Planning;

/// <summary>
/// 有些修改不會丟資料，卻會讓既有的資料不再符合新的設定：類型不能再掛在某個類型底下、角色被拿掉。
/// 變更集沒有地方交代這些，有沒有受影響的資料也要查了才知道，所以計畫把它們列出來，由套用那一步檢查。
/// </summary>
[TestFixture]
public sealed class PlanHierarchyAndRoleTests
{
    [Test]
    public void Plan_ItemTypeLosesAnAllowedParent_IsListedSoExistingItemsCanBeChecked()
    {
        // Arrange：task 原本可以掛在 requirement 或 change_request 底下，現在只剩 requirement。
        var next = Agency().WithItemType(TaskType, type => type with { Parents = ["requirement"] });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new ParentRemovalImpact("task", "change_request") }));
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_ItemTypeLosesAllItsParents_ListsEachOne()
    {
        // Arrange
        var next = Agency().WithItemType(TaskType, type => type with { Parents = [] });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[]
        {
            new ParentRemovalImpact("task", "requirement"),
            new ParentRemovalImpact("task", "change_request"),
        }));
    }

    [Test]
    public void Plan_ItemTypeGainsAParentOrListsThemInAnotherOrder_HasNoImpact()
    {
        // Arrange
        var next = Agency()
            .WithItemType(TaskType, type => type with { Parents = ["change_request", "requirement"] })
            .WithItemType(ChangeRequest, type => type with { Parents = ["requirement"] });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        Assert.That(plan.DataImpacts, Is.Empty);
    }

    [Test]
    public void Plan_ParentItemTypeIsRemovedAltogether_IsCoveredByThatRemoval()
    {
        // Arrange：change_request 整個被拿掉，task 的上層清單也跟著拿掉它。
        // 底下沒有 change_request 的項目，就不會有掛在它底下的 task，不必再列一次。
        var next = (Agency() with { ItemTypes = Agency().ItemTypes.WithoutAt(ChangeRequest) })
            .WithItemType(TaskType - 1, type => type with { Parents = ["requirement"] });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new ItemTypeRemovalImpact("change_request") }));
    }

    [Test]
    public void Plan_RemovedRole_IsListedSoExistingMembersCanBeChecked()
    {
        // Arrange
        var next = Agency() with { Roles = [Agency().Roles[0], Role("qa_lead")] };

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new RoleRemovalImpact("client") }));
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_RemovedItemTypeNarrowedParentsAndRemovedRole_AreListedInThatOrder()
    {
        // Arrange
        var next = (Agency() with
        {
            ItemTypes = Agency().ItemTypes.WithoutAt(Requirement),
            Roles = [Agency().Roles[0]],
        }).WithItemType(TaskType - 1, type => type with { Parents = [] });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert：requirement 整個被拿掉，由類型的移除涵蓋；change_request 還在，所以要另外列。
        Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[]
        {
            new ItemTypeRemovalImpact("requirement"),
            new ParentRemovalImpact("task", "change_request"),
            new RoleRemovalImpact("client"),
        }));
    }

    [Test]
    public void Plan_RenamingAFieldAStateAndAnItemType_ChangesNoData()
    {
        // Arrange：內部認的是 key，顯示名稱怎麼改都不影響既有的資料。
        var next = Agency()
            .WithField(QuoteAmount, field => field with { Name = "報價" })
            .WithItemType(TaskType, type => type with { Name = "工作" })
            .WithWorkflow(TaskFlow, workflow => workflow with
            {
                Name = "任務",
                States = [workflow.States[0] with { Name = "還沒做" }, .. workflow.States.Skip(1)],
            });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Changes.Describe(), Is.EqualTo(new[]
            {
                "Modified Field quote_amount [name]",
                "Modified Workflow task_flow [name]",
                "Modified State task_flow/todo [name]",
                "Modified ItemType task [name]",
            }));
            Assert.That(plan.DataImpacts, Is.Empty);
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }
}
