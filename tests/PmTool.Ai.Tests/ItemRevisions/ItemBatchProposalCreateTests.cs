using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRevisions;

/// <summary>建立批次修改建議：每一筆走單一項目建議的檢查，細項路徑多一層 items[n]，一次回全部、什麼都不存。</summary>
[TestFixture]
public sealed class ItemBatchProposalCreateTests : ItemRevisionTestBase
{
    private IItemBatchProposalCreator Creator => H.Host.Get<IItemBatchProposalCreator>();

    private IItemBatchProposalService Proposals => H.Host.Get<IItemBatchProposalService>();

    private static ItemBatchEntryDraft Entry(string key, IReadOnlyList<ItemFieldChange>? changes = null, string? comment = null, string? revision = null) =>
        new(key, changes ?? [Change("dueDate", "2026-12-05")], comment, revision);

    private Task<Result<ItemBatchProposal>> CreateAsync(params ItemBatchEntryDraft[] entries) => CreateAsync(Me, entries);

    private Task<Result<ItemBatchProposal>> CreateAsync(Actor actor, params ItemBatchEntryDraft[] entries) =>
        Creator.CreateAsync(actor, new ItemBatchProposalDraft("WEB", entries) { ConversationId = Guid.CreateVersion7() }, CancellationToken.None);

    [Test]
    public async Task Create_ValidEntries_StoresAPendingProposalWithEachEntrysOwnChanges()
    {
        // Act
        var created = await CreateAsync(
            Entry("WEB-12", [Change("dueDate", "2026-12-05")]),
            Entry("web-13", [Change("dueDate", "2026-12-06"), Change("priority", "high")], "  順延  "),
            Entry("WEB-14", [], "只留言"));
        var read = await Proposals.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message + string.Join("；", created.Failure?.Details.Select(d => d.Path + d.Message) ?? []));
            Assert.That(created.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(created.Value.Items.Select(i => i.ItemKey), Is.EqualTo(new[] { "WEB-12", "WEB-13", "WEB-14" }));
            Assert.That(created.Value.Items.Select(i => i.ItemTitle), Is.EqualTo(new[] { "改版首頁", "第二個任務", "第三個任務" }));
            Assert.That(created.Value.Items[0].Changes.Single().Value.GetString(), Is.EqualTo("2026-12-05"));
            Assert.That(created.Value.Items[1].Changes.Select(c => c.Field), Is.EqualTo(new[] { "dueDate", "priority" }));
            Assert.That(created.Value.Items[1].Comment, Is.EqualTo("順延"));
            Assert.That(created.Value.Items[2].Changes, Is.Empty);
            Assert.That(created.Value.Items.Select(i => i.Error), Is.All.Null);
            Assert.That(read.Value.Items[1].Changes[1].Value.GetString(), Is.EqualTo("high"));
            Assert.That(H.WorkItems.Updates, Is.Empty);
        }
    }

    [Test]
    public async Task Create_ProjectNotVisible_IsReturnedAsIs()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var created = await CreateAsync(Entry("WEB-12"));

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task Create_NoEntriesOrTooMany_IsInvalid()
    {
        // Arrange
        var tooMany = Enumerable.Range(0, AiRules.MaxItemsPerBatchProposal + 1).Select(_ => Entry("WEB-12")).ToArray();

        // Act
        var none = await CreateAsync();
        var many = await CreateAsync(tooMany);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(none.Failure, "items"), Does.Contain("1 到 20"));
            Assert.That(IssueAt(many.Failure, "items"), Does.Contain("1 到 20"));
        }
    }

    [Test]
    public async Task Create_ProblemsInSeveralEntries_AreAllReportedWithTheEntryIndexInThePath()
    {
        // Act
        var created = await CreateAsync(
            Entry("WEB-12"),
            Entry("WEB-13", [Change("dueDate", "明天")]),
            Entry("WEB-99"),
            Entry("WEB-12"),
            Entry("WEB-14", [Change("status", "不存在的狀態"), Change("nope", 1)]));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(
                created.Failure!.Details.Select(d => d.Path),
                Is.EqualTo(new[] { "items[1].changes[0]", "items[2].itemKey", "items[3].itemKey", "items[4].changes[0]", "items[4].changes[1]" }));
            Assert.That(IssueAt(created.Failure, "items[2].itemKey"), Does.Contain("WEB-99"));
            Assert.That(IssueAt(created.Failure, "items[3].itemKey"), Does.Contain("不只一次"));
            Assert.That(IssueAt(created.Failure, "items[4].changes[0]"), Does.Contain("可用的狀態"));
        }
    }

    [Test]
    public async Task Create_EntryWithNothingToChange_IsInvalidAtItsOwnPath()
    {
        // Act
        var created = await CreateAsync(Entry("WEB-12"), Entry("WEB-13", []));

        // Assert
        Assert.That(IssueAt(created.Failure, "items[1].changes"), Does.Contain("至少要有一個"));
    }

    [Test]
    public async Task Create_DescriptionWithoutRevision_IsInvalidAtTheEntrysDescriptionRevisionPath()
    {
        // Act
        var missing = await CreateAsync(Entry("WEB-12", [Change("description", "新說明")]));
        var given = await CreateAsync(Entry("WEB-12", [Change("description", "新說明")], revision: OldRevision));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(missing.Failure, "items[0].descriptionRevision"), Does.Contain("descriptionRevision"));
            Assert.That(given.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_CommentWithControlCharacter_IsInvalidAtTheEntrysCommentPath()
    {
        // Act
        var created = await CreateAsync(Entry("WEB-12", comment: "壞\0掉"));

        // Assert
        Assert.That(IssueAt(created.Failure, "items[0].comment"), Does.Contain("控制字元"));
    }

    [Test]
    public async Task Create_ReadOnlyPerson_IsForbiddenWithAToolFriendlyMessage()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([PmTool.Work.WorkPermissions.ItemRead]);

        // Act
        var created = await CreateAsync(Entry("WEB-12"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(created.Failure?.Message, Is.EqualTo("你沒有修改項目的權限，只能讀。"));
        }
    }

    [Test]
    public async Task Create_Invalid_StoresNothing()
    {
        // Act
        await CreateAsync(Entry("WEB-12"), Entry("WEB-99"));

        // Assert
        await using var db = await H.Host.CreateDbContextAsync();
        Assert.That(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.ItemBatchProposals), Is.Zero);
    }

    [Test]
    public async Task Get_ProposalOfAnotherPerson_IsNotFound()
    {
        // Arrange
        var created = await CreateAsync(Entry("WEB-12"));
        var other = new Actor(Me.OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: true);

        // Act
        var read = await Proposals.GetAsync(other, created.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemBatchProposalNotFound));
    }
}
