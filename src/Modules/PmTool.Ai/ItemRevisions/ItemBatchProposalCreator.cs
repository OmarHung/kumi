using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Items;
using PmTool.Ai.Persistence;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.ItemRevisions;

/// <summary>
/// 建立批次修改建議（<see cref="IItemBatchProposalCreator"/>）。只存、不改項目。每一筆的檢查就是單一項目修改建議的
/// <see cref="ItemChangeChecker"/>，只是細項的路徑前面多了 <c>items[n].</c>。
/// </summary>
internal sealed class ItemBatchProposalCreator(
    IDbContextFactory<AiDbContext> contexts,
    IWorkItemService items,
    IProjectService projects,
    IPlanningService planning,
    IItemActivityService activity,
    IPeopleDirectory people,
    TimeProvider time) : IItemBatchProposalCreator
{
    private readonly ItemChangeFactsLoader _facts = new(items, planning, activity, people);

    public async Task<Result<ItemBatchProposal>> CreateAsync(Actor actor, ItemBatchProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var project = await projects.GetAsync(actor, draft.ProjectKey, cancellationToken);
        if (project.Failure is { } notFound)
        {
            return notFound;
        }

        var entries = draft.Items ?? [];
        if (entries.Count is 0 or > AiRules.MaxItemsPerBatchProposal)
        {
            return Invalid([Issue("items", $"一份批次修改建議要有 1 到 {AiRules.MaxItemsPerBatchProposal} 個項目。")]);
        }

        var permission = ItemChangePermissions.Missing(
            entries.Where(e => e is not null).SelectMany(e => e.Changes ?? []).Where(c => c is not null),
            string.Join(' ', entries.Where(e => e is not null).Select(e => e.Comment)),
            project.Value.MyPermissions);
        if (permission is not null)
        {
            return permission;
        }

        var issues = new List<FailureDetail>();
        var stored = new List<ItemBatchMapper.StoredEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            var one = await CheckEntryAsync(actor, project.Value, entries[i], i, seen, issues, cancellationToken);
            if (one is not null)
            {
                stored.Add(one);
            }
        }

        if (issues.Count > 0)
        {
            return Invalid(issues);
        }

        var row = new ItemBatchProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = project.Value.Project.Key,
            Items = ItemBatchMapper.Write(stored),
            Status = ItemBatchProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ItemBatchProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ItemBatchMapper.ToProposal(row);
    }

    private async Task<ItemBatchMapper.StoredEntry?> CheckEntryAsync(
        Actor actor,
        ProjectDetail project,
        ItemBatchEntryDraft? entry,
        int index,
        HashSet<string> seen,
        List<FailureDetail> issues,
        CancellationToken cancellationToken)
    {
        var path = $"items[{index}]";
        if (entry is null)
        {
            issues.Add(Issue(path, "這一筆是空的。"));
            return null;
        }

        var found = await ItemLookup.FindAsync(items, actor, project.Project.Key, entry.ItemKey, cancellationToken);
        if (found.Failure is { } notFound)
        {
            issues.Add(Issue($"{path}.itemKey", notFound.Message));
            return null;
        }

        var item = found.Value;
        if (!seen.Add(item.Key))
        {
            issues.Add(Issue($"{path}.itemKey", $"項目 {item.Key} 出現了不只一次，同一個項目的修改請合併成一筆。"));
            return null;
        }

        var facts = await _facts.LoadAsync(actor, item, entry.Changes ?? [], entry.Comment, entry.ReplyTo, cancellationToken);
        var changes = ItemChangeChecker.Check(
            entry.Changes ?? [], entry.Comment, entry.ExpectedDescriptionRevision, item, project, facts, entry.ReplyTo);
        if (changes.Failure is { } invalid)
        {
            issues.AddRange(invalid.Details.Select(d => d with { Path = $"{path}.{d.Path}" }));
            return null;
        }

        var revision = changes.Value.Any(c => c.Field == ItemFieldNames.Description) ? entry.ExpectedDescriptionRevision!.Trim() : null;
        return ItemBatchMapper.Store(
            item.Key, AiText.Cut(item.Title, ItemProposalLimits.MaxItemTitleLength), changes.Value, ItemChangeChecker.NormalizeComment(entry.Comment), revision, facts.Reply);
    }

    private static Failure Invalid(IReadOnlyList<FailureDetail> issues) =>
        new(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "批次修改建議不合規定，沒有建立。") { Details = issues };

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);
}
