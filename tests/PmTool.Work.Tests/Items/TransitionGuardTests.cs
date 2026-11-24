using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 轉換要求先填核心欄位。內建模板沒有這樣的轉換，開專案又只能從模板開，
/// 所以這條規則直接對檢查本身測，藍圖照樣經過驗證器。
/// 批次不能有上層，所以用自己的流程：要求上層項目的流程只能給可以有上層的類型用。
/// </summary>
[TestFixture]
public sealed class TransitionGuardTests
{
    private const string Blueprint = """
        apiVersion: blueprint/v0
        kind: ProjectBlueprint
        metadata: { key: core_required, name: 核心欄位 }
        fields: []
        workflows:
          - key: flow
            name: 出貨流程
            states:
              - { key: open, name: 開啟, category: not_started }
              - { key: shipped, name: 出貨, category: done }
              - { key: filed, name: 歸檔, category: done }
            transitions:
              - { from: open, to: shipped, requireFields: [title, state, item_type, assignee, priority, start_date, due_date, estimate] }
              - { from: open, to: filed, requireFields: [description, parent] }
          - key: batch_flow
            name: 批次流程
            states:
              - { key: open, name: 開啟, category: not_started }
              - { key: closed, name: 結束, category: done }
        itemTypes:
          - { key: batch, name: 批次, workflow: batch_flow }
          - { key: order, name: 訂單, workflow: flow, parents: [batch] }
        """;

    private static readonly BlueprintValidationResult Validated = BlueprintValidator.CreateDefault().ValidateBlueprint(Blueprint, DocumentFormat.Yaml);

    private static ProjectSchema Schema => ProjectSchema.From(Validated.Blueprint!);

    [Test]
    public void Blueprint_UsedByTheseTests_PassesValidation()
    {
        // Assert：要求上層項目的轉換只能給可以有上層的類型用，否則測的是驗證器會擋掉的藍圖。
        Assert.That(Validated.Report.Issues.Select(i => (i.Code, i.Path)), Is.Empty);
    }

    [Test]
    public void Check_CoreFieldsWithoutValues_ListsEachMissingField()
    {
        // Arrange
        var item = Order();

        // Act
        var result = TransitionGuard.Check(Schema, Schema.ItemTypes["order"], item, "admin", "shipped");

        // Assert：標題、狀態、類型一定有值；優先順序「無」算沒填。
        Assert.That(result.Failure!.Code, Is.EqualTo(WorkErrorCodes.TransitionFieldsRequired));
        Assert.That(
            result.Failure.Details.Select(d => d.Path),
            Is.EqualTo(new[] { "assignee", "priority", "start_date", "due_date", "estimate" }));
        Assert.That(result.Failure.Message, Does.Contain("「負責人」").And.Contain("「到期日」").And.Contain("「出貨」"));
    }

    [Test]
    public void Check_CoreFieldsWithValues_AllowsTheTransition()
    {
        // Arrange
        var item = Order() with
        {
            AssigneeId = Guid.NewGuid(),
            Priority = (short)Priority.Low,
            StartDate = new DateOnly(2026, 11, 1),
            DueDate = new DateOnly(2026, 11, 2),
            Estimate = 0m,
        };

        // Act
        var result = TransitionGuard.Check(Schema, Schema.ItemTypes["order"], item, null, "shipped");

        // Assert
        Assert.That(result.Value.Key, Is.EqualTo("shipped"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  \n ")]
    public void Check_DescriptionAndParentWithoutValues_ListsBothWithTheirNames(string? description)
    {
        // Arrange：空白的說明不會存進來，但檢查本身也不把它當成有值。
        var item = Order() with { Description = description };

        // Act
        var result = TransitionGuard.Check(Schema, Schema.ItemTypes["order"], item, "admin", "filed");

        // Assert
        Assert.That(result.Failure!.Code, Is.EqualTo(WorkErrorCodes.TransitionFieldsRequired));
        Assert.That(result.Failure.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[]
        {
            (WorkErrorCodes.TransitionFieldsRequired, "description"),
            (WorkErrorCodes.TransitionFieldsRequired, "parent"),
        }));
        Assert.That(result.Failure.Message, Is.EqualTo("要先填「說明」、「上層項目」才能進入「歸檔」。"));
    }

    [Test]
    public void Check_DescriptionAndParentWithValues_AllowsTheTransition()
    {
        // Arrange
        var item = Order() with { Description = "範例說明", ParentId = Guid.NewGuid() };

        // Act
        var result = TransitionGuard.Check(Schema, Schema.ItemTypes["order"], item, "admin", "filed");

        // Assert
        Assert.That(result.Value.Key, Is.EqualTo("filed"));
    }

    [Test]
    public void Check_OnlyTheParentMissing_ListsOnlyTheParent()
    {
        // Arrange
        var item = Order() with { Description = "範例說明" };

        // Act
        var result = TransitionGuard.Check(Schema, Schema.ItemTypes["order"], item, "admin", "filed");

        // Assert
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "parent" }));
    }

    private static ItemEntity Order() => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        Number = 1,
        Type = "order",
        Title = "範例訂單",
        State = "open",
        StateCategory = "not_started",
        Priority = 0,
        Fields = "{}",
        CreatedBy = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
