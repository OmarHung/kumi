using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRevisions;

/// <summary>批次修改的每一筆也有類型、上層、週期、里程碑與回覆留言：規則和單一項目的一樣，細項路徑多一層 items[n]。</summary>
[TestFixture]
public sealed class ItemBatchStructureTests : ItemRevisionTestBase
{
    private static readonly Guid CommentId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private IItemBatchProposalCreator Creator => H.Host.Get<IItemBatchProposalCreator>();

    private IItemBatchProposalService Proposals => H.Host.Get<IItemBatchProposalService>();

    [SetUp]
    public void ArrangeStructure()
    {
        H.Projects.OnGet = (_, _) => Detail() with { Blueprint = StructureBlueprint() };
        H.WorkItems.Items["WEB-20"] = ItemSamples.Item("WEB-20", "大項") with { Type = "epic" };
        H.Activity.StepLog = H.WorkItems.Steps;
    }

    [Test]
    public async Task Create_NewFieldsOnSeveralEntries_AreCheckedPerEntryWithTheNewTypeAndReportedWithTheEntryPath()
    {
        // Act：第二筆改成 bug 之後再填 done（bug 的流程沒有），第三筆的上層不存在。
        var created = await CreateAsync(
            new ItemBatchEntryDraft("WEB-12", [Change("parentKey", "WEB-20")], null, null),
            new ItemBatchEntryDraft("WEB-13", [Change("type", "bug"), Change("status", "done")], null, null),
            new ItemBatchEntryDraft("WEB-14", [Change("parentKey", "WEB-99"), Change("cycleId", Guid.NewGuid())], null, null));

        // Assert
        var paths = created.Failure!.Details.Select(d => d.Path).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.False);
            Assert.That(paths, Is.EqualTo(new[] { "items[1].changes[1]", "items[2].changes[0]", "items[2].changes[1]" }));
            Assert.That(IssueAt(created.Failure, "items[2].changes[0]"), Does.Contain("找不到上層項目"));
            Assert.That(IssueAt(created.Failure, "items[2].changes[1]"), Does.Contain("沒有這個週期"));
        }
    }

    [Test]
    public async Task Create_ReplyToOnOneEntry_IsStoredOnThatEntryOnly()
    {
        // Arrange
        var author = Guid.NewGuid();
        H.People.Set(H.OrganizationId, author, OrganizationRole.Member, name: "小華");
        H.Activity.Activities = [new ItemActivity(CommentId, "WEB-12", ActivityKind.Commented, author, DateTimeOffset.UnixEpoch) { Comment = "原文" }];

        // Act
        var created = await CreateAsync(
            new ItemBatchEntryDraft("WEB-12", [], "收到", null) { ReplyTo = CommentId },
            new ItemBatchEntryDraft("WEB-13", [], "好", null));
        var read = await Proposals.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
            Assert.That(read.Value.Items[0].ReplyTo, Is.EqualTo(new CommentReplyTarget(CommentId, "小華", "原文")));
            Assert.That(read.Value.Items[1].ReplyTo, Is.Null);
        }
    }

    [Test]
    public async Task Create_ReplyToRules_AreTheSameAsForASingleItem()
    {
        // Act：沒有 comment、留言不是這個項目的。
        var noComment = await CreateAsync(new ItemBatchEntryDraft("WEB-12", [Change("title", "x")], null, null) { ReplyTo = CommentId });
        var missing = await CreateAsync(new ItemBatchEntryDraft("WEB-12", [], "收到", null) { ReplyTo = CommentId });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(noComment.Failure, "items[0].replyTo"), Does.Contain("要一起給 comment"));
            Assert.That(IssueAt(missing.Failure, "items[0].replyTo"), Does.Contain("找不到這則留言"));
        }
    }

    [Test]
    public async Task Apply_EachEntry_RunsTypeThenFieldsThenStatusThenCommentWithItsOwnReplyTo()
    {
        // Arrange
        H.Activity.Activities = [new ItemActivity(CommentId, "WEB-12", ActivityKind.Commented, ItemSamples.MemberId, DateTimeOffset.UnixEpoch) { Comment = "原文" }];
        var created = await CreateAsync(
            new ItemBatchEntryDraft("WEB-12", [Change("status", "fixed"), Change("title", "新"), Change("type", "bug")], "收到", null) { ReplyTo = CommentId },
            new ItemBatchEntryDraft("WEB-13", [Change("parentKey", "WEB-20")], "好", null));

        // Act
        var applied = await Proposals.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => Describe(applied.Failure));
            Assert.That(H.WorkItems.Steps, Is.EqualTo(new[] { "type", "update", "transition", "comment", "update", "comment" }));
            Assert.That(H.WorkItems.TypeChanges.Single(), Is.EqualTo((Me, "WEB-12", "bug")));
            Assert.That(H.Activity.ReplyTos, Is.EqualTo(new Guid?[] { CommentId, null }));
            Assert.That(H.WorkItems.Updates[1].Patch.ParentKey.Value, Is.EqualTo("WEB-20"));
        }
    }

    [Test]
    public async Task Apply_TypeChangeRefusedOnTheFirstStepOfEveryEntry_StaysPending()
    {
        // Arrange
        var created = await CreateAsync(
            new ItemBatchEntryDraft("WEB-12", [Change("type", "bug"), Change("title", "新")], null, null),
            new ItemBatchEntryDraft("WEB-13", [Change("type", "bug")], null, null));
        H.WorkItems.OnChangeType = (_, _) => WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.ItemParentNotAllowed, "放不下。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, created.Value.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemParentNotAllowed));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(H.WorkItems.Updates, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_EntryWithTheSameTypeAsNow_SkipsTheTypeStepAndItsFailureLeavesItPending()
    {
        // Arrange
        var created = await CreateAsync(new ItemBatchEntryDraft("WEB-12", [Change("type", "task"), Change("title", "新")], null, null));
        H.WorkItems.OnUpdate = (_, _) => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, created.Value.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(H.WorkItems.TypeChanges, Is.Empty);
        }
    }

    private Task<Result<ItemBatchProposal>> CreateAsync(params ItemBatchEntryDraft[] entries) =>
        Creator.CreateAsync(Me, new ItemBatchProposalDraft("WEB", entries) { ConversationId = Guid.CreateVersion7() }, CancellationToken.None);

    private static string Describe(Failure? failure) =>
        failure is null ? string.Empty : failure.Message + "；" + string.Join("；", failure.Details.Select(d => d.Path + " " + d.Message));

    private static ProjectBlueprint StructureBlueprint() => WorkSamples.Blueprint with
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
