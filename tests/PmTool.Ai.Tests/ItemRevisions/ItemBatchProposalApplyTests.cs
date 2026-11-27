using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRevisions;

/// <summary>批次修改建議的套用與作廢：每一筆以套用的人的身分依序套用、失敗時的狀態、別人的一律找不到。</summary>
[TestFixture]
public sealed class ItemBatchProposalApplyTests : ItemRevisionTestBase
{
    private IItemBatchProposalService Proposals => H.Host.Get<IItemBatchProposalService>();

    private static ItemBatchEntryDraft Entry(string key, IReadOnlyList<ItemFieldChange>? changes = null, string? comment = null, string? revision = null) =>
        new(key, changes ?? [Change("dueDate", "2026-12-05")], comment, revision);

    private async Task<ItemBatchProposal> CreatedAsync(params ItemBatchEntryDraft[] entries)
    {
        var created = await H.Host.Get<IItemBatchProposalCreator>().CreateAsync(
            Me, new ItemBatchProposalDraft("WEB", entries), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message + string.Join("；", created.Failure?.Details.Select(d => d.Path + d.Message) ?? []));
        return created.Value;
    }

    private static Failure Refused(string message) => new(FailureKind.Forbidden, WorkErrorCodes.Forbidden, message);

    [Test]
    public async Task Apply_EveryEntryIsWrittenWithItsOwnValuesAsTheApplyingPerson()
    {
        // Arrange
        var proposal = await CreatedAsync(
            Entry("WEB-12", [Change("dueDate", "2026-12-05")]),
            Entry("WEB-13", [Change("dueDate", "2026-12-06"), Change("status", "done")], "順延了"),
            Entry("WEB-14", [Change("description", "新說明")], revision: OldRevision));

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        var updates = H.WorkItems.Updates;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(applied.Value.Items.Select(i => i.Error), Is.All.Null);
            Assert.That(updates.Select(u => (u.Actor, u.Key)), Is.EqualTo(new[] { (Me, "WEB-12"), (Me, "WEB-13"), (Me, "WEB-14") }));
            Assert.That(updates[0].Patch.DueDate.Value, Is.EqualTo(new DateOnly(2026, 12, 5)));
            Assert.That(updates[1].Patch.DueDate.Value, Is.EqualTo(new DateOnly(2026, 12, 6)));
            Assert.That(updates[2].Patch.Description.Value, Is.EqualTo("新說明"));
            Assert.That(updates[2].Patch.ExpectedDescriptionRevision, Is.EqualTo(OldRevision));
            Assert.That(H.WorkItems.Transitions.Single(), Is.EqualTo((Me, "WEB-13", "done")));
            Assert.That(H.Activity.Comments.Single(), Is.EqualTo((Me, "WEB-13", "順延了")));
        }
    }

    [Test]
    public async Task Apply_EveryEntryFailsOnItsFirstStep_ChangesNothingAndStaysPendingWithTheFirstFailure()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13"));
        H.WorkItems.OnUpdate = (key, _) => Refused(key == "WEB-12" ? "沒有權限。" : "別的原因。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(applied.Failure?.Message, Is.EqualTo("沒有權限。"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(read.Value.Items.Select(i => i.Error), Is.All.Null);
            Assert.That(H.WorkItems.Updates.Select(u => u.Key), Is.EqualTo(new[] { "WEB-12", "WEB-13" }));
        }
    }

    [Test]
    public async Task Apply_FirstEntryFailsButALaterOneSucceeds_IsAppliedAndRecordsTheErrorOnTheFirst()
    {
        // Arrange：第一個項目已刪除或說明衝突時，後面能套用的不能因此永遠套用不了。
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13"), Entry("WEB-14"));
        H.WorkItems.OnUpdate = (key, _) => key == "WEB-12" ? Refused("項目已經刪除。") : ItemSamples.Item(key);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Items.Select(i => i.Error), Is.EqualTo(new[] { "項目已經刪除。", null, null }));
            Assert.That(applied.Value.Error, Does.StartWith("有幾筆沒有完全套用：WEB-12（項目已經刪除。）"));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalClosed));
        }
    }

    [Test]
    public async Task Apply_TimesOutAfterSomeEntries_MarksTheRestAsNotAppliedAndIsApplied()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13"), Entry("WEB-14"));
        H.WorkItems.OnUpdate = (key, _) => key == "WEB-13" ? throw new OperationCanceledException() : ItemSamples.Item(key);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Items.Select(i => i.Error), Is.EqualTo(new[] { null, "逾時沒有套用", "逾時沒有套用" }));
            Assert.That(H.WorkItems.Updates.Select(u => u.Key), Is.EqualTo(new[] { "WEB-12", "WEB-13" }));
        }
    }

    [Test]
    public async Task Apply_TimesOutBeforeAnyProgress_IsAClearFailureAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13"));
        H.WorkItems.OnUpdate = (_, _) => throw new OperationCanceledException();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ApplyTimedOut));
            Assert.That(applied.Failure?.Message, Does.Contain("逾時"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task Apply_LaterEntryFails_KeepsTheEarlierOnesContinuesAndRecordsTheErrorOnThatEntry()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13"), Entry("WEB-14"));
        H.WorkItems.OnUpdate = (key, _) => key == "WEB-13" ? Refused("這個項目鎖住了。") : ItemSamples.Item(key);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Items.Select(i => i.Error), Is.EqualTo(new[] { null, "這個項目鎖住了。", null }));
            Assert.That(applied.Value.Error, Is.EqualTo("有幾筆沒有完全套用：WEB-13（這個項目鎖住了。）。其餘已經套用。"));
            Assert.That(H.WorkItems.Updates.Select(u => u.Key), Is.EqualTo(new[] { "WEB-12", "WEB-13", "WEB-14" }));
            Assert.That(read.Value.Items[1].Error, Is.EqualTo("這個項目鎖住了。"));
            Assert.That(read.Value.Error, Is.EqualTo(applied.Value.Error));
        }
    }

    [Test]
    public async Task Apply_StatusFailsAfterFieldsOfALaterEntry_RecordsThePartialExplanationOnThatEntry()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13", [Change("title", "新標題"), Change("status", "done")], "留言"));
        H.WorkItems.OnTransition = (_, _) => WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.TransitionNotAllowed, "不能這樣轉換。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Items[0].Error, Is.Null);
            Assert.That(applied.Value.Items[1].Error, Does.Contain("狀態").And.Contain("不能這樣轉換").And.Contain("留言"));
            Assert.That(applied.Value.Error, Does.StartWith("有幾筆沒有完全套用：WEB-13"));
            Assert.That(H.Activity.Comments, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_DescriptionConflictInAMiddleEntry_IsRecordedOnThatEntryOnly()
    {
        // Arrange
        var proposal = await CreatedAsync(
            Entry("WEB-12"), Entry("WEB-13", [Change("description", "新說明")], revision: OldRevision), Entry("WEB-14"));
        H.WorkItems.OnUpdate = (key, patch) => patch.ExpectedDescriptionRevision is not null
            ? WorkSamples.Failure(FailureKind.Conflict, WorkErrorCodes.ItemDescriptionConflict, "說明被改過了。")
            : ItemSamples.Item(key);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Items.Select(i => i.Error), Is.EqualTo(new[] { null, "說明被改過了。", null }));
            Assert.That(H.WorkItems.Updates, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task Apply_LaterEntryThrows_IsAppliedWithAnErrorNotA500AndTheMessageDoesNotLeak()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13"), Entry("WEB-14"));
        H.WorkItems.OnUpdate = (key, _) => key == "WEB-13" ? throw new InvalidOperationException("連線字串 secret") : ItemSamples.Item(key);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Items[1].Error, Is.EqualTo("發生未預期的錯誤。"));
            Assert.That(applied.Value.Error, Does.Not.Contain("secret"));
            Assert.That(applied.Value.Items[2].Error, Is.Null);
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalClosed));
            Assert.That(H.WorkItems.Updates, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task Apply_FirstEntryThrows_ChangesNothingAndStaysPending()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13"));
        H.WorkItems.OnUpdate = (_, _) => throw new InvalidOperationException("壞掉了");

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None));
        Assert.That((await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None)).Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
    }

    [Test]
    public async Task Apply_Twice_SecondIsClosedAndWritesNothingMore()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"));
        await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Act
        var again = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(again.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalClosed));
            Assert.That(H.WorkItems.Updates, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_TwoAtTheSameTime_WritesOnlyOnce()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"), Entry("WEB-13"));

        // Act
        var results = await Task.WhenAll(
            Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None), Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(H.WorkItems.Updates, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Dismiss_PendingProposal_IsDismissedAndCannotBeAppliedAfterwards()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"));

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var dismissedAgain = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(ItemProposalStatus.Dismissed));
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalClosed));
            Assert.That(dismissedAgain.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalClosed));
            Assert.That(H.WorkItems.Updates, Is.Empty);
        }
    }

    [Test]
    public async Task ApplyAndDismiss_ProposalOfAnotherPersonOrUnknown_AreNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"));
        var other = new Actor(Me.OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: true);

        // Act
        var byOther = await Proposals.ApplyAsync(other, proposal.Id, CancellationToken.None);
        var dismissByOther = await Proposals.DismissAsync(other, proposal.Id, CancellationToken.None);
        var unknown = await Proposals.ApplyAsync(Me, Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byOther.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalNotFound));
            Assert.That(dismissByOther.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalNotFound));
            Assert.That(unknown.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalNotFound));
            Assert.That(H.WorkItems.Updates, Is.Empty);
        }
    }

    [Test]
    public async Task ApplyGetAndDismiss_ProjectNoLongerVisible_AreNotFound()
    {
        // Arrange
        var proposal = await CreatedAsync(Entry("WEB-12"));
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalNotFound));
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalNotFound));
            Assert.That(dismissed.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalNotFound));
            Assert.That(H.WorkItems.Updates, Is.Empty);
        }
    }
}
