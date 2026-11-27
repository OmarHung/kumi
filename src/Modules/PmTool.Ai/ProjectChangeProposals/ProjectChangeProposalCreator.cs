using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Ai.PlanningProposals;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.ProjectChangeProposals;

/// <summary>建立改專案的建議（<see cref="IProjectChangeProposalCreator"/>）。只存、不改專案。</summary>
internal sealed class ProjectChangeProposalCreator(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    IProjectDescriptionService descriptions,
    IProjectStaffingQueries staffing,
    IPeopleDirectory people,
    TimeProvider time) : IProjectChangeProposalCreator
{
    public async Task<Result<ProjectChangeProposal>> CreateAsync(Actor actor, ProjectChangeProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var project = await projects.GetAsync(actor, draft.ProjectKey, cancellationToken);
        if (project.Failure is { } notFound)
        {
            return notFound;
        }

        if (!project.Value.MyPermissions.Contains(WorkPermissions.ProjectManage))
        {
            return await ProjectForbidden.BuildAsync(
                staffing, people, actor, project.Value, "修改這個專案（名稱、封存、資訊頁）", cancellationToken);
        }

        var description = await descriptions.GetAsync(actor, project.Value.Project.Key, cancellationToken);
        if (description.Failure is { } descriptionFailure)
        {
            return descriptionFailure;
        }

        var before = new ProjectSnapshot(
            project.Value.Project.Name, project.Value.Project.ArchivedAt is not null, description.Value.Text, description.Value.UpdatedAt);
        var checkedChange = ProjectChangeChecker.Check(draft, before);
        var reason = ProposalReason.Check(draft.Reason);
        if (checkedChange.Failure is { } invalid)
        {
            return reason.Issue is { } extra ? invalid with { Details = [.. invalid.Details, extra] } : invalid;
        }

        if (reason.Issue is { } reasonIssue)
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "改專案的建議不合規定，沒有建立。") { Details = [reasonIssue] };
        }

        var change = checkedChange.Value;
        var row = new ProjectChangeProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = project.Value.Project.Key,
            Name = change.Name,
            Archived = change.Archived,
            ChangesDescription = change.ChangesDescription,
            Description = change.Description,
            Before = ProjectChangeMapper.WriteBefore(before),
            Reason = reason.Text,
            Status = ProjectChangeProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ProjectChangeProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ProjectChangeMapper.ToProposal(row);
    }
}
