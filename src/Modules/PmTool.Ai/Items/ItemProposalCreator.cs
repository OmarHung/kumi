using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>建立修改建議（<see cref="IItemProposalCreator"/>）。只存、不改項目。</summary>
internal sealed class ItemProposalCreator(
    IDbContextFactory<AiDbContext> contexts, IWorkItemService items, IProjectService projects, TimeProvider time) : IItemProposalCreator
{
    public async Task<Result<ItemProposal>> CreateAsync(Actor actor, ItemProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var found = await ItemLookup.FindAsync(items, actor, draft.ProjectKey, draft.ItemKey, cancellationToken);
        if (found.Failure is { } notFound)
        {
            return notFound;
        }

        var item = found.Value;
        var project = await projects.GetAsync(actor, item.ProjectKey, cancellationToken);
        if (project.Failure is not null)
        {
            return ItemLookup.NotFound(item.Key);
        }

        var permission = MissingPermission(draft, project.Value);
        if (permission is not null)
        {
            return permission;
        }

        var changes = ItemChangeChecker.Check(draft.Changes ?? [], draft.Comment, draft.DescriptionRevision, item, project.Value);
        if (changes.Failure is { } invalid)
        {
            return invalid;
        }

        var row = new ItemProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = item.ProjectKey,
            ItemKey = item.Key,
            ItemTitle = item.Title,
            Changes = ItemProposalMapper.WriteChanges(changes.Value),
            Comment = ItemChangeChecker.NormalizeComment(draft.Comment),
            DescriptionRevision = changes.Value.Any(c => c.Field == ItemFieldNames.Description) ? draft.DescriptionRevision!.Trim() : null,
            Status = ItemProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ItemProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ItemProposalMapper.ToProposal(row);
    }

    private static Failure? MissingPermission(ItemProposalDraft draft, ProjectDetail project) =>
        ItemChangePermissions.Missing(draft.Changes ?? [], draft.Comment, project.MyPermissions);
}
