using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Items;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ItemRevisions;

/// <summary>建立刪除建議（<see cref="IItemDeletionProposalCreator"/>）。只存、不刪除。</summary>
internal sealed class ItemDeletionProposalCreator(
    IDbContextFactory<AiDbContext> contexts, IWorkItemService items, IProjectService projects, TimeProvider time) : IItemDeletionProposalCreator
{
    public async Task<Result<ItemDeletionProposal>> CreateAsync(Actor actor, ItemDeletionProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var project = await projects.GetAsync(actor, draft.ProjectKey, cancellationToken);
        if (project.Failure is { } notFound)
        {
            return notFound;
        }

        // 套用時工作引擎照樣檢查；先擋在這裡，使用者沒有權限時不必看到一張永遠刪不了的卡片。
        if (!project.Value.MyPermissions.Contains(WorkPermissions.ItemDelete))
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "你沒有刪除項目的權限。");
        }

        var keys = draft.ItemKeys ?? [];
        var issues = new List<FailureDetail>();
        if (keys.Count is 0 or > AiRules.MaxItemsPerDeletionProposal)
        {
            issues.Add(Issue("itemKeys", $"一份刪除建議要有 1 到 {AiRules.MaxItemsPerDeletionProposal} 個項目。"));
        }

        var reason = CheckReason(draft.Reason, issues);
        var targets = new List<WorkItem>();
        for (var i = 0; i < keys.Count && i < AiRules.MaxItemsPerDeletionProposal; i++)
        {
            var found = await ItemLookup.FindAsync(items, actor, project.Value.Project.Key, keys[i], cancellationToken);
            if (found.Failure is { } missing)
            {
                issues.Add(Issue($"itemKeys[{i}]", missing.Message));
            }
            else if (targets.Any(t => string.Equals(t.Key, found.Value.Key, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Issue($"itemKeys[{i}]", $"項目 {found.Value.Key} 出現了不只一次。"));
            }
            else
            {
                targets.Add(found.Value);
            }
        }

        if (issues.Count == 0)
        {
            await CheckChildrenAsync(actor, project.Value.Project.Key, targets, issues, cancellationToken);
        }

        if (issues.Count > 0)
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "刪除建議不合規定，沒有建立。") { Details = issues };
        }

        var row = new ItemDeletionProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = project.Value.Project.Key,
            Items = ItemDeletionMapper.WriteItems(targets.Select(t => new ItemDeletionTarget(t.Key, AiText.Cut(t.Title, ItemProposalLimits.MaxItemTitleLength)))),
            Reason = reason,
            Status = ItemDeletionProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ItemDeletionProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ItemDeletionMapper.ToProposal(row);
    }

    // 還有沒刪除、又不在這份裡的下層：工作引擎套用時會擋下，先在這裡把編號告訴模型，讓它決定要一起刪還是不刪。
    private async Task CheckChildrenAsync(
        Actor actor, string projectKey, IReadOnlyList<WorkItem> targets, List<FailureDetail> issues, CancellationToken cancellationToken)
    {
        var selected = targets.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < targets.Count; i++)
        {
            var page = await items.ListAsync(
                actor, projectKey, new WorkItemQuery { ParentKey = targets[i].Key, Limit = ItemRevisionLimits.ChildrenScanLimit }, cancellationToken);
            if (page.Failure is not null)
            {
                continue;
            }

            // 讀不完全部的下層就沒辦法確認都選進來了，不放行。
            if (page.Value.NextCursor is not null)
            {
                issues.Add(Issue($"itemKeys[{i}]", $"{targets[i].Key} 的下層太多（超過 {ItemRevisionLimits.ChildrenScanLimit} 個），沒辦法確認都在這份建議裡。請使用者在網頁上處理，或分批刪除。"));
                continue;
            }

            var left = page.Value.Items.Where(c => !selected.Contains(c.Key)).Select(c => c.Key).ToList();
            if (left.Count > 0)
            {
                issues.Add(Issue($"itemKeys[{i}]", ChildrenMessage(targets[i].Key, left)));
            }
        }
    }

    private static string ChildrenMessage(string key, IReadOnlyList<string> children)
    {
        var listed = string.Join("、", children.Take(ItemRevisionLimits.MaxChildrenListed));
        var tail = children.Count > ItemRevisionLimits.MaxChildrenListed ? "…等" : string.Empty;
        return $"{key} 還有沒刪除的下層：{listed}{tail}。要一起刪就把它們也放進 itemKeys，不刪的話就把 {key} 拿掉。";
    }

    private static string? CheckReason(string? reason, List<FailureDetail> issues)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > ItemRevisionLimits.MaxReasonLength)
        {
            issues.Add(Issue("reason", $"reason 最長 {ItemRevisionLimits.MaxReasonLength} 個字元。"));
        }
        else if (!ItemValueRules.IsPlainText(text, allowLineBreaks: false))
        {
            issues.Add(Issue("reason", "reason 不能含有換行、NUL 或其他控制字元。"));
        }

        return text;
    }

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);
}
