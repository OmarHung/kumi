using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Proposals;

/// <summary>
/// 一則對話裡助理提出過的建議與它們現在的狀態、撤回（<see cref="IAssistantProposalLedger"/>）。
/// 「這則對話的建議」以對話存下的工具段落為主（內建、本機、主機三條路都會記），再補上建議自己記著這則對話編號的（同一則回覆裡剛提出、段落還沒存下的）。
/// 狀態一律用發問的人的 <see cref="Actor"/> 向各服務讀：讀不到的（專案看不到了、被清掉了）不列。
/// </summary>
internal sealed class AssistantProposalLedger(
    IDbContextFactory<AiDbContext> contexts,
    ProposalServices services,
    IProjectService projects) : IAssistantProposalLedger
{
    // 找建議時從最新的助理訊息往前讀：一次讀多少則、最多讀幾則（沒有建議的長對話不會讀到底）。
    private const int MessageBatchSize = 40;

    private const int MaxMessagesScanned = 200;

    public async Task<IReadOnlyList<ProposalStatusEntry>> ListAsync(Actor actor, Guid conversationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var conversation = await FindConversationAsync(actor, conversationId, cancellationToken);
        if (conversation is null)
        {
            return [];
        }

        // 工具段落的建議（照提出的先後）之後，接上只有建議自己記著這則對話的（同一則回覆裡剛提出、段落還沒存下的，一定是最新的）。
        var references = (await ReadReferencesAsync(actor, conversationId, cancellationToken))
            .TakeLast(AiRules.MaxProposalsInLedger)
            .Concat(await ReadConversationReferencesAsync(actor, conversationId, null, cancellationToken))
            .DistinctBy(r => (r.Id, r.Number))
            .ToList();
        var projectKey = references.Any(r => r.Kind == ProposalKind.ChangeSet)
            ? await ProjectKeyOfAsync(actor, conversation, cancellationToken)
            : null;

        var entries = new List<ProposalStatusEntry>(references.Count);
        foreach (var reference in references)
        {
            var read = await services.GetAsync(actor, reference, projectKey, cancellationToken);
            if (read.Succeeded)
            {
                entries.Add(read.Value);
            }
        }

        return [.. entries.TakeLast(AiRules.MaxProposalsInLedger)];
    }

    public async Task<Result<ProposalStatusEntry>> WithdrawAsync(
        Actor actor, Guid conversationId, string reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (ProposalReference.Parse(reference) is not { } parsed
            || await FindConversationAsync(actor, conversationId, cancellationToken) is null)
        {
            return ProposalFailures.NotFound();
        }

        var found = (await ReadReferencesAsync(actor, conversationId, cancellationToken))
                .FirstOrDefault(r => r.Id == parsed.Id && r.Number == parsed.Number)
            // 同一則回覆裡剛提出的：工具段落要等回覆結束才存，建議自己記的對話編號先頂著用（同一個人、同一則對話才算）。
            ?? (parsed.Id is { } id
                ? (await ReadConversationReferencesAsync(actor, conversationId, id, cancellationToken)).FirstOrDefault()
                : null);
        if (found is null)
        {
            return ProposalFailures.NotFound();
        }

        return found.Kind == ProposalKind.ChangeSet
            ? ProposalFailures.NotWithdrawable()
            : await services.DismissAsync(actor, found, cancellationToken);
    }

    private async Task<AiConversationRow?> FindConversationAsync(Actor actor, Guid conversationId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Conversations.AsNoTracking().SingleOrDefaultAsync(
            c => c.Id == conversationId && c.OrganizationId == actor.OrganizationId && c.PersonId == actor.PersonId,
            cancellationToken);
    }

    // 同一份建議可能出現在好幾個工具段落（模型重複呼叫同樣參數的提出類工具）；每份只留第一次，順序照提出的先後。
    // 只讀助理的訊息、從最新的往前分批讀，找夠 MaxProposalsInLedger 份或讀完就停：不把整則對話的段落都讀進記憶體。
    private async Task<IReadOnlyList<ProposalReference>> ReadReferencesAsync(Actor actor, Guid conversationId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var batches = new List<IReadOnlyList<ProposalReference>>();
        var seen = new HashSet<(Guid? Id, long? Number)>();
        var scanned = 0;
        int? before = null;
        while (seen.Count < AiRules.MaxProposalsInLedger && scanned < MaxMessagesScanned)
        {
            var page = await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.OrganizationId == actor.OrganizationId
                    && m.Role == AssistantMessageMapper.AssistantRoleName && (before == null || m.Sequence < before))
                .OrderByDescending(m => m.Sequence)
                .Take(MessageBatchSize)
                .Select(m => new { m.Sequence, m.Parts })
                .ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var message in page)
            {
                var found = AssistantMessageMapper.ReadParts(message.Parts)
                    .OfType<AssistantToolPart>()
                    .SelectMany(ProposalReference.From)
                    .ToList();
                found.ForEach(r => seen.Add((r.Id, r.Number)));
                batches.Add(found);
            }

            scanned += page.Count;
            before = page[^1].Sequence;
        }

        // batches 是由新到舊的訊息；翻回由舊到新，每份建議留第一次出現的位置。
        return [.. Enumerable.Reverse(batches).SelectMany(b => b).DistinctBy(r => (r.Id, r.Number))];
    }

    // 建議自己記著對話編號的（內建助理、經 MCP 提出的都記）；onlyId 有值時只找那一份（撤回用）。同一個人、同一個組織才算。
    private async Task<IReadOnlyList<ProposalReference>> ReadConversationReferencesAsync(
        Actor actor, Guid conversationId, Guid? onlyId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var org = actor.OrganizationId;
        var person = actor.PersonId;
        var rows = await db.ItemProposals.AsNoTracking()
            .Where(p => p.ConversationId == conversationId && p.OrganizationId == org && p.PersonId == person)
            .Select(p => new Found { Kind = ProposalKind.ItemChanges, Id = p.Id, CreatedAt = p.CreatedAt })
            .Concat(db.ItemBatchProposals.AsNoTracking()
                .Where(p => p.ConversationId == conversationId && p.OrganizationId == org && p.PersonId == person)
                .Select(p => new Found { Kind = ProposalKind.BulkItemChanges, Id = p.Id, CreatedAt = p.CreatedAt }))
            .Concat(db.NewItemProposals.AsNoTracking()
                .Where(p => p.ConversationId == conversationId && p.OrganizationId == org && p.PersonId == person)
                .Select(p => new Found { Kind = ProposalKind.NewItems, Id = p.Id, CreatedAt = p.CreatedAt }))
            .Concat(db.ItemDeletionProposals.AsNoTracking()
                .Where(p => p.ConversationId == conversationId && p.OrganizationId == org && p.PersonId == person)
                .Select(p => new Found { Kind = ProposalKind.ItemDeletions, Id = p.Id, CreatedAt = p.CreatedAt }))
            .Concat(db.MemberProposals.AsNoTracking()
                .Where(p => p.ConversationId == conversationId && p.OrganizationId == org && p.PersonId == person)
                .Select(p => new Found { Kind = ProposalKind.Members, Id = p.Id, CreatedAt = p.CreatedAt }))
            .Concat(db.ProjectProposals.AsNoTracking()
                .Where(p => p.ConversationId == conversationId && p.OrganizationId == org && p.PersonId == person)
                .Select(p => new Found { Kind = ProposalKind.Project, Id = p.Id, CreatedAt = p.CreatedAt }))
            .Where(f => onlyId == null || f.Id == onlyId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(AiRules.MaxProposalsInLedger)
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(f => f.CreatedAt).Select(f => new ProposalReference(f.Kind, f.Id, null))];
    }

    private sealed class Found
    {
        public ProposalKind Kind { get; init; }

        public Guid Id { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }

    // 變更集的編號是專案裡的號碼，要靠對話的專案編號換回代號；專案已經看不到時變更集也一併不列。
    private async Task<string?> ProjectKeyOfAsync(Actor actor, AiConversationRow conversation, CancellationToken cancellationToken)
    {
        if (conversation.ProjectId is not { } projectId)
        {
            return null;
        }

        var visible = await projects.ListAsync(actor, cancellationToken);
        return visible.FirstOrDefault(p => p.Id == projectId)?.Key;
    }
}
