using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Access;
using PmTool.Ai.Persistence;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.MemberProposals;

/// <summary>建立成員建議（<see cref="IMemberProposalCreator"/>）。只存、不改成員。</summary>
internal sealed class MemberProposalCreator(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    IProjectStaffingQueries staffing,
    IPeopleDirectory people,
    TimeProvider time) : IMemberProposalCreator
{
    public async Task<Result<MemberProposal>> CreateAsync(Actor actor, MemberProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var project = await projects.GetAsync(actor, draft.ProjectKey, cancellationToken);
        if (project.Failure is { } notFound)
        {
            return notFound;
        }

        var roles = await staffing.ListRolesAsync(actor, project.Value.Project.Key, cancellationToken);
        if (roles.Failure is { } rolesFailure)
        {
            return rolesFailure;
        }

        if (!project.Value.MyPermissions.Contains(WorkPermissions.ProjectManage))
        {
            var managers = await MemberManagers.ListAsync(people, actor.OrganizationId, project.Value.Members, roles.Value, cancellationToken);
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, MemberManagers.ForbiddenMessage(managers));
        }

        var changes = draft.Changes ?? [];
        var ids = changes.Where(c => c is not null).Select(c => c.PersonId).Distinct().ToList();
        var found = await people.FindManyAsync(actor.OrganizationId, ids, cancellationToken);
        var checkedChanges = MemberProposalChecker.Check(changes, project.Value.Members, roles.Value, found);
        if (checkedChanges.Failure is { } invalid)
        {
            return invalid;
        }

        var row = new MemberProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = project.Value.Project.Key,
            Changes = MemberProposalMapper.WriteChanges(checkedChanges.Value),
            Status = MemberProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.MemberProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return MemberProposalMapper.ToProposal(row);
    }
}
