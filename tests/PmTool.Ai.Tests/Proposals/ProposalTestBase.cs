using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Proposals;

/// <summary>
/// 建議現況與撤回測試的共同環境：助理的環境，加上六種建議的記憶體服務、刪除與批次修改的假建立者；專案清單有 WEB（變更集要靠它換回代號）。
/// 建議是「這則對話的」由對話存下的工具段落決定，所以測試把工具段落直接寫進助理的訊息。
/// </summary>
public abstract class ProposalTestBase
{
    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal FakeProposalServices Proposals { get; private set; } = null!;

    internal FakeRevisionCreators Creators { get; private set; } = null!;

    internal IAssistantProposalLedger Ledger => _h.Host.Get<IAssistantProposalLedger>();

    protected Actor Me => _h.Actor;

    [SetUp]
    public async Task StartAsync()
    {
        Proposals = new FakeProposalServices();
        Creators = new FakeRevisionCreators();
        _h = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton<IItemProposalService>(Proposals);
            services.AddSingleton<IItemBatchProposalService>(Proposals);
            services.AddSingleton<INewItemProposalService>(Proposals);
            services.AddSingleton<IItemDeletionProposalService>(Proposals);
            services.AddSingleton<IMemberProposalService>(Proposals);
            services.AddSingleton<IProjectProposalService>(Proposals);
            services.AddSingleton<IItemDeletionProposalCreator>(Creators);
            services.AddSingleton<IItemBatchProposalCreator>(Creators);
        });
        _h.Projects.OnList = _ => [new ProjectSummary(WorkSamples.ProjectId, AssistantHarness.ProjectKey, "網站", 3, "admin", DateTimeOffset.UnixEpoch)];
        Proposals.Now = () => _h.Clock.GetUtcNow();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    /// <summary>把一則助理的回覆（帶這些工具段落）直接寫進對話。</summary>
    protected async Task SeedReplyAsync(Guid conversationId, int sequence, params AssistantToolPart[] tools)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        db.Messages.Add(new Persistence.AiMessageRow
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            OrganizationId = _h.OrganizationId,
            Sequence = sequence,
            Role = AssistantMessageMapper.AssistantRoleName,
            Parts = AssistantMessageMapper.WriteParts(tools),
            Transcript = "[]",
            Status = "complete",
            CreatedAt = _h.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();

        // 之後送出的訊息接在後面：對話的訊息數要跟上，不然序號會撞上。
        await db.Conversations.Where(c => c.Id == conversationId).ExecuteUpdateAsync(set => set.SetProperty(c => c.MessageCount, sequence));
    }

    protected static AssistantToolPart Tool(string name) => new("c1", name, AssistantToolStatus.Done, null, null);

    protected ItemProposal AddItemProposal(Guid ownerPersonId, ItemProposalStatus status = ItemProposalStatus.Pending, string itemKey = "WEB-12", DateTimeOffset? createdAt = null)
    {
        var proposal = new ItemProposal(
            Guid.CreateVersion7(), "WEB", itemKey, "秘密標題", [new ItemFieldChange("dueDate", default), new ItemFieldChange("priority", default)], null, status, createdAt ?? _h.Clock.GetUtcNow());
        Proposals.Add(ownerPersonId, proposal);
        return proposal;
    }

    protected static NewItemProposal NewItemsOf(int count, IReadOnlyList<string?> created, NewItemProposalStatus status) => new(
        Guid.CreateVersion7(),
        "WEB",
        [.. Enumerable.Range(0, count).Select(i => new NewItemDraft("task", $"秘密任務 {i}"))],
        created,
        status,
        DateTimeOffset.UnixEpoch);
}
