using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.NewItems;

/// <summary>套用與作廢：依序以套用的人的身分建立、失敗時的狀態、不會建立兩次、停在「建立中」的處理。</summary>
[TestFixture]
public sealed class NewItemProposalApplyTests : NewItemProposalTestBase
{
    [Test]
    public async Task Apply_AllSucceed_CreatesInOrderAsTheApplyingPersonAndHangsChildrenOnTheirParents()
    {
        // Arrange
        var proposal = await CreatedAsync(
            Epic(),
            Task("備份資料庫") with { Priority = "high", DueDate = "2026-12-01", Estimate = 2m, AssigneeId = ItemSamples.MemberId, Description = "做完整備份", Fields = new Dictionary<string, System.Text.Json.JsonElement> { ["category"] = Json("資料") } },
            Task("切換 DNS", parentRef: null, parentKey: "WEB-12"));

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        var creates = H.WorkItems.Creates;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(applied.Value.CreatedKeys, Is.EqualTo(new[] { "WEB-101", "WEB-102", "WEB-103" }));
            Assert.That(creates.Select(c => c.Actor), Is.All.EqualTo(Me));
            Assert.That(creates.Select(c => c.ProjectKey), Is.All.EqualTo("WEB"));
            Assert.That(creates.Select(c => c.Item.Title), Is.EqualTo(new[] { "搬到 AWS", "備份資料庫", "切換 DNS" }));
            Assert.That(creates[0].Item.ParentKey, Is.Null);
            Assert.That(creates[1].Item.ParentKey, Is.EqualTo("WEB-101"), "parentRef 換成剛建立的編號");
            Assert.That(creates[2].Item.ParentKey, Is.EqualTo("WEB-12"));
            Assert.That(creates[1].Item.Priority, Is.EqualTo(Priority.High));
            Assert.That(creates[1].Item.DueDate, Is.EqualTo(new DateOnly(2026, 12, 1)));
            Assert.That(creates[1].Item.Estimate, Is.EqualTo(2m));
            Assert.That(creates[1].Item.AssigneeId, Is.EqualTo(ItemSamples.MemberId));
            Assert.That(creates[1].Item.Description, Is.EqualTo("做完整備份"));
            Assert.That(creates[1].Item.Fields["category"].GetString(), Is.EqualTo("資料"));
            Assert.That(creates[0].Item.Priority, Is.EqualTo(Priority.None));
        }
    }

    [Test]
    public async Task Apply_Result_IsReadBackAsApplied()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic());

        // Act
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(read.Value.CreatedKeys, Is.EqualTo(new[] { "WEB-101" }));
            Assert.That(read.Value.DecidedAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task Apply_FirstItemFails_CreatesNothingAndStaysPendingAndCanBeAppliedLater()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic(), Task("x"));
        H.WorkItems.OnCreate = (_, _) => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");

        // Act
        var failed = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        H.WorkItems.OnCreate = null;
        var retried = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(failed.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(read.Value.DecidedAt, Is.Null);
            Assert.That(read.Value.CreatedKeys, Is.All.Null);
            Assert.That(retried.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
        }
    }

    [Test]
    public async Task Apply_FirstItemThrows_StaysPendingAndRethrows()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic());
        H.WorkItems.OnCreate = (_, _) => throw new InvalidOperationException("壞掉了");

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None));
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
    }

    [Test]
    public async Task Apply_MiddleItemFails_KeepsTheCreatedOnesSkipsItsChildrenAndExplains()
    {
        // Arrange
        var proposal = await CreatedAsync(
            Epic("第一個史詩", "e1"), Epic("第二個史詩", "e2"), Task("第二個史詩的任務", "e2"), Task("第一個史詩的任務", "e1"));
        H.WorkItems.OnCreate = (index, item) => index == 1
            ? WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.Forbidden, "欄位不合規定。")
            : ItemSamples.Item($"WEB-{101 + index}", item.Title);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.CreatedKeys, Is.EqualTo(new[] { "WEB-101", null, null, "WEB-103" }));
            Assert.That(H.WorkItems.Creates, Has.Count.EqualTo(3), "掛在失敗項目底下的不會送去建立");
            Assert.That(H.WorkItems.Creates[2].Item.ParentKey, Is.EqualTo("WEB-101"));
            Assert.That(applied.Value.Error, Is.EqualTo("有幾個沒有建立：第 2 個「第二個史詩」（欄位不合規定。）；第 3 個「第二個史詩的任務」（上層沒有建立成功）。其餘已經建立。"));
        }
    }

    [Test]
    public async Task Apply_LaterItemThrows_IsAppliedWithAnErrorAndDoesNotLeakTheMessage()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic(), Task("x"));
        H.WorkItems.OnCreate = (index, item) => index == 1 ? throw new InvalidOperationException("連線字串 secret") : ItemSamples.Item("WEB-101", item.Title);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("第 2 個").And.Contain("發生未預期的錯誤").And.Not.Contain("secret"));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalClosed));
            Assert.That(H.WorkItems.Creates, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Apply_Twice_SecondIsClosedAndNothingIsCreatedAgain()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic());
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Act
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalClosed));
            Assert.That(H.WorkItems.Creates, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_PressedTwiceAtTheSameTime_CreatesOnlyOnce()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic(), Task("x"));
        var gate = new TaskCompletionSource();
        H.WorkItems.OnCreate = (index, item) =>
        {
            gate.Task.Wait(TimeSpan.FromSeconds(10));
            return ItemSamples.Item($"WEB-{101 + index}", item.Title);
        };

        // Act
        var first = Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        while (H.WorkItems.Creates.Count == 0)
        {
            await System.Threading.Tasks.Task.Delay(10);
        }

        var second = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        gate.SetResult();
        var firstResult = await first;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalClosed));
            Assert.That(firstResult.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(H.WorkItems.Creates, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Apply_WhileBeingCreated_ReadsAsApplyingAndDismissIsClosed()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic());
        var gate = new TaskCompletionSource();
        H.WorkItems.OnCreate = (_, item) =>
        {
            gate.Task.Wait(TimeSpan.FromSeconds(10));
            return ItemSamples.Item("WEB-101", item.Title);
        };
        var running = Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        while (H.WorkItems.Creates.Count == 0)
        {
            await System.Threading.Tasks.Task.Delay(10);
        }

        // Act
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);
        gate.SetResult();
        await running;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Applying));
            Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalClosed));
        }
    }

    [Test]
    public async Task Get_StuckInApplyingForMoreThanFiveMinutes_ReadsAsAppliedWithTheInterruptedNote()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic("第一個"), Epic("第二個", "e2"));
        await StickInApplyingAsync(proposal.Id, "[\"WEB-101\",null]");

        // Act
        var fresh = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        H.Clock.Advance(AiRules.NewItemProposalApplyStale + TimeSpan.FromSeconds(1));
        var stale = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        var apply = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fresh.Value.Status, Is.EqualTo(NewItemProposalStatus.Applying));
            Assert.That(stale.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(stale.Value.CreatedKeys, Is.EqualTo(new[] { "WEB-101", null }));
            Assert.That(stale.Value.Error, Does.StartWith("建立的過程中斷了。").And.Contain("第 2 個「第二個」"));
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalClosed));
            Assert.That(H.WorkItems.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_AnotherPersonsProposalOrProjectNoLongerVisible_IsNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic());
        var other = new Actor(H.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

        // Act
        var byOther = await Proposals.ApplyAsync(other, proposal.Id, CancellationToken.None);
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");
        var invisible = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var unknown = await Proposals.ApplyAsync(Me, Guid.NewGuid(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byOther.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalNotFound));
            Assert.That(invisible.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalNotFound));
            Assert.That(unknown.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalNotFound));
            Assert.That(H.WorkItems.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task Dismiss_Pending_IsDismissedAndCannotBeAppliedOrDismissedAgain()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic());

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var apply = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var again = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(NewItemProposalStatus.Dismissed));
            Assert.That(dismissed.Value.DecidedAt, Is.Not.Null);
            Assert.That(apply.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalClosed));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalClosed));
            Assert.That(H.WorkItems.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task Dismiss_AppliedOrAnotherPersons_IsClosedOrNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync(Epic());
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Act
        var afterApply = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var byOther = await Proposals.DismissAsync(new Actor(H.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: false), proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterApply.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalClosed));
            Assert.That(byOther.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalNotFound));
        }
    }

    // 模擬程序在建立的中途結束：資料列停在「建立中」，只記下前面建好的編號。
    private async Task StickInApplyingAsync(Guid proposalId, string createdKeys)
    {
        var now = H.Clock.GetUtcNow();
        await using var db = await H.Host.CreateDbContextAsync();
        await db.NewItemProposals.Where(p => p.Id == proposalId).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, PmTool.Ai.Persistence.NewItemProposalRow.ApplyingStatus)
                .SetProperty(p => p.DecidedAt, now)
                .SetProperty(p => p.CreatedKeys, createdKeys));
    }
}
