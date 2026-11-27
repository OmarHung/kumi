using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.Tests.Items;

/// <summary>套用補的欄位：順序是改類型 → 欄位 → 狀態 → 留言，第一個真的要做的步驟失敗時什麼都沒改，回覆帶 replyTo。</summary>
[TestFixture]
public sealed class ItemProposalStructureApplyTests : ItemProposalTestBase
{
    private static readonly Guid CommentId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    [SetUp]
    public void ArrangeStructure()
    {
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember() with { Blueprint = Blueprint() };
        H.WorkItems.Items["WEB-20"] = ItemSamples.Item("WEB-20", "大項") with { Type = "epic" };
        H.WorkItems.Items["WEB-50"] = ItemSamples.Item("WEB-50", "任務");
        H.Activity.StepLog = H.WorkItems.Steps;
    }

    [Test]
    public async Task Apply_TypeFieldsStatusAndComment_RunInThatOrderAsTheApplyingPerson()
    {
        // Arrange
        var cycle = H.Planning.AddCycle("第 3 週", new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 13));
        var milestone = H.Planning.AddMilestone("上線", new DateOnly(2026, 12, 31));
        var proposal = await CreatedAsync(
            [Change("status", "fixed"), Change("title", "新標題"), Change("type", "bug"), Change("parentKey", null),
             Change("cycleId", cycle.Id), Change("milestoneId", milestone.Id)],
            "改成缺陷");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        var patch = H.WorkItems.Updates.Single().Patch;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(H.WorkItems.Steps, Is.EqualTo(new[] { "type", "update", "transition", "comment" }));
            Assert.That(H.WorkItems.TypeChanges.Single(), Is.EqualTo((Me, "WEB-12", "bug")));
            Assert.That(patch.Title.Value, Is.EqualTo("新標題"));
            Assert.That(patch.ParentKey.HasValue, Is.True);
            Assert.That(patch.ParentKey.Value, Is.Null);
            Assert.That(patch.CycleId.Value, Is.EqualTo(cycle.Id));
            Assert.That(patch.MilestoneId.Value, Is.EqualTo(milestone.Id));
            Assert.That(H.WorkItems.Transitions.Single().State, Is.EqualTo("fixed"));
        }
    }

    [Test]
    public async Task Apply_OnlyAType_DoesNotCallUpdateOrTransition()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("type", "bug")]);

        // Act
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(H.WorkItems.Steps, Is.EqualTo(new[] { "type" }));
    }

    [Test]
    public async Task Apply_ParentKey_GoesIntoThePatchAsTheCanonicalKey()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("parentKey", "web-20")]);

        // Act
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(H.WorkItems.Updates.Single().Patch.ParentKey.Value, Is.EqualTo("WEB-20"));
    }

    [Test]
    public async Task Apply_TypeChangeIsRefused_ChangesNothingElseAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("type", "bug"), Change("title", "新標題"), Change("status", "fixed")], "留言");
        H.WorkItems.OnChangeType = (_, _) => WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.ItemParentNotAllowed, "上層放不下這個類型。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemParentNotAllowed));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(H.WorkItems.Steps, Is.EqualTo(new[] { "type" }));
            Assert.That(H.Activity.Comments, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_FieldsFailAfterTheTypeChanged_IsAppliedWithAnErrorAndStopsTheRest()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("type", "bug"), Change("title", "新標題"), Change("status", "fixed")], "留言");
        H.WorkItems.OnUpdate = (_, _) => WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.ItemParentCycle, "上層不合規定。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert：類型已經改了，不能再當成什麼都沒發生。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("欄位").And.Contain("上層不合規定"));
            Assert.That(H.WorkItems.Steps, Is.EqualTo(new[] { "type", "update", "transition", "comment" }));
        }
    }

    [Test]
    public async Task Apply_TypeSameAsTheCurrentOne_IsSkippedAndDoesNotCountAsAStepDone()
    {
        // Arrange：WEB-12 現在就是 task；後面的欄位失敗時，什麼都沒改，不能算「部分套用」。
        var proposal = await CreatedAsync([Change("type", "task"), Change("title", "新標題")]);
        H.WorkItems.OnUpdate = (_, _) => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(H.WorkItems.TypeChanges, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_ReplyTo_IsPassedToAddComment()
    {
        // Arrange
        H.Activity.Activities = [new ItemActivity(CommentId, "WEB-12", ActivityKind.Commented, ItemSamples.MemberId, DateTimeOffset.UnixEpoch) { Comment = "原文" }];
        var created = await Creator.CreateAsync(
            Me, new ItemProposalDraft("WEB", "WEB-12", [], "收到") { ReplyTo = CommentId }, CancellationToken.None);

        // Act
        var applied = await Proposals.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(H.Activity.ReplyTos.Single(), Is.EqualTo(CommentId));
            Assert.That(applied.Value.ReplyTo?.CommentId, Is.EqualTo(CommentId));
        }
    }

    [Test]
    public async Task Apply_CommentWithoutReplyTo_PassesNull()
    {
        // Arrange
        var proposal = await CreatedAsync([], "新的一則");

        // Act
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(H.Activity.ReplyTos.Single(), Is.Null);
    }

    private static ProjectBlueprint Blueprint() => WorkSamples.Blueprint with
    {
        Workflows =
        [
            .. WorkSamples.Blueprint.Workflows,
            new Workflow
            {
                Key = "bugflow",
                Name = "缺陷流程",
                States =
                [
                    new WorkflowState { Key = "open", Name = "開啟", Category = StateCategory.NotStarted },
                    new WorkflowState { Key = "fixed", Name = "已修復", Category = StateCategory.Done },
                ],
            },
        ],
        ItemTypes =
        [
            new ItemType { Key = "task", Name = "任務", Workflow = "flow", Parents = ["epic"] },
            new ItemType { Key = "epic", Name = "大項", Workflow = "flow" },
            new ItemType { Key = "bug", Name = "缺陷", Workflow = "bugflow" },
        ],
    };
}
