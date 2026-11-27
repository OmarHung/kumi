using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Proposals;

/// <summary>每次回覆前告訴模型的「這則對話的建議與現在的狀態」：七種建議、別人的與讀不到的不列、上限。</summary>
[TestFixture]
public sealed class ProposalLedgerTests : ProposalTestBase
{
    [Test]
    public async Task List_WithNoProposalsInTheConversation_IsEmpty()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public async Task List_ReadsAllSevenKinds_OldestFirst_WithCurrentStateAndNumbersOnlySummaries()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var item = AddItemProposal(Me.PersonId, ItemProposalStatus.Applied);
        var newItems = NewItemsOf(7, ["WEB-38", "WEB-39", "WEB-40", "WEB-41", "WEB-42", "WEB-43", "WEB-44"], NewItemProposalStatus.Applied);
        Proposals.Add(Me.PersonId, newItems);
        var deletion = new ItemDeletionProposal(Guid.CreateVersion7(), "WEB", [new ItemDeletionTarget("WEB-45", "秘密"), new ItemDeletionTarget("WEB-47", "秘密")], ItemProposalStatus.Pending, DateTimeOffset.UnixEpoch);
        Proposals.Add(Me.PersonId, deletion);
        var batch = new ItemBatchProposal(
            Guid.CreateVersion7(),
            "WEB",
            [
                new ItemBatchEntry("WEB-1", "秘密", [new ItemFieldChange("dueDate", default)], null, null),
                new ItemBatchEntry("WEB-2", "秘密", [new ItemFieldChange("dueDate", default)], null, null),
            ],
            ItemProposalStatus.Dismissed,
            DateTimeOffset.UnixEpoch);
        Proposals.Add(Me.PersonId, batch);
        var members = new MemberProposal(
            Guid.CreateVersion7(), "WEB", [new MemberChange(Guid.NewGuid(), "秘密", "contributor", "執行者", null, null)], MemberProposalStatus.Pending, DateTimeOffset.UnixEpoch);
        Proposals.Add(Me.PersonId, members);
        var project = new ProjectProposal(Guid.CreateVersion7(), "NEW", "秘密", "simple_tasks", "簡單任務", false, null, ProjectProposalStatus.Applied, DateTimeOffset.UnixEpoch)
        {
            CreatedProjectKey = "NEW",
        };
        Proposals.Add(Me.PersonId, project);
        H.Changes.OnGet = (_, number) => WorkSamples.ProposedDetail(number) with
        {
            ChangeSet = WorkSamples.ProposedDetail(number).ChangeSet with { Status = ChangeSetStatus.Rejected },
        };
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_changeset") with { ChangeSetNumber = 12 }, Tool("propose_item_changes") with { ItemProposalId = item.Id });
        await SeedReplyAsync(conversation.Id, 2, Tool("propose_new_items") with { NewItemProposalId = newItems.Id }, Tool("propose_item_deletions") with { ItemDeletionProposalId = deletion.Id });
        await SeedReplyAsync(conversation.Id, 3, Tool("propose_bulk_item_changes") with { ItemBatchProposalId = batch.Id }, Tool("propose_member_changes") with { MemberProposalId = members.Id });
        await SeedReplyAsync(conversation.Id, 4, Tool("propose_project") with { ProjectProposalId = project.Id });

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries.Select(e => e.Kind), Is.EqualTo(new[]
            {
                ProposalKind.ChangeSet, ProposalKind.ItemChanges, ProposalKind.NewItems, ProposalKind.ItemDeletions,
                ProposalKind.BulkItemChanges, ProposalKind.Members, ProposalKind.Project,
            }));
            Assert.That(entries.Select(e => e.State), Is.EqualTo(new[]
            {
                ProposalState.Rejected, ProposalState.Applied, ProposalState.Applied, ProposalState.Pending,
                ProposalState.Dismissed, ProposalState.Pending, ProposalState.Applied,
            }));
            Assert.That(entries[0].Reference, Is.EqualTo("changeset:12"));
            Assert.That(entries[1].Reference, Is.EqualTo(item.Id.ToString()));
            Assert.That(entries[1].Summary, Is.EqualTo("改 WEB-12 的 dueDate、priority"));
            Assert.That(entries[2].Summary, Is.EqualTo("新增 7 個項目，已建立 WEB-38～WEB-44"));
            Assert.That(entries[3].Summary, Is.EqualTo("刪除 2 個項目：WEB-45、WEB-47"));
            Assert.That(entries[4].Summary, Is.EqualTo("改 2 個項目：WEB-1、WEB-2，欄位 dueDate"));
            Assert.That(entries[5].Summary, Is.EqualTo("把 1 個人加進專案"));
            Assert.That(entries[6].Summary, Is.EqualTo("已建立專案 NEW"));
            Assert.That(entries.Select(e => e.Summary), Has.None.Contain("秘密"));
        }
    }

    [Test]
    public async Task List_PendingNewItems_ShowsAppliedAfterTheUserPressedTheButton()
    {
        // Arrange：07:51 使用者按了「全部建立」。助理之後的回覆必須讀到現在的狀態，不是提出時的。
        var conversation = await H.NewConversationAsync();
        var proposal = NewItemsOf(2, [null, null], NewItemProposalStatus.Pending);
        Proposals.Add(Me.PersonId, proposal);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_new_items") with { NewItemProposalId = proposal.Id });
        var before = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);
        Proposals.NewItems[proposal.Id] = proposal with { Status = NewItemProposalStatus.Applied, CreatedKeys = ["WEB-38", "WEB-39"] };

        // Act
        var after = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Single().State, Is.EqualTo(ProposalState.Pending));
            Assert.That(after.Single().State, Is.EqualTo(ProposalState.Applied));
            Assert.That(after.Single().Summary, Is.EqualTo("新增 2 個項目，已建立 WEB-38、WEB-39"));
        }
    }

    [Test]
    public async Task List_SkipsProposalsThatCanNoLongerBeRead()
    {
        // Arrange：一份是別人的（讀不到）、一份已經被清掉。
        var conversation = await H.NewConversationAsync();
        var mine = AddItemProposal(Me.PersonId);
        var someonesElse = AddItemProposal(Guid.NewGuid());
        await SeedReplyAsync(
            conversation.Id,
            1,
            Tool("propose_item_changes") with { ItemProposalId = someonesElse.Id },
            Tool("propose_item_changes") with { ItemProposalId = Guid.NewGuid() },
            Tool("propose_item_changes") with { ItemProposalId = mine.Id });

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(entries.Select(e => e.Reference), Is.EqualTo(new[] { mine.Id.ToString() }));
    }

    [Test]
    public async Task List_ChangeSetsInAProjectThatIsNoLongerVisible_AreNotListed()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Projects.OnList = _ => [];
        H.Changes.OnGet = (_, number) => WorkSamples.ProposedDetail(number);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_changeset") with { ChangeSetNumber = 12 });

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public async Task List_OfSomeoneElsesConversation_IsEmpty()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });

        // Act
        var entries = await Ledger.ListAsync(H.Admin, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public async Task List_WithManyProposals_KeepsTheNewestOnesAndReadsEachOnce()
    {
        // Arrange：25 份，每份一則回覆。
        var conversation = await H.NewConversationAsync();
        var ids = new List<Guid>();
        for (var i = 1; i <= 25; i++)
        {
            var proposal = AddItemProposal(Me.PersonId, itemKey: $"WEB-{i}");
            ids.Add(proposal.Id);
            await SeedReplyAsync(conversation.Id, i, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });
        }

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries, Has.Count.EqualTo(AiRules.MaxProposalsInLedger));
            Assert.That(entries.Select(e => e.Reference), Is.EqualTo(ids.Skip(5).Select(id => id.ToString())));
            Assert.That(Proposals.Reads, Has.Count.EqualTo(AiRules.MaxProposalsInLedger));
        }
    }

    [Test]
    public async Task List_TheSameProposalInTwoToolParts_AppearsOnce()
    {
        // Arrange：提出類的工具同樣的參數會回同一份。
        var conversation = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });
        await SeedReplyAsync(conversation.Id, 2, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(entries, Has.Count.EqualTo(1));
    }

    // 建議自己記著對話編號（MCP 與內建助理提出時填的），但回覆還在進行、工具段落還沒存下。
    private async Task<ItemProposal> AddRowOnlyProposalAsync(Guid? conversationId, Guid? personId = null, DateTimeOffset? createdAt = null)
    {
        var proposal = AddItemProposal(personId ?? Me.PersonId, createdAt: createdAt);
        await using var db = await H.Host.CreateDbContextAsync();
        db.ItemProposals.Add(new Persistence.ItemProposalRow
        {
            Id = proposal.Id,
            OrganizationId = H.OrganizationId,
            PersonId = personId ?? Me.PersonId,
            ConversationId = conversationId,
            ProjectKey = "WEB",
            ItemKey = "WEB-12",
            ItemTitle = "t",
            Changes = "[]",
            Status = Persistence.ItemProposalRow.PendingStatus,
            CreatedAt = proposal.CreatedAt,
        });
        await db.SaveChangesAsync();
        return proposal;
    }

    [Test]
    public async Task List_AProposalThatRemembersTheConversationButHasNoToolPartYet_IsListedOnce()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var inTools = AddItemProposal(Me.PersonId, createdAt: H.Clock.GetUtcNow().AddMinutes(-5));
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = inTools.Id });
        var rowOnly = await AddRowOnlyProposalAsync(conversation.Id);
        var other = await H.NewConversationAsync();
        await AddRowOnlyProposalAsync(other.Id);
        await AddRowOnlyProposalAsync(conversation.Id, personId: Guid.CreateVersion7());

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert：兩份，由舊到新；別的對話、別人的都不列。
        Assert.That(entries.Select(e => e.Reference), Is.EqualTo(new[] { inTools.Id.ToString(), rowOnly.Id.ToString() }));
    }

    [Test]
    public async Task Withdraw_AProposalJustMadeInTheSameReply_IsFoundByItsOwnConversationId()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = await AddRowOnlyProposalAsync(conversation.Id);

        // Act
        var result = await Ledger.WithdrawAsync(Me, conversation.Id, proposal.Id.ToString(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
            Assert.That(Proposals.Items[proposal.Id].Status, Is.EqualTo(ItemProposalStatus.Dismissed));
        }
    }

    [Test]
    public async Task Withdraw_AProposalOfAnotherConversationOrPerson_IsNotFoundEvenWithAConversationId()
    {
        // Arrange
        var mine = await H.NewConversationAsync();
        var other = await H.NewConversationAsync();
        var inOther = await AddRowOnlyProposalAsync(other.Id);
        var someoneElses = await AddRowOnlyProposalAsync(mine.Id, personId: Guid.CreateVersion7());

        // Act
        var first = await Ledger.WithdrawAsync(Me, mine.Id, inOther.Id.ToString(), CancellationToken.None);
        var second = await Ledger.WithdrawAsync(Me, mine.Id, someoneElses.Id.ToString(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalNotFound));
            Assert.That(second.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalNotFound));
            Assert.That(Proposals.Items[inOther.Id].Status, Is.EqualTo(ItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task Withdraw_APendingProposalOfThisConversation_DismissesIt()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });

        // Act
        var result = await Ledger.WithdrawAsync(Me, conversation.Id, proposal.Id.ToString(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value.State, Is.EqualTo(ProposalState.Dismissed));
            Assert.That(result.Value.Kind, Is.EqualTo(ProposalKind.ItemChanges));
            Assert.That(Proposals.Items[proposal.Id].Status, Is.EqualTo(ItemProposalStatus.Dismissed));
        }
    }

    [Test]
    public async Task Withdraw_AProposalFromAnotherConversation_IsNotFoundAndNotTouched()
    {
        // Arrange
        var mine = await H.NewConversationAsync();
        var other = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId);
        await SeedReplyAsync(other.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });

        // Act
        var result = await Ledger.WithdrawAsync(Me, mine.Id, proposal.Id.ToString(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalNotFound));
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(Proposals.Items[proposal.Id].Status, Is.EqualTo(ItemProposalStatus.Pending));
        }
    }

    [TestCase("")]
    [TestCase("不是代號")]
    [TestCase("changeset:abc")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task Withdraw_WithAnUnknownReference_IsNotFound(string reference)
    {
        // Arrange
        var conversation = await H.NewConversationAsync();

        // Act
        var result = await Ledger.WithdrawAsync(Me, conversation.Id, reference, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalNotFound));
    }

    [Test]
    public async Task Withdraw_AnAppliedProposal_IsClosed()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId, ItemProposalStatus.Applied);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });

        // Act
        var result = await Ledger.WithdrawAsync(Me, conversation.Id, proposal.Id.ToString(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalClosed));
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
        }
    }

    [Test]
    public async Task Withdraw_ANewItemProposalThatIsBeingCreated_IsClosed()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = NewItemsOf(2, [null, null], NewItemProposalStatus.Applying);
        Proposals.Add(Me.PersonId, proposal);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_new_items") with { NewItemProposalId = proposal.Id });

        // Act
        var result = await Ledger.WithdrawAsync(Me, conversation.Id, proposal.Id.ToString(), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalClosed));
    }

    [Test]
    public async Task Withdraw_AChangeSet_CannotBeWithdrawn()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_changeset") with { ChangeSetNumber = 12 });

        // Act
        var result = await Ledger.WithdrawAsync(Me, conversation.Id, "changeset:12", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalNotWithdrawable));
            Assert.That(result.Failure?.Message, Does.Contain("變更集"));
        }
    }

    [Test]
    public async Task Withdraw_InSomeoneElsesConversation_IsNotFound()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });

        // Act
        var result = await Ledger.WithdrawAsync(H.Admin, conversation.Id, proposal.Id.ToString(), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalNotFound));
    }
}
