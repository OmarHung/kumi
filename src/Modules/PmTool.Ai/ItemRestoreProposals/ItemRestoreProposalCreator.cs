using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Items;
using PmTool.Ai.ItemRevisions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ItemRestoreProposals;

/// <summary>建立復原建議（<see cref="IItemRestoreProposalCreator"/>）。只存、不復原；每一個編號的問題一次回完。</summary>
internal sealed class ItemRestoreProposalCreator(
    IDbContextFactory<AiDbContext> contexts, IWorkItemService items, IProjectService projects, TimeProvider time) : IItemRestoreProposalCreator
{
    public async Task<Result<ItemRestoreProposal>> CreateAsync(Actor actor, ItemRestoreProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var project = await projects.GetAsync(actor, draft.ProjectKey, cancellationToken);
        if (project.Failure is { } notFound)
        {
            return notFound;
        }

        // 套用時工作引擎照樣檢查；先擋在這裡，使用者沒有權限時不必看到一張永遠復原不了的卡片。
        if (!project.Value.MyPermissions.Contains(WorkPermissions.ItemDelete))
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "你沒有復原項目的權限。");
        }

        var keys = draft.ItemKeys ?? [];
        var issues = new List<FailureDetail>();
        if (keys.Count is 0 or > AiRules.MaxItemsPerDeletionProposal)
        {
            issues.Add(Issue("itemKeys", $"一份復原建議要有 1 到 {AiRules.MaxItemsPerDeletionProposal} 個項目。"));
        }

        var reason = CheckReason(draft.Reason, issues);
        var targets = new List<DeletedItemSummary>();
        if (keys.Count is > 0 and <= AiRules.MaxItemsPerDeletionProposal)
        {
            var deleted = await ScanDeletedAsync(actor, project.Value.Project.Key, cancellationToken);
            if (deleted.Failure is { } scanFailure)
            {
                return scanFailure;
            }

            Resolve(keys, deleted.Value, targets, issues);
        }

        if (issues.Count > 0)
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "復原建議不合規定，沒有建立。") { Details = issues };
        }

        var row = new ItemRestoreProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = project.Value.Project.Key,
            Items = ItemRestoreMapper.WriteItems(
                targets.Select(t => new ItemRestoreTarget(t.Key, AiText.Cut(t.Title, ItemProposalLimits.MaxItemTitleLength), t.DeletedAt))),
            Reason = reason,
            Status = ItemRestoreProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ItemRestoreProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ItemRestoreMapper.ToProposal(row);
    }

    // 從最近刪除的往回翻；翻完整個上限，才能確認「上層也是刪除的」（上層可能刪得比下層早）。
    private async Task<Result<Dictionary<string, DeletedItemSummary>>> ScanDeletedAsync(
        Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        var deleted = new Dictionary<string, DeletedItemSummary>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;
        for (var page = 0; page < AiRules.MaxDeletedPagesScanned; page++)
        {
            var read = await items.ListDeletedAsync(
                actor, projectKey, new DeletedItemQuery { Limit = DeletedItemQuery.MaxLimit, Cursor = cursor }, cancellationToken);
            if (read.Failure is { } failure)
            {
                return failure;
            }

            foreach (var item in read.Value.Items)
            {
                deleted[item.Key] = item;
            }

            cursor = read.Value.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return deleted;
    }

    private static void Resolve(
        IReadOnlyList<string> keys, IReadOnlyDictionary<string, DeletedItemSummary> deleted, List<DeletedItemSummary> targets, List<FailureDetail> issues)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i]?.Trim() ?? string.Empty;
            if (key.Length is 0 or > ItemProposalLimits.MaxItemKeyLength || key.Any(char.IsControl))
            {
                issues.Add(Issue($"itemKeys[{i}]", "項目編號不合規定。"));
            }
            else if (!deleted.TryGetValue(key, out var found))
            {
                issues.Add(Issue($"itemKeys[{i}]", $"找不到已刪除的項目「{key}」（太久以前刪的請使用者到已刪除頁復原）。"));
            }
            else if (!selected.Add(found.Key))
            {
                issues.Add(Issue($"itemKeys[{i}]", $"項目 {found.Key} 出現了不只一次。"));
            }
            else
            {
                targets.Add(found);
            }
        }

        for (var i = 0; i < keys.Count; i++)
        {
            var target = targets.FirstOrDefault(t => string.Equals(t.Key, keys[i]?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (target?.ParentKey is { } parent && deleted.ContainsKey(parent) && !selected.Contains(parent))
            {
                issues.Add(Issue(
                    $"itemKeys[{i}]",
                    $"{target.Key} 的上層 {parent} 也是刪除的，又不在這份建議裡，沒辦法單獨復原。問使用者要不要把 {parent} 一起復原（放進 itemKeys），不要自己決定。"));
            }
        }
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
