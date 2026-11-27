using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.PlanningProposals;

/// <summary>套用、作廢與讀取：照順序以套用的人的身分寫入、每一筆自己的結果、建立中與中斷、別人看不到。</summary>
[TestFixture]
public sealed class PlanningProposalApplyTests : ProjectWriteTestBase
{
    private static readonly DateOnly Jan1 = new(2027, 1, 1);

    private async Task<PlanningProposal> CreatedAsync(params PlanningChangeDraft[] changes)
    {
        var created = await PlanningCreator.CreateAsync(Me, new PlanningProposalDraft("WEB", changes), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
        return created.Value;
    }

    private static int _nextCycle;

    // 每個新週期的日期都不同：同一個專案的週期日期不能重疊，建立建議時就會被擋。
    private static PlanningChangeDraft NewCycle(string name)
    {
        var start = new DateOnly(2030, 1, 1).AddDays(20 * Interlocked.Increment(ref _nextCycle));
        return new(PlanningTarget.Cycle, PlanningAction.Create, null) { Name = name, StartDate = start, EndDate = start.AddDays(13) };
    }

    private static PlanningChangeDraft NewMilestone(string name) =>
        new(PlanningTarget.Milestone, PlanningAction.Create, null) { Name = name, DueDate = Jan1, Description = "說明" };

    [Test]
    public async Task Apply_AllSucceed_WritesInOrderAsTheApplyingPersonAndRecordsTheNewIds()
    {
        // Arrange
        var cycle = Planning.AddCycle("舊週期", Jan1, Jan1.AddDays(13));
        var milestone = Planning.AddMilestone("舊里程碑", Jan1);
        var proposal = await CreatedAsync(
            NewCycle("新週期"),
            new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, cycle.Id) { Name = "改名" },
            NewMilestone("新里程碑"),
            new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Delete, milestone.Id));

        // Act
        var applied = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(applied.Succeeded, Is.True, () => Describe(applied.Failure));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(Planning.Writes, Is.EqualTo(new[]
            {
                "create-cycle:新週期", $"update-cycle:{cycle.Id}:改名", "create-milestone:新里程碑:說明", $"delete-milestone:{milestone.Id}",
            }));
            Assert.That(Planning.Actors, Is.All.EqualTo(Me));
            Assert.That(applied.Value.Changes[0].Id, Is.Not.Null, "新增的記下新的 id");
            Assert.That(applied.Value.Changes[1].Id, Is.EqualTo(cycle.Id));
            Assert.That(applied.Value.Changes.Select(c => c.Error), Is.All.Null);
        }
    }

    [Test]
    public async Task Apply_TheFirstChangeIsRefused_WritesNoMoreStaysPendingAndReturnsTheFailure()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("甲"), NewCycle("乙"));
        Planning.OnWrite = _ => new Failure(FailureKind.Conflict, WorkErrorCodes.CycleLimitReached, "週期太多了。");

        // Act
        var failed = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await PlanningService.GetAsync(Me, proposal.Id, CancellationToken.None);
        Planning.OnWrite = null;
        var retried = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Failure?.Code, Is.EqualTo(WorkErrorCodes.CycleLimitReached));
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(read.Value.DecidedAt, Is.Null);
            Assert.That(read.Value.Changes.Select(c => c.Error), Is.All.Null, "第一筆就被拒絕時不記錯誤，回到待決定");
            Assert.That(retried.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(Planning.Writes, Has.Count.EqualTo(3), "第一次只試了第一筆");
        }
    }

    [Test]
    public async Task Apply_ALaterChangeIsRefused_KeepsWhatWasWrittenAndRecordsTheErrorOnThatChange()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("甲"), NewMilestone("乙"), NewCycle("丙"));
        Planning.OnWrite = index => index == 1 ? new Failure(FailureKind.Conflict, WorkErrorCodes.MilestoneLimitReached, "里程碑太多了。") : null;

        // Act
        var applied = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("1 筆"));
            Assert.That(applied.Value.Changes[0].Error, Is.Null);
            Assert.That(applied.Value.Changes[1].Error, Is.EqualTo("里程碑太多了。"));
            Assert.That(applied.Value.Changes[1].Id, Is.Null);
            Assert.That(applied.Value.Changes[2].Error, Is.Null);
            Assert.That(Planning.Writes, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task Apply_TheFirstChangeThrows_StaysApplyingAndNeverWritesAgain()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("甲"), NewCycle("乙"));
        Planning.OnWrite = _ => throw new TimeoutException("逾時");

        // Act / Assert
        Assert.ThrowsAsync<TimeoutException>(() => PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None));
        Planning.OnWrite = null;
        var read = await PlanningService.GetAsync(Me, proposal.Id, CancellationToken.None);
        var again = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Applying), "工作引擎可能已經寫入，不能退回待決定");
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalClosed));
            Assert.That(Planning.Writes, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_ALaterChangeThrows_IsAppliedWithThatChangeMarkedFailed()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("甲"), NewCycle("乙"));
        Planning.OnWrite = index => index == 1 ? throw new InvalidOperationException("資料庫掛了") : null;

        // Act
        var applied = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Changes[1].Error, Is.EqualTo("發生未預期的錯誤。"), "不把例外的內容給使用者");
            Assert.That(applied.Value.Changes[0].Error, Is.Null);
        }
    }

    [Test]
    public async Task Apply_WhileBeingApplied_ReadsAsApplyingAndDismissIsClosed()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("甲"));
        var gate = new TaskCompletionSource();
        Planning.OnWrite = _ =>
        {
            gate.Task.Wait(TimeSpan.FromSeconds(10));
            return null;
        };
        var running = PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        while (Planning.Writes.Count == 0)
        {
            await Task.Delay(10);
        }

        // Act
        var read = await PlanningService.GetAsync(Me, proposal.Id, CancellationToken.None);
        var dismissed = await PlanningService.DismissAsync(Me, proposal.Id, CancellationToken.None);
        gate.SetResult();
        await running;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Applying));
            Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalClosed));
        }
    }

    [Test]
    public async Task Get_StuckInApplyingForMoreThanFiveMinutes_ReadsAsAppliedAndMarksTheUnhandledChanges()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("第一個"), NewCycle("第二個"));
        var stored = (await PlanningRowAsync(proposal.Id)).Changes;
        var firstOnly = System.Text.Json.Nodes.JsonNode.Parse(stored)!.AsArray();
        firstOnly[0]!["done"] = true;
        await StickPlanningInApplyingAsync(proposal.Id, firstOnly.ToJsonString());

        // Act
        var fresh = await PlanningService.GetAsync(Me, proposal.Id, CancellationToken.None);
        H.Clock.Advance(AiRules.NewItemProposalApplyStale + TimeSpan.FromSeconds(1));
        var stale = await PlanningService.GetAsync(Me, proposal.Id, CancellationToken.None);
        var apply = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fresh.Value.Status, Is.EqualTo(NewItemProposalStatus.Applying));
            Assert.That(stale.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(stale.Value.Error, Does.Contain("中斷").And.Contain("處理了 1 筆"));
            Assert.That(stale.Value.Changes[0].Error, Is.Null);
            Assert.That(stale.Value.Changes[1].Error, Does.Contain("中斷"));
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalClosed));
            Assert.That(Planning.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_AnotherPersonsProposalOrAProjectNoLongerVisible_IsNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("甲"));
        var other = new Actor(H.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

        // Act
        var byOther = await PlanningService.ApplyAsync(other, proposal.Id, CancellationToken.None);
        var unknown = await PlanningService.ApplyAsync(Me, Guid.NewGuid(), CancellationToken.None);
        Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");
        var hidden = await PlanningService.GetAsync(Me, proposal.Id, CancellationToken.None);
        var hiddenApply = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byOther.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalNotFound));
            Assert.That(unknown.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalNotFound));
            Assert.That(hidden.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalNotFound));
            Assert.That(hiddenApply.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalNotFound));
            Assert.That(Planning.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task Dismiss_ThenApply_WritesNothingAndTheSecondDismissIsClosed()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("甲"));

        // Act
        var dismissed = await PlanningService.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var apply = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var again = await PlanningService.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(NewItemProposalStatus.Dismissed));
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalClosed));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalClosed));
            Assert.That(Planning.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_TwiceInARow_WritesOnlyOnce()
    {
        // Arrange
        var proposal = await CreatedAsync(NewCycle("甲"));

        // Act
        var first = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var second = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Succeeded, Is.True);
            Assert.That(second.Failure?.Code, Is.EqualTo(AiErrorCodes.PlanningProposalClosed));
            Assert.That(Planning.Writes, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_AnUpdate_OnlySendsTheFieldsTheProposalChangesOnTopOfTheCurrentState()
    {
        // Arrange
        var cycle = Planning.AddCycle("舊名", Jan1, Jan1.AddDays(13));
        var milestone = Planning.AddMilestone("上線", Jan1, "舊說明");
        var proposal = await CreatedAsync(
            new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, cycle.Id) { Name = "新名" },
            new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Update, milestone.Id) { DueDate = Jan1.AddDays(9) });
        // 別人在這中間改了日期與說明。
        Planning.Cycles[0] = cycle with { StartDate = Jan1.AddDays(2), EndDate = Jan1.AddDays(20) };
        Planning.Milestones[0] = milestone with { Description = "別人改的說明", Name = "別人改的名稱" };

        // Act
        var applied = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(Planning.CycleDrafts.Single(), Is.EqualTo(new CycleDraft("新名", Jan1.AddDays(2), Jan1.AddDays(20))));
            Assert.That(Planning.MilestoneDrafts.Single(), Is.EqualTo(new MilestoneDraft("別人改的名稱", Jan1.AddDays(9), "別人改的說明")));
            Assert.That(applied.Value.Changes[0].After!.Name, Is.EqualTo("新名"), "卡片照舊顯示建立時的 before／after");
        }
    }

    [Test]
    public async Task Apply_AnUpdateWhoseTargetWasDeletedMeanwhile_FailsThatChangeOnly()
    {
        // Arrange
        var gone = Planning.AddCycle("會消失", Jan1, Jan1.AddDays(13));
        var proposal = await CreatedAsync(NewCycle("先做的"), new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, gone.Id) { Name = "改名" });
        Planning.Cycles.Clear();

        // Act
        var applied = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Changes[0].Error, Is.Null);
            Assert.That(applied.Value.Changes[1].Error, Does.Contain("週期"));
            Assert.That(Planning.Writes, Has.Count.EqualTo(1), "已經不存在的不送");
        }
    }

    [Test]
    public async Task Apply_TheOnlyChangeIsAnUpdateOfADeletedTarget_StaysPendingWithTheFailure()
    {
        // Arrange
        var gone = Planning.AddCycle("會消失", Jan1, Jan1.AddDays(13));
        var proposal = await CreatedAsync(new PlanningChangeDraft(PlanningTarget.Cycle, PlanningAction.Update, gone.Id) { Name = "改名" });
        Planning.Cycles.Clear();

        // Act
        var failed = await PlanningService.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await PlanningService.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
        }
    }
}
