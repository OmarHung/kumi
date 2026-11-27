using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRevisions;

/// <summary>建立刪除建議：每一條檢查、下層沒選的錯誤、只存不刪。</summary>
[TestFixture]
public sealed class ItemDeletionProposalCreateTests : ItemRevisionTestBase
{
    private IItemDeletionProposalCreator Creator => H.Host.Get<IItemDeletionProposalCreator>();

    private IItemDeletionProposalService Proposals => H.Host.Get<IItemDeletionProposalService>();

    private Task<Result<ItemDeletionProposal>> CreateAsync(IReadOnlyList<string> keys, string? reason = null, Actor? actor = null) =>
        Creator.CreateAsync(actor ?? Me, new ItemDeletionProposalDraft("WEB", keys) { Reason = reason }, CancellationToken.None);

    [Test]
    public async Task Create_ValidKeys_StoresAPendingProposalWithTheCurrentTitlesAndDeletesNothing()
    {
        // Arrange
        var conversation = Guid.CreateVersion7();

        // Act
        var created = await Creator.CreateAsync(
            Me, new ItemDeletionProposalDraft("WEB", ["WEB-12", "WEB-13"]) { ConversationId = conversation, Reason = "  重複建立的  " }, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
            Assert.That(created.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(created.Value.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(created.Value.Items.Select(i => (i.ItemKey, i.Title)), Is.EqualTo(new[] { ("WEB-12", "改版首頁"), ("WEB-13", "第二個任務") }));
            Assert.That(created.Value.Reason, Is.EqualTo("重複建立的"));
            Assert.That(created.Value.DeletedKeys, Is.Empty);
            Assert.That(read.Value.Items, Has.Count.EqualTo(2));
            Assert.That(Bulk.Deletes, Is.Empty);
        }
    }

    [Test]
    public async Task Create_ProjectNotVisible_IsReturnedAsIs()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var created = await CreateAsync(["WEB-12"]);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task Create_WithoutDeletePermission_IsForbiddenWithAToolFriendlyMessage()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => Detail(ItemSamples.EditorPermissions);

        // Act
        var created = await CreateAsync(["WEB-12"]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(created.Failure?.Message, Is.EqualTo("你沒有刪除項目的權限。"));
        }
    }

    [Test]
    public async Task Create_NoKeysOrTooMany_IsInvalid()
    {
        // Arrange
        var tooMany = Enumerable.Range(1, AiRules.MaxItemsPerDeletionProposal + 1).Select(n => $"WEB-{n}").ToList();

        // Act
        var none = await CreateAsync([]);
        var many = await CreateAsync(tooMany);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(none.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(none.Failure, "itemKeys"), Does.Contain("1 到 50"));
            Assert.That(IssueAt(many.Failure, "itemKeys"), Does.Contain("1 到 50"));
        }
    }

    [Test]
    public async Task Create_UnknownDuplicateAndOtherProjectKeys_AreAllReportedAtOnceWithTheirIndex()
    {
        // Arrange
        H.WorkItems.Items["API-1"] = ItemSamples.Item("API-1", "別的專案", "API");

        // Act
        var created = await CreateAsync(["WEB-12", "WEB-99", "web-12", "API-1"]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(created.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "itemKeys[1]", "itemKeys[2]", "itemKeys[3]" }));
            Assert.That(IssueAt(created.Failure, "itemKeys[1]"), Does.Contain("WEB-99"));
            Assert.That(IssueAt(created.Failure, "itemKeys[2]"), Does.Contain("不只一次"));
        }
    }

    [Test]
    public async Task Create_ItemWithUnselectedChildren_ListsTheChildrenSoTheModelCanDecide()
    {
        // Arrange
        H.WorkItems.Items["WEB-21"] = ItemSamples.Item("WEB-21", "下層一") with { ParentKey = "WEB-12" };
        H.WorkItems.Items["WEB-22"] = ItemSamples.Item("WEB-22", "下層二") with { ParentKey = "WEB-12" };

        // Act
        var created = await CreateAsync(["WEB-12"]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(created.Failure, "itemKeys[0]"), Does.Contain("WEB-21").And.Contain("WEB-22").And.Contain("下層"));
        }
    }

    [Test]
    public async Task Create_ListsAtMostTenChildren()
    {
        // Arrange
        foreach (var n in Enumerable.Range(30, 15))
        {
            H.WorkItems.Items[$"WEB-{n}"] = ItemSamples.Item($"WEB-{n}", $"下層 {n}") with { ParentKey = "WEB-12" };
        }

        // Act
        var created = await CreateAsync(["WEB-12"]);

        // Assert
        var message = IssueAt(created.Failure, "itemKeys[0]");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("WEB-30").And.Contain("WEB-39").And.Not.Contain("WEB-40"));
            Assert.That(message, Does.Contain("…等"));
        }
    }

    [Test]
    public async Task Create_ItemWithMoreChildrenThanCanBeChecked_IsRefusedInsteadOfLettingItThrough()
    {
        // Arrange：只讀得到前 50 個下層，讀不完就沒辦法確認都選進來了。
        foreach (var n in Enumerable.Range(100, 55))
        {
            H.WorkItems.Items[$"WEB-{n}"] = ItemSamples.Item($"WEB-{n}", $"下層 {n}") with { ParentKey = "WEB-12" };
        }

        // Act
        var created = await CreateAsync(["WEB-12"]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(created.Failure, "itemKeys[0]"), Does.Contain("下層太多"));
        }
    }

    [Test]
    public async Task Create_ChildrenSelectedTogetherWithTheParent_IsAllowed()
    {
        // Arrange
        H.WorkItems.Items["WEB-21"] = ItemSamples.Item("WEB-21", "下層一") with { ParentKey = "WEB-12" };

        // Act
        var created = await CreateAsync(["WEB-12", "WEB-21"]);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message + string.Join("；", created.Failure?.Details.Select(d => d.Message) ?? []));
    }

    [Test]
    public async Task Create_ReasonTooLongOrWithControlCharacters_IsInvalid()
    {
        // Act
        var tooLong = await CreateAsync(["WEB-12"], new string('字', 201));
        var control = await CreateAsync(["WEB-12"], "第一行\n第二行");
        var nul = await CreateAsync(["WEB-12"], "壞\0掉");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(tooLong.Failure, "reason"), Does.Contain("200"));
            Assert.That(IssueAt(control.Failure, "reason"), Does.Contain("控制字元"));
            Assert.That(IssueAt(nul.Failure, "reason"), Does.Contain("控制字元"));
        }
    }

    [Test]
    public async Task Create_BlankReason_IsStoredAsNothing()
    {
        // Act
        var created = await CreateAsync(["WEB-12"], "   ");

        // Assert
        Assert.That(created.Value.Reason, Is.Null);
    }

    [Test]
    public async Task Create_Invalid_StoresNothing()
    {
        // Arrange
        var before = await CountAsync();

        // Act
        await CreateAsync(["WEB-12", "WEB-99"]);

        // Assert
        Assert.That(await CountAsync(), Is.EqualTo(before));
    }

    [Test]
    public async Task Get_ProposalOfAnotherPerson_IsNotFound()
    {
        // Arrange
        var created = await CreateAsync(["WEB-12"]);
        var other = new Actor(Me.OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: false);

        // Act
        var read = await Proposals.GetAsync(other, created.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemDeletionProposalNotFound));
    }

    private async Task<int> CountAsync()
    {
        await using var db = await H.Host.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.ItemDeletionProposals);
    }
}
