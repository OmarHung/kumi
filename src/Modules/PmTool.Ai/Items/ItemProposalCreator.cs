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

    // 建議的每一步套用時都會以使用者的權限檢查；先擋在這裡，使用者只能讀的時候不必看到一張永遠套用不了的卡片。
    private static Failure? MissingPermission(ItemProposalDraft draft, ProjectDetail project)
    {
        var permissions = project.MyPermissions;
        var changes = draft.Changes ?? [];
        var denied = (changes.Any(c => c.Field != ItemFieldNames.Status) && !permissions.Contains(WorkPermissions.ItemUpdate))
            || (changes.Any(c => c.Field == ItemFieldNames.Status) && !permissions.Contains(WorkPermissions.ItemTransition))
            || (!string.IsNullOrWhiteSpace(draft.Comment) && !permissions.Contains(WorkPermissions.CommentCreate));
        return denied
            ? new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "你沒有修改項目的權限，只能讀。")
            : null;
    }
}
