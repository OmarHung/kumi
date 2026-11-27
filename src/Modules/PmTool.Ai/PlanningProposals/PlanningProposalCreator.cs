using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.PlanningProposals;

/// <summary>建立週期與里程碑建議（<see cref="IPlanningProposalCreator"/>）。只存、不改週期與里程碑。</summary>
internal sealed class PlanningProposalCreator(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    IPlanningService planning,
    IProjectStaffingQueries staffing,
    IPeopleDirectory people,
    TimeProvider time) : IPlanningProposalCreator
{
    public async Task<Result<PlanningProposal>> CreateAsync(Actor actor, PlanningProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var project = await projects.GetAsync(actor, draft.ProjectKey, cancellationToken);
        if (project.Failure is { } notFound)
        {
            return notFound;
        }

        if (await CheckAccessAsync(actor, project.Value, cancellationToken) is { } denied)
        {
            return denied;
        }

        var cycles = await planning.ListCyclesAsync(actor, project.Value.Project.Key, cancellationToken);
        if (cycles.Failure is { } cyclesFailure)
        {
            return cyclesFailure;
        }

        var milestones = await planning.ListMilestonesAsync(actor, project.Value.Project.Key, cancellationToken);
        if (milestones.Failure is { } milestonesFailure)
        {
            return milestonesFailure;
        }

        var checkedChanges = PlanningProposalChecker.Check(draft.Changes, cycles.Value, milestones.Value);
        var reason = ProposalReason.Check(draft.Reason);
        if (checkedChanges.Failure is { } invalid)
        {
            return reason.Issue is { } extra ? invalid with { Details = [.. invalid.Details, extra] } : invalid;
        }

        if (reason.Issue is { } reasonIssue)
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "週期與里程碑的建議不合規定，沒有建立。") { Details = [reasonIssue] };
        }

        var row = new PlanningProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            ProjectKey = project.Value.Project.Key,
            Changes = PlanningProposalMapper.WriteChanges(checkedChanges.Value),
            Reason = reason.Text,
            Status = PlanningProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.PlanningProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return PlanningProposalMapper.ToProposal(row, time.GetUtcNow());
    }

    // 規則和 PlanningService.CheckWriteAccessAsync 一樣：要能改項目，而且看得到全部（只看自己項目的人不行）。
    // 封存的專案只剩讀取與管理，item.update 不在，先說清楚是封存，不要讓人以為是自己沒權限。
    private async Task<Failure?> CheckAccessAsync(Actor actor, ProjectDetail project, CancellationToken cancellationToken)
    {
        if (project.Project.ArchivedAt is not null)
        {
            return new Failure(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, "這個專案已經封存，不能新增、修改或刪除週期與里程碑；要先取消封存。");
        }

        if (project.MyPermissions.Contains(WorkPermissions.ItemUpdate) && project.MyPermissions.Contains(WorkPermissions.ItemRead))
        {
            return null;
        }

        return await ProjectForbidden.BuildAsync(
            staffing, people, actor, project, "新增、修改或刪除這個專案的週期與里程碑（要能修改項目、而且看得到全部項目）", cancellationToken);
    }
}
