using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Tests.Items;

/// <summary>修改建議補的欄位：類型、上層、週期、里程碑，與回覆留言（建立時的檢查與存下的內容）。</summary>
[TestFixture]
public sealed class ItemProposalStructureTests : ItemProposalTestBase
{
    private static readonly Guid CommentId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    [SetUp]
    public void ArrangeStructure()
    {
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember() with { Blueprint = StructureBlueprint() };
        H.WorkItems.Items["WEB-20"] = Typed("WEB-20", "epic");
        H.WorkItems.Items["WEB-21"] = Typed("WEB-21", "bug");
        H.WorkItems.Items["WEB-40"] = Typed("WEB-40", "task", parent: "WEB-12");
        H.WorkItems.Items["WEB-41"] = Typed("WEB-41", "task", parent: "WEB-40");
        H.WorkItems.Items["WEB-50"] = Typed("WEB-50", "task");
        H.WorkItems.Items["WEB-51"] = Typed("WEB-51", "task", parent: "WEB-20");
        H.WorkItems.Items["OPS-1"] = ItemSamples.Item("OPS-1", project: "OPS");
    }

    [Test]
    public async Task Create_TypeChangeWithStatusAndCustomFieldOfTheNewType_IsCheckedAgainstTheNewType()
    {
        // Act
        var created = await CreateAsync([Change("type", "bug"), Change("status", "fixed"), Change("fields.category", "ui")], itemKey: "WEB-50");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
            Assert.That(created.Value.Changes.Select(c => c.Field), Is.EqualTo(new[] { "type", "status", "fields.category" }));
        }
    }

    [Test]
    public async Task Create_TypeChangeThenAStatusOnlyTheOldWorkflowHas_IsRejected()
    {
        // Act：bug 的流程是 open／fixed，沒有 done。
        var created = await CreateAsync([Change("type", "bug"), Change("status", "done")], itemKey: "WEB-50");

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[1]"), Does.Contain("沒有狀態「done」").And.Contain("open"));
    }

    [Test]
    public async Task Create_CustomFieldOfTheOldTypeAfterATypeChange_IsRejected()
    {
        // Arrange：task 沒有 category，bug 有；反過來，改成沒有它的 epic 之後就不能再填。
        H.WorkItems.Items["WEB-22"] = Typed("WEB-22", "bug");

        // Act
        var created = await CreateAsync([Change("type", "epic"), Change("fields.category", "ui")], itemKey: "WEB-22");

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[1]"), Does.Contain("沒有自訂欄位「category」"));
    }

    [Test]
    public async Task Create_UnknownType_ListsTheKnownTypes()
    {
        // Act
        var created = await CreateAsync([Change("type", "nope")], itemKey: "WEB-50");

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[0]"), Does.Contain("沒有類型「nope」").And.Contain("epic"));
    }

    [Test]
    public async Task Create_TypeChangeWhoseCurrentParentCannotHoldTheNewType_IsRejectedUnlessTheParentChangesToo()
    {
        // Act：WEB-51 掛在 epic 底下，bug 只能放最上層。
        var blocked = await CreateAsync([Change("type", "bug")], itemKey: "WEB-51");
        var moved = await CreateAsync([Change("type", "bug"), Change("parentKey", null)], itemKey: "WEB-51");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(blocked.Failure, "changes[0]"), Does.Contain("WEB-20").And.Contain("只能放在最上層"));
            Assert.That(moved.Succeeded, Is.True, () => Describe(moved.Failure));
        }
    }

    [Test]
    public async Task Create_TypeChangeWhoseChildrenCannotHangUnderTheNewType_NamesTheChildren()
    {
        // Act：WEB-12 底下有 task（WEB-40），bug 不能有 task 下層。
        var created = await CreateAsync([Change("type", "bug")]);

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[0]"), Does.Contain("WEB-40"));
    }

    [Test]
    public async Task Create_ParentKey_IsStoredAsTheCanonicalKey()
    {
        // Act：模型寫成小寫。
        var created = await CreateAsync([Change("parentKey", "web-20")]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
            Assert.That(created.Value.Changes.Single().Value.GetString(), Is.EqualTo("WEB-20"));
        }
    }

    [Test]
    public async Task Create_ParentKeyNull_MovesToTheTopLevel()
    {
        // Act
        var created = await CreateAsync([Change("parentKey", null)], itemKey: "WEB-51");

        // Assert
        Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
    }

    [TestCase("WEB-12", "不能是自己的上層")]
    [TestCase("WEB-41", "是這個項目的下層")]
    [TestCase("WEB-21", "不能掛在")]
    [TestCase("WEB-99", "找不到上層項目")]
    [TestCase("OPS-1", "找不到上層項目")]
    public async Task Create_BadParent_IsRejectedWithAReason(string parent, string message)
    {
        // Act
        var created = await CreateAsync([Change("parentKey", parent)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(created.Failure, "changes[0]"), Does.Contain(message));
        }
    }

    [Test]
    public async Task Create_ParentNotAString_IsRejected()
    {
        // Act
        var created = await CreateAsync([Change("parentKey", 12)]);

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[0]"), Does.Contain("parentKey 要是上層項目的編號"));
    }

    [Test]
    public async Task Create_CycleAndMilestoneOfThisProject_AreAccepted_AndNullClearsThem()
    {
        // Arrange
        var cycle = H.Planning.AddCycle("第 3 週", new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 13));
        var milestone = H.Planning.AddMilestone("上線", new DateOnly(2026, 12, 31));

        // Act
        var set = await CreateAsync([Change("cycleId", cycle.Id), Change("milestoneId", milestone.Id)]);
        var cleared = await CreateAsync([Change("cycleId", null), Change("milestoneId", null)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(set.Succeeded, Is.True, () => Describe(set.Failure));
            Assert.That(cleared.Succeeded, Is.True, () => Describe(cleared.Failure));
        }
    }

    [Test]
    public async Task Create_CycleOrMilestoneNotOfThisProject_IsRejected()
    {
        // Act
        var created = await CreateAsync([Change("cycleId", Guid.NewGuid()), Change("milestoneId", Guid.NewGuid()), Change("cycleId2", 1)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created.Failure, "changes[0]"), Does.Contain("沒有這個週期").And.Contain("get_planning"));
            Assert.That(IssueAt(created.Failure, "changes[1]"), Does.Contain("沒有這個里程碑"));
            Assert.That(IssueAt(created.Failure, "changes[2]"), Does.Contain("沒有欄位 cycleId2").And.Contain("cycleId、milestoneId"));
        }
    }

    [Test]
    public async Task Create_PlanningNotReadable_SaysSo()
    {
        // Arrange
        H.Planning.Failure = WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");

        // Act
        var created = await CreateAsync([Change("cycleId", Guid.NewGuid())]);

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[0]"), Does.Contain("你讀不到這個專案的週期"));
    }

    [Test]
    public async Task Create_CycleIdNotAGuid_IsRejected()
    {
        // Act
        var created = await CreateAsync([Change("milestoneId", "soon")]);

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[0]"), Does.Contain("要是里程碑的 id"));
    }

    [Test]
    public async Task Create_ReplyToAComment_StoresTheAuthorNameAndATrimmedExcerpt()
    {
        // Arrange
        var author = Guid.NewGuid();
        H.People.Set(H.OrganizationId, author, OrganizationRole.Member, name: "小華");
        H.Activity.Activities = [Comment(CommentId, author, new string('字', AiRules.MaxCommentExcerptLength + 50))];

        // Act
        var created = await CreateReplyAsync("收到", CommentId);
        var read = await Proposals.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
            Assert.That(read.Value.ReplyTo?.CommentId, Is.EqualTo(CommentId));
            Assert.That(read.Value.ReplyTo?.AuthorName, Is.EqualTo("小華"));
            Assert.That(read.Value.ReplyTo?.Excerpt, Has.Length.EqualTo(AiRules.MaxCommentExcerptLength));
            Assert.That(H.Activity.Queries.Select(q => q.Kind), Is.All.EqualTo(ActivityKind.Commented), "只翻留言");
        }
    }

    [Test]
    public async Task Create_ReplyToWithoutAComment_IsRejected()
    {
        // Arrange
        H.Activity.Activities = [Comment(CommentId, ItemSamples.MemberId, "原文")];

        // Act
        var created = await CreateReplyAsync(null, CommentId, [Change("title", "新標題")]);

        // Assert
        Assert.That(IssueAt(created.Failure, "replyTo"), Does.Contain("要一起給 comment"));
    }

    [Test]
    public async Task Create_ReplyToACommentThatIsNotThere_IsRejected()
    {
        // Act
        var created = await CreateReplyAsync("收到", Guid.NewGuid());

        // Assert
        Assert.That(IssueAt(created.Failure, "replyTo"), Does.Contain("找不到這則留言"));
    }

    [Test]
    public async Task Create_ReplyToADeletedComment_IsRejected()
    {
        // Arrange
        H.Activity.Activities = [Comment(CommentId, ItemSamples.MemberId, null) with { DeletedAt = DateTimeOffset.UnixEpoch }];

        // Act
        var created = await CreateReplyAsync("收到", CommentId);

        // Assert
        Assert.That(IssueAt(created.Failure, "replyTo"), Does.Contain("已經刪除"));
    }

    [Test]
    public async Task Create_ReplyToAnActivityThatIsNotAComment_IsRejected()
    {
        // Arrange
        H.Activity.Activities = [new ItemActivity(CommentId, "WEB-12", ActivityKind.Updated, ItemSamples.MemberId, DateTimeOffset.UnixEpoch)];

        // Act
        var created = await CreateReplyAsync("收到", CommentId);

        // Assert
        Assert.That(IssueAt(created.Failure, "replyTo"), Does.Contain("找不到這則留言"));
    }

    [Test]
    public async Task Create_WithoutNewFields_DoesNotAskPlanningOrActivity()
    {
        // Act
        await CreateAsync([Change("title", "新標題")], "留言");

        // Assert
        Assert.That(H.Activity.Queries, Is.Empty);
    }

    private Task<Result<ItemProposal>> CreateReplyAsync(string? comment, Guid replyTo, IReadOnlyList<ItemFieldChange>? changes = null) =>
        Creator.CreateAsync(
            Me,
            new ItemProposalDraft("WEB", "WEB-12", changes ?? [], comment) { ReplyTo = replyTo },
            CancellationToken.None);

    private static string Describe(Failure? failure) =>
        failure is null ? string.Empty : failure.Message + "；" + string.Join("；", failure.Details.Select(d => d.Path + " " + d.Message));

    private static string IssueAt(Failure? failure, string path) => failure!.Details.Single(d => d.Path == path).Message;

    private static ItemActivity Comment(Guid id, Guid author, string? body) =>
        new(id, "WEB-12", ActivityKind.Commented, author, DateTimeOffset.UnixEpoch) { Comment = body };

    private static WorkItem Typed(string key, string type, string? parent = null) =>
        ItemSamples.Item(key, key) with { Type = type, ParentKey = parent, State = type == "bug" ? "open" : "todo" };

    /// <summary>task 可以掛在 epic 或 task 底下；epic 與 bug 只能放最上層；bug 有自己的流程與自訂欄位 category。</summary>
    private static ProjectBlueprint StructureBlueprint() => WorkSamples.Blueprint with
    {
        Fields = [new FieldDefinition { Key = "category", Name = "分類", Type = FieldType.Text }],
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
            new ItemType { Key = "task", Name = "任務", Workflow = "flow", Parents = ["epic", "task"] },
            new ItemType { Key = "epic", Name = "大項", Workflow = "flow" },
            new ItemType { Key = "bug", Name = "缺陷", Workflow = "bugflow", Fields = ["category"] },
        ],
    };
}
