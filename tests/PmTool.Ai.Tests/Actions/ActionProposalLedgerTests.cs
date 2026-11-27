using PmTool.Ai.Proposals;

namespace PmTool.Ai.Tests.Actions;

/// <summary>帳本：這則對話裡的動作建議列得出現況、撤得掉；步驟的文字不進給模型的附註。</summary>
[TestFixture]
public sealed class ActionProposalLedgerTests : ActionProposalTestBase
{
    private IAssistantProposalLedger Ledger => H.Host.Get<IAssistantProposalLedger>();

    [Test]
    public async Task List_ShowsActionProposalsOfTheConversationWithoutStepText()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = await CreatedAsync(Draft() with { ConversationId = conversation.Id });

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        var entry = entries.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Reference, Does.Contain(proposal.Id.ToString()));
            Assert.That(entry.State, Is.EqualTo(ProposalState.Pending));
            Assert.That(entry.Summary, Does.Contain("動作").And.Contain("3 步"));
            Assert.That(entry.Summary, Does.Not.Contain("HR"));
        }
    }

    [Test]
    public async Task List_AfterApply_ShowsApplied()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = await CreatedAsync(Draft() with { ConversationId = conversation.Id });
        await Proposals.ApplyAsync(Me, proposal.Id, NoInputs, CancellationToken.None);

        // Act
        var entries = await Ledger.ListAsync(Me, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(entries.Single().State, Is.EqualTo(ProposalState.Applied));
    }

    [Test]
    public async Task Withdraw_PendingActionProposal_DismissesIt()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = await CreatedAsync(Draft() with { ConversationId = conversation.Id });

        // Act
        var withdrawn = await Ledger.WithdrawAsync(Me, conversation.Id, proposal.Id.ToString(), CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withdrawn.Succeeded, Is.True, () => withdrawn.Failure?.Message ?? string.Empty);
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Dismissed));
        }
    }

    [Test]
    public async Task List_DoesNotShowProposalsOfOtherPeople()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        await CreatedAsync(Draft() with { ConversationId = conversation.Id });

        // Act
        var entries = await Ledger.ListAsync(H.Admin, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(entries, Is.Empty);
    }
}
