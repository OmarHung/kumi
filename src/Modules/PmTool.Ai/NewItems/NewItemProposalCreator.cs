using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Items;
using PmTool.Ai.Persistence;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.NewItems;

/// <summary>建立新增項目的建議（<see cref="INewItemProposalCreator"/>）。只存、不建立項目。</summary>
internal sealed class NewItemProposalCreator(
    IDbContextFactory<AiDbContext> contexts, IWorkItemService items, IProjectService projects, IPeopleDirectory people, TimeProvider time)
    : INewItemProposalCreator
{
    public async Task<Result<NewItemProposal>> CreateAsync(Actor actor, NewItemProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var project = await projects.GetAsync(actor, draft.ProjectKey, cancellationToken);
        if (project.Failure is { } notFound)
        {
            return notFound;
        }

        // 每一個項目建立時都會以使用者的權限檢查；先擋在這裡，使用者只能讀的時候不必看到一張永遠建立不了的卡片。
        if (!project.Value.MyPermissions.Contains(WorkPermissions.ItemCreate))
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "你沒有建立項目的權限。");
        }

        var drafts = draft.Items ?? [];
        var parents = await FindParentsAsync(actor, project.Value.Project.Key, drafts, cancellationToken);
        var membersText = NewItemChecker.HasUnknownAssignee(drafts, project.Value)
            ? await NewItemMembers.DescribeAsync(people, actor.OrganizationId, project.Value.Members, cancellationToken)
            : null;
        var checkedItems = NewItemChecker.Check(drafts, project.Value, parents, membersText);
        if (checkedItems.Failure is { } invalid)
        {
            return invalid;
        }

        var row = new NewItemProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = project.Value.Project.Key,
            Items = NewItemMapper.WriteItems(checkedItems.Value),
            CreatedKeys = NewItemMapper.WriteKeys([.. checkedItems.Value.Select(_ => (string?)null)]),
            Status = NewItemProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.NewItemProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return NewItemMapper.ToProposal(row, time.GetUtcNow());
    }

    // 找不到的不放進結果，檢查時當成「找不到上層項目」；失敗一律是同一種（看不到、不在這個專案、編號寫錯沒有差別）。
    private async Task<IReadOnlyDictionary<string, WorkItem>> FindParentsAsync(
        Actor actor, string projectKey, IReadOnlyList<NewItemDraft> drafts, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, WorkItem>(StringComparer.OrdinalIgnoreCase);
        var keys = drafts
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.ParentKey))
            .Select(d => d.ParentKey!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(AiRules.MaxNewItemsPerProposal);
        foreach (var key in keys)
        {
            var item = await ItemLookup.FindAsync(items, actor, projectKey, key, cancellationToken);
            if (item.Failure is null)
            {
                found[key] = item.Value;
            }
        }

        return found;
    }
}
