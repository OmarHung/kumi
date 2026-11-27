using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.PlanningProposals;

/// <summary>建立週期與里程碑建議：權限、筆數、新增／修改／刪除的欄位規則、一次回齊所有問題。</summary>
[TestFixture]
public sealed class PlanningProposalCreateTests : ProjectWriteTestBase
{
    private static readonly DateOnly Jan1 = new(2027, 1, 1);

    private static PlanningProposalDraft Draft(params PlanningChangeDraft[] changes) => new("WEB", changes);

    private static PlanningChangeDraft NewCycle(string name, DateOnly start, DateOnly end) =>
        new(PlanningTarget.Cycle, PlanningAction.Create, null) { Name = name, StartDate = start, EndDate = end };

    private static PlanningChangeDraft NewMilestone(string name, DateOnly due, string? description = null) =>
        new(PlanningTarget.Milestone, PlanningAction.Create, null) { Name = name, DueDate = due, Description = description };

    [Test]
    public async Task Create_MixedChanges_StoresBeforeAfterAndTheItemCountOfADeletedCycle()
    {
        // Arrange
        var cycle = Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));
        var milestone = Planning.AddMilestone("上線", Jan1.AddDays(30), "舊說明");
        var doomed = Planning.AddCycle("作廢的", Jan1.AddDays(60), Jan1.AddDays(73), items: 7);
        var draft = Draft(
            NewCycle("  第二期 ", Jan1.AddDays(14), Jan1.AddDays(27)),
            new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, cycle.Id) { Name = "衝刺一" },
            new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Update, milestone.Id) { DueDate = Jan1.AddDays(40) },
            new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Delete, doomed.Id)) with
        {
            Reason = " 重新排期 ",
            ConversationId = Guid.NewGuid(),
        };

        // Act
        var created = await PlanningCreator.CreateAsync(Me, draft, CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
        var changes = created.Value.Changes;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(created.Value.Reason, Is.EqualTo("重新排期"));
            Assert.That(changes[0].After, Is.EqualTo(new PlanningValues("第二期", Jan1.AddDays(14), Jan1.AddDays(27), null, null)));
            Assert.That(changes[0].Before, Is.Null);
            Assert.That(changes[1].Before, Is.EqualTo(new PlanningValues("第一期", Jan1, Jan1.AddDays(13), null, null)));
            Assert.That(changes[1].After!.Name, Is.EqualTo("衝刺一"));
            Assert.That(changes[1].After!.StartDate, Is.EqualTo(Jan1), "沒給的欄位照現在的值補齊");
            Assert.That(changes[2].After, Is.EqualTo(new PlanningValues("上線", null, null, Jan1.AddDays(40), "舊說明")));
            Assert.That(changes[3].ItemCount, Is.EqualTo(7));
            Assert.That(changes[3].After, Is.Null);
            Assert.That(Planning.Writes, Is.Empty, "只建立建議，不寫週期與里程碑");
        }
    }

    [Test]
    public async Task Create_OnlyTheCreatorCanReadItBack()
    {
        // Arrange
        var created = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("第一期", Jan1, Jan1.AddDays(13))), CancellationToken.None);
        var other = new Actor(H.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

        // Act
        var mine = await PlanningService.GetAsync(Me, created.Value.Id, CancellationToken.None);
        var theirs = await PlanningService.GetAsync(other, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mine.Value.Changes, Has.Count.EqualTo(1));
            Assert.That(theirs.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalNotFound));
        }
    }

    [Test]
    public async Task Create_WithoutItemUpdate_IsForbiddenAndNamesWhoCanDoIt()
    {
        // Arrange
        UseProject(canManage: false, canUpdate: false);

        // Act
        var created = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("第一期", Jan1, Jan1.AddDays(13))), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(created.Failure?.Message, Does.Contain("週期與里程碑").And.Contain("甲").And.Contain("組織管理員"));
        }
    }

    [Test]
    public async Task Create_WhenOnlyOwnItemsAreVisible_IsForbidden()
    {
        // Arrange
        UseProject(canManage: false, readsAll: false);

        // Act
        var created = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("第一期", Jan1, Jan1.AddDays(13))), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
    }

    [Test]
    public async Task Create_InAnArchivedProject_SaysTheProjectIsArchived()
    {
        // Arrange
        UseProject(archived: true);

        // Act
        var created = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("第一期", Jan1, Jan1.AddDays(13))), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
            Assert.That(created.Failure?.Message, Does.Contain("封存"));
        }
    }

    [Test]
    public async Task Create_ProjectNotVisible_ReturnsTheProjectNotFoundAsIs()
    {
        // Arrange
        Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var created = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("第一期", Jan1, Jan1.AddDays(13))), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task Create_ZeroOrTooManyChanges_IsRejected()
    {
        // Arrange
        var tooMany = Enumerable.Range(0, AiRules.MaxPlanningChangesPerProposal + 1)
            .Select(i => NewMilestone($"里程碑 {i}", Jan1.AddDays(i))).ToArray();

        // Act
        var none = await PlanningCreator.CreateAsync(Me, Draft(), CancellationToken.None);
        var many = await PlanningCreator.CreateAsync(Me, Draft(tooMany), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(none.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(none, "changes"), Does.Contain("1 到 10"));
            Assert.That(IssueAt(many, "changes"), Does.Contain("1 到 10"));
        }
    }

    [Test]
    public async Task Create_ReportsEveryProblemAtOnceWithThePathOfEachChange()
    {
        // Arrange
        var cycle = Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));
        var draft = Draft(
            new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Create, null) { Name = "缺日期" },
            new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Create, Guid.NewGuid()) { Name = "有 id", DueDate = Jan1 },
            new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, cycle.Id) { DueDate = Jan1 },
            new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Create, null) { Name = "搞混", StartDate = Jan1, DueDate = Jan1 });

        // Act
        var created = await PlanningCreator.CreateAsync(Me, draft, CancellationToken.None);

        // Assert
        var paths = created.Failure!.Details.Select(d => d.Path).ToList();
        Assert.That(paths, Is.SupersetOf(new[]
        {
            "changes[0].startDate", "changes[0].endDate", "changes[1].id", "changes[2].dueDate", "changes[3].startDate",
        }));
    }

    [Test]
    public async Task Create_UpdateOrDeleteOfSomethingNotInThisProject_IsRejected()
    {
        // Arrange
        var draft = Draft(
            new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, Guid.NewGuid()) { Name = "x" },
            new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Delete, Guid.NewGuid()),
            new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Delete, null));

        // Act
        var created = await PlanningCreator.CreateAsync(Me, draft, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created, "changes[0].id"), Does.Contain("沒有這個週期"));
            Assert.That(IssueAt(created, "changes[1].id"), Does.Contain("沒有這個里程碑"));
            Assert.That(IssueAt(created, "changes[2].id"), Does.Contain("要給 id"));
        }
    }

    [Test]
    public async Task Create_ACycleIdGivenForAMilestone_IsNotFoundBecauseTheKindsAreSeparate()
    {
        // Arrange
        var cycle = Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));

        // Act
        var created = await PlanningCreator.CreateAsync(
            Me, Draft(new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Delete, cycle.Id)), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, "changes[0].id"), Does.Contain("沒有這個里程碑"));
    }

    [Test]
    public async Task Create_UpdateThatChangesNothing_IsRejected()
    {
        // Arrange
        var cycle = Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));
        var milestone = Planning.AddMilestone("上線", Jan1, "說明");

        // Act
        var same = await PlanningCreator.CreateAsync(
            Me,
            Draft(
                new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, cycle.Id) { Name = " 第一期 ", StartDate = Jan1 },
                new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Update, milestone.Id) { Description = "說明" }),
            CancellationToken.None);
        var nothing = await PlanningCreator.CreateAsync(
            Me, Draft(new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, cycle.Id)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(same, "changes[0]"), Does.Contain("沒有任何欄位和現在不同"));
            Assert.That(IssueAt(same, "changes[1]"), Does.Contain("沒有任何欄位和現在不同"));
            Assert.That(IssueAt(nothing, "changes[0]"), Does.Contain("沒有任何欄位和現在不同"));
        }
    }

    [Test]
    public async Task Create_TheSameTargetTwice_IsRejected()
    {
        // Arrange
        var cycle = Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));

        // Act
        var created = await PlanningCreator.CreateAsync(
            Me,
            Draft(
                new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, cycle.Id) { Name = "甲" },
                new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Delete, cycle.Id)),
            CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, "changes[1].id"), Does.Contain("只能出現一次"));
    }

    [Test]
    public async Task Create_NameRules_BlankTooLongAndControlCharacters()
    {
        // Arrange
        var draft = Draft(
            NewCycle("   ", Jan1, Jan1),
            NewCycle(new string('甲', PlanningRules.MaxCycleNameLength + 1), Jan1, Jan1),
            NewMilestone(new string('乙', PlanningRules.MaxMilestoneNameLength + 1), Jan1),
            NewMilestone("換\n行", Jan1),
            NewMilestone("零寬​字", Jan1),
            NewCycle(new string('甲', PlanningRules.MaxCycleNameLength), Jan1, Jan1));

        // Act
        var created = await PlanningCreator.CreateAsync(Me, draft, CancellationToken.None);

        // Assert
        var paths = created.Failure!.Details.Select(d => d.Path).ToList();
        Assert.That(paths, Is.EqualTo(new[]
        {
            "changes[0].name", "changes[1].name", "changes[2].name", "changes[3].name", "changes[4].name",
        }));
    }

    [Test]
    public async Task Create_CycleDates_EndBeforeStartAndMoreThanAYear_AreRejected()
    {
        // Act
        var reversed = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("倒過來", Jan1.AddDays(5), Jan1)), CancellationToken.None);
        var tooLong = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("太長", Jan1, Jan1.AddDays(PlanningRules.MaxCycleDays))), CancellationToken.None);
        var exact = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("剛好", Jan1, Jan1.AddDays(PlanningRules.MaxCycleDays - 1))), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(reversed, "changes[0].endDate"), Does.Contain("不能早於開始日"));
            Assert.That(IssueAt(tooLong, "changes[0].endDate"), Does.Contain("最長 365 天"));
            Assert.That(exact.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_UpdatingOnlyOneEndOfACycle_IsCheckedAgainstTheOtherCurrentEnd()
    {
        // Arrange
        var cycle = Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));

        // Act
        var created = await PlanningCreator.CreateAsync(
            Me, Draft(new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, cycle.Id) { StartDate = Jan1.AddDays(20) }), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, "changes[0].endDate"), Does.Contain("不能早於開始日"));
    }

    [Test]
    public async Task Create_MilestoneDescription_TooLongOrControlCharacters_AreRejectedAndEmptyClearsIt()
    {
        // Arrange
        var milestone = Planning.AddMilestone("上線", Jan1, "舊說明");

        // Act
        var tooLong = await PlanningCreator.CreateAsync(
            Me, Draft(NewMilestone("甲", Jan1, new string('字', PlanningRules.MaxDescriptionLength + 1))), CancellationToken.None);
        var control = await PlanningCreator.CreateAsync(Me, Draft(NewMilestone("乙", Jan1, "壞\u0001字")), CancellationToken.None);
        var cleared = await PlanningCreator.CreateAsync(
            Me, Draft(new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Update, milestone.Id) { Description = "" }), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(tooLong, "changes[0].description"), Does.Contain("最多 2000"));
            Assert.That(IssueAt(control, "changes[0].description"), Does.Contain("控制字元"));
            Assert.That(cleared.Succeeded, Is.True, () => Describe(cleared.Failure));
            Assert.That(cleared.Value.Changes[0].After!.Description, Is.Null);
            Assert.That(cleared.Value.Changes[0].Before!.Description, Is.EqualTo("舊說明"));
        }
    }

    [Test]
    public async Task Create_DeleteWithOtherFields_IsRejected()
    {
        // Arrange
        var cycle = Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));

        // Act
        var created = await PlanningCreator.CreateAsync(
            Me, Draft(new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Delete, cycle.Id) { Name = "順便改名" }), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, "changes[0]"), Does.Contain("刪除只要給"));
    }

    [Test]
    public async Task Create_ReasonTooLongOrWithNewline_IsRejected()
    {
        // Act
        var tooLong = await PlanningCreator.CreateAsync(
            Me, Draft(NewCycle("甲", Jan1, Jan1)) with { Reason = new string('因', 201) }, CancellationToken.None);
        var newline = await PlanningCreator.CreateAsync(
            Me, Draft(NewCycle("乙", Jan1, Jan1)) with { Reason = "第一行\n第二行" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(tooLong, "reason"), Does.Contain("200"));
            Assert.That(IssueAt(newline, "reason"), Does.Contain("控制字元"));
        }
    }

    [Test]
    public async Task Create_MoreThanTheProjectLimit_IsRejected()
    {
        // Arrange
        for (var i = 0; i < PlanningRules.MaxPerProject; i++)
        {
            Planning.AddCycle($"週期 {i}", Jan1, Jan1);
        }

        // Act
        var created = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("多一個", Jan1, Jan1)), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, "changes"), Does.Contain("最多 200 個週期"));
    }

    [Test]
    public async Task Create_CycleNameAlreadyUsed_IgnoringCase_IsRejectedAtTheName()
    {
        // Arrange
        Planning.AddCycle("Sprint 1", Jan1, Jan1.AddDays(13));

        // Act
        var created = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("sprint 1", Jan1.AddDays(20), Jan1.AddDays(30))), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, "changes[0].name"), Does.Contain("已經有"));
    }

    [Test]
    public async Task Create_CycleOverlappingAnotherCycle_IsRejectedAtTheStartDate()
    {
        // Arrange
        Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));

        // Act
        var created = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("第二期", Jan1.AddDays(13), Jan1.AddDays(20))), CancellationToken.None);
        var adjacent = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("第三期", Jan1.AddDays(14), Jan1.AddDays(20))), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created, "changes[0].startDate"), Does.Contain("重疊"));
            Assert.That(adjacent.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_TwoNewCyclesInTheSameProposal_AreCheckedAgainstEachOther()
    {
        // Act
        var sameName = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("甲", Jan1, Jan1.AddDays(6)), NewCycle("甲", Jan1.AddDays(10), Jan1.AddDays(16))), CancellationToken.None);
        var overlap = await PlanningCreator.CreateAsync(Me, Draft(NewCycle("乙", Jan1, Jan1.AddDays(6)), NewCycle("丙", Jan1.AddDays(3), Jan1.AddDays(9))), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(sameName, "changes[1].name"), Does.Contain("已經有"));
            Assert.That(IssueAt(overlap, "changes[1].startDate"), Does.Contain("重疊"));
        }
    }

    [Test]
    public async Task Create_DeletingOrMovingACycleFirst_FreesItsNameAndDates()
    {
        // Arrange
        var old = Planning.AddCycle("第一期", Jan1, Jan1.AddDays(13));
        var other = Planning.AddCycle("第二期", Jan1.AddDays(14), Jan1.AddDays(27));

        // Act
        var afterDelete = await PlanningCreator.CreateAsync(
            Me,
            Draft(new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Delete, old.Id), NewCycle("第一期", Jan1, Jan1.AddDays(13))),
            CancellationToken.None);
        var movedThenTaken = await PlanningCreator.CreateAsync(
            Me,
            Draft(
                new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, other.Id) { StartDate = Jan1.AddDays(30), EndDate = Jan1.AddDays(40) },
                NewCycle("第三期", Jan1.AddDays(14), Jan1.AddDays(27))),
            CancellationToken.None);
        var updateOverlaps = await PlanningCreator.CreateAsync(
            Me, Draft(new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, other.Id) { StartDate = Jan1.AddDays(10) }), CancellationToken.None);
        var renameTaken = await PlanningCreator.CreateAsync(
            Me, Draft(new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, other.Id) { Name = "第一期" }), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterDelete.Succeeded, Is.True, () => Describe(afterDelete.Failure));
            Assert.That(movedThenTaken.Succeeded, Is.True, () => Describe(movedThenTaken.Failure));
            Assert.That(IssueAt(updateOverlaps, "changes[0].startDate"), Does.Contain("重疊"));
            Assert.That(IssueAt(renameTaken, "changes[0].name"), Does.Contain("已經有"));
        }
    }

    [Test]
    public async Task Create_MilestoneNameAlreadyUsed_IgnoringCase_IsRejectedButDatesMayOverlap()
    {
        // Arrange
        Planning.AddMilestone("Launch", Jan1);

        // Act
        var taken = await PlanningCreator.CreateAsync(Me, Draft(NewMilestone("LAUNCH", Jan1.AddDays(5))), CancellationToken.None);
        var sameDay = await PlanningCreator.CreateAsync(Me, Draft(NewMilestone("Beta", Jan1)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(taken, "changes[0].name"), Does.Contain("已經有"));
            Assert.That(sameDay.Succeeded, Is.True);
        }
    }
}
