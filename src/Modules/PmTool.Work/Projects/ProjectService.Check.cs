using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Common;

namespace PmTool.Work.Projects;

internal sealed partial class ProjectService
{
    public async Task<Result<Unit>> CheckNewProjectAsync(Actor actor, NewProject project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(project);
        if (!actor.IsOrganizationAdmin)
        {
            return WorkFailures.Forbidden("只有組織管理員可以開新專案。");
        }

        var checkedProject = NewProjectRules.Check(project, templates);
        if (checkedProject.Failure is { } failure)
        {
            return failure;
        }

        // 封存的專案也佔著代號（CreateAsync 靠唯一索引擋，這裡先查；中間被搶走時 CreateAsync 照樣回 key_taken）。
        var key = checkedProject.Value.Key;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var taken = await db.Projects.AnyAsync(p => p.OrganizationId == actor.OrganizationId && p.Key == key, cancellationToken);
        return taken ? WorkFailures.ProjectKeyTaken(key) : Unit.Value;
    }
}
