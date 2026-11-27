using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Items;
using PmTool.Ai.Persistence;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.CommentProposals;

/// <summary>建立留言建議（<see cref="ICommentProposalCreator"/>）。只存、不寫入；每一筆的問題一次回完。</summary>
internal sealed class CommentProposalCreator(
    IDbContextFactory<AiDbContext> contexts,
    IWorkItemService items,
    IItemActivityService activity,
    IProjectService projects,
    IPeopleDirectory people,
    TimeProvider time) : ICommentProposalCreator
{
    public async Task<Result<CommentProposal>> CreateAsync(Actor actor, CommentProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var project = await projects.GetAsync(actor, draft.ProjectKey, cancellationToken);
        if (project.Failure is { } notFound)
        {
            return notFound;
        }

        if (project.Value.Project.ArchivedAt is not null)
        {
            return new Failure(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, "專案已經封存，不能改留言。");
        }

        var changes = draft.Changes ?? [];
        if (changes.Count is 0 or > AiRules.MaxCommentChangesPerProposal)
        {
            return Invalid([Issue("changes", $"一份留言建議要有 1 到 {AiRules.MaxCommentChangesPerProposal} 筆。")]);
        }

        var issues = new List<FailureDetail>();
        var found = await ResolveAsync(actor, project.Value, changes, issues, cancellationToken);
        if (issues.Count > 0)
        {
            return Invalid(issues);
        }

        var names = await people.FindManyAsync(actor.OrganizationId, [.. found.Select(f => f.Comment.ActorId).Distinct()], cancellationToken);
        var stored = found.Select(f => ToChange(f.Draft, f.Comment, names)).ToList();
        var row = new CommentProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = project.Value.Project.Key,
            Changes = CommentProposalMapper.WriteChanges(stored),
            Status = CommentProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.CommentProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return CommentProposalMapper.ToProposal(row);
    }

    private async Task<List<(CommentChangeDraft Draft, ItemActivity Comment)>> ResolveAsync(
        Actor actor, ProjectDetail project, IReadOnlyList<CommentChangeDraft> changes, List<FailureDetail> issues, CancellationToken cancellationToken)
    {
        var canComment = project.MyPermissions.Contains(WorkPermissions.CommentCreate);
        var canManage = project.MyPermissions.Contains(WorkPermissions.ProjectManage);
        var loaded = new Dictionary<string, IReadOnlyDictionary<Guid, ItemActivity>?>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<Guid>();
        var found = new List<(CommentChangeDraft, ItemActivity)>();
        for (var i = 0; i < changes.Count; i++)
        {
            var path = $"changes[{i}].commentId";
            var change = changes[i];
            var item = await ItemLookup.FindAsync(items, actor, project.Project.Key, change.ItemKey, cancellationToken);
            if (item.Failure is { } missing)
            {
                issues.Add(Issue($"changes[{i}].itemKey", missing.Message));
                continue;
            }

            if (!loaded.TryGetValue(item.Value.Key, out var comments))
            {
                var read = await CommentLookup.LoadAsync(activity, actor, item.Value.Key, cancellationToken);
                comments = read.Failure is null ? read.Value : null;
                loaded[item.Value.Key] = comments;
            }

            if (comments is null || !comments.TryGetValue(change.CommentId, out var comment))
            {
                issues.Add(Issue(path, $"在 {item.Value.Key} 找不到這則留言（commentId 從 get_item 的活動或 list_comments 拿）。"));
            }
            else if (!seen.Add(comment.Id))
            {
                issues.Add(Issue(path, "同一則留言在這份建議裡出現了不只一次。"));
            }
            else if (CommentRules.Check(change, comment, actor.PersonId, canComment, canManage) is { } problem)
            {
                issues.Add(Issue(path, problem));
            }
            else
            {
                found.Add((change with { ItemKey = item.Value.Key }, comment));
            }
        }

        return found;
    }

    private static CommentChange ToChange(CommentChangeDraft draft, ItemActivity comment, IReadOnlyDictionary<Guid, PersonSummary> names) => new(
        draft.ItemKey,
        comment.Id,
        draft.Action,
        names.TryGetValue(comment.ActorId, out var author) ? AiText.Cut(author.DisplayName, ItemProposalLimits.MaxItemTitleLength) : "（已離開的人）",
        comment.At,
        comment.DeletedAt is null && comment.Comment is { } text ? AiText.Cut(text, AiRules.MaxCommentExcerptLength) : null,
        draft.Action == CommentAction.Edit ? CommentRules.Normalize(draft.Body!) : null,
        null);

    private static Failure Invalid(IReadOnlyList<FailureDetail> issues) =>
        new(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "留言建議不合規定，沒有建立。") { Details = issues };

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);
}
