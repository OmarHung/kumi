using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.Tests.Items;

/// <summary>
/// 類型與上層一起改（故事 → 任務、從史詩搬到故事底下）：兩種順序的中間狀態都不合法，先改類型會被現在的上層擋下。
/// 這時先把上層清掉（任何類型都能放在最上層）、改類型，新的上層跟著欄位寫；改類型還是失敗就把原本的上層放回去。
/// </summary>
[TestFixture]
public sealed class ItemProposalTypeAndParentTests : ItemProposalTestBase
{
    [SetUp]
    public void ArrangeStructure()
    {
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember() with { Blueprint = Blueprint() };
        H.WorkItems.Items["WEB-20"] = ItemSamples.Item("WEB-20", "史詩") with { Type = "epic" };
        H.WorkItems.Items["WEB-30"] = ItemSamples.Item("WEB-30", "故事") with { Type = "story", ParentKey = "WEB-20" };
        H.WorkItems.Items["WEB-12"] = H.WorkItems.Items["WEB-12"] with { Type = "story", ParentKey = "WEB-20" };
    }

    [Test]
    public async Task Apply_TypeRefusedByTheCurrentParentWhileMovingParent_DetachesChangesTypeThenSetsTheNewParent()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("type", "task"), Change("parentKey", "WEB-30")]);
        var calls = 0;
        H.WorkItems.OnChangeType = (key, _) => ++calls == 1
            ? WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.ItemParentNotAllowed, "「任務」不能掛在「史詩」底下。")
            : H.WorkItems.Items[key];

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        var patches = H.WorkItems.Updates.Select(u => u.Patch).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(H.WorkItems.Steps, Is.EqualTo(new[] { "type", "update", "type", "update" }));
            Assert.That(patches[0].ParentKey.HasValue && patches[0].ParentKey.Value is null, Is.True, "先清掉上層");
            Assert.That(patches[1].ParentKey.Value, Is.EqualTo("WEB-30"));
        }
    }

    [Test]
    public async Task Apply_TypeStillRefusedAfterDetaching_PutsTheOriginalParentBackAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("type", "task"), Change("parentKey", "WEB-30")]);
        H.WorkItems.OnChangeType = (_, _) =>
            WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.ItemParentNotAllowed, "「任務」不能掛在「史詩」底下。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        var patches = H.WorkItems.Updates.Select(u => u.Patch).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemParentNotAllowed));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(H.WorkItems.Steps, Is.EqualTo(new[] { "type", "update", "type", "update" }));
            Assert.That(patches[^1].ParentKey.Value, Is.EqualTo("WEB-20"), "原本的上層放回去");
        }
    }

    [Test]
    public async Task Apply_TypeRefusedWithoutMovingParent_DoesNotTouchTheParent()
    {
        // Arrange：建立時上層放得下（在最上層），套用時工作引擎才擋下（例如中間有人把它搬走了）。
        H.WorkItems.Items["WEB-12"] = H.WorkItems.Items["WEB-12"] with { ParentKey = null };
        var proposal = await CreatedAsync([Change("type", "task")]);
        H.WorkItems.OnChangeType = (_, _) =>
            WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.ItemParentNotAllowed, "「任務」不能掛在「史詩」底下。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemParentNotAllowed));
            Assert.That(H.WorkItems.Steps, Is.EqualTo(new[] { "type" }));
        }
    }

    private static ProjectBlueprint Blueprint() => WorkSamples.Blueprint with
    {
        ItemTypes =
        [
            new ItemType { Key = "epic", Name = "史詩", Workflow = "flow" },
            new ItemType { Key = "story", Name = "故事", Workflow = "flow", Parents = ["epic"] },
            new ItemType { Key = "task", Name = "任務", Workflow = "flow", Parents = ["story"] },
        ],
    };
}
