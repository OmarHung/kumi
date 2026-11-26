using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Access;
using PmTool.Ai.Items;
using PmTool.Ai.Persistence;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.NewItems;

/// <summary>建立新增項目的建議（<see cref="INewItemProposalCreator"/>）。只存、不建立項目。</summary>
internal sealed class NewItemProposalCreator(
    IDbContextFactory<AiDbContext> contexts,
    IWorkItemService items,
    IProjectService projects,
    IProjectStaffingQueries staffing,
    IPlanningService planning,
    IPeopleDirectory people,
    TimeProvider time) : INewItemProposalCreator
{
    private static readonly ProjectRoleInfo[] NoRoles = [];

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
        var canManage = project.Value.MyPermissions.Contains(WorkPermissions.ProjectManage);
        var additions = draft.AddMembers ?? [];
        var active = await NewItemMembers.ActiveAsync(people, actor.OrganizationId, project.Value.Members, cancellationToken);
        Result<IReadOnlyList<ProjectRoleInfo>> roles = NoRoles;
        if (additions.Count > 0 || NeedsManagersText(drafts, active, canManage))
        {
            roles = await staffing.ListRolesAsync(actor, project.Value.Project.Key, cancellationToken);
        }

        if (roles.Failure is { } rolesFailure)
        {
            return rolesFailure;
        }

        if (additions.Count > 0 && !canManage)
        {
            return await ForbiddenAsync(actor, project.Value, roles.Value, cancellationToken);
        }

        var parents = await FindParentsAsync(actor, project.Value.Project.Key, drafts, cancellationToken);
        var addIssues = new List<FailureDetail>();
        var found = await people.FindManyAsync(actor.OrganizationId, [.. additions.Where(a => a is not null).Select(a => a.PersonId).Distinct()], cancellationToken);
        var checkedAdditions = NewItemAddMembersChecker.Check(additions, project.Value.Members, roles.Value, found, addIssues);
        var assignable = active.Select(member => member.PersonId).Concat(additions.Where(a => a is not null).Select(a => a.PersonId)).ToHashSet();
        var membersText = NewItemChecker.HasUnknownAssignee(drafts, assignable)
            ? await UnknownAssigneeTextAsync(actor, project.Value, active, roles.Value, canManage, cancellationToken)
            : null;
        var planningIds = await FindPlanningAsync(actor, project.Value.Project.Key, drafts, cancellationToken);
        var checkedItems = NewItemChecker.Check(drafts, project.Value, parents, assignable, membersText, planningIds, addIssues);
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
            AddMembers = NewItemMapper.WriteAddMembers(checkedAdditions),
            Status = NewItemProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.NewItemProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return NewItemMapper.ToProposal(row, time.GetUtcNow());
    }

    // 負責人不是（有效的）成員、也沒有 addMembers 可以帶他時，訊息要說明誰能加人：沒有 project.manage 才需要查名單。
    // 已停用的成員和檢查時一樣算「不能指派」，所以用有效成員的名單判斷，不用 project.Members。
    private static bool NeedsManagersText(
        IReadOnlyList<NewItemDraft> drafts, IReadOnlyList<(Guid PersonId, string DisplayName)> active, bool canManage) =>
        !canManage && drafts.Any(d => d?.AssigneeId is { } id && active.All(m => m.PersonId != id));

    private async Task<Failure> ForbiddenAsync(
        Actor actor, ProjectDetail project, IReadOnlyList<ProjectRoleInfo> roles, CancellationToken cancellationToken)
    {
        var managers = await MemberManagers.ListAsync(people, actor.OrganizationId, project.Members, roles, cancellationToken);
        return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, MemberManagers.ForbiddenMessage(managers));
    }

    private async Task<string> UnknownAssigneeTextAsync(
        Actor actor,
        ProjectDetail project,
        IReadOnlyList<(Guid PersonId, string DisplayName)> active,
        IReadOnlyList<ProjectRoleInfo> roles,
        bool canManage,
        CancellationToken cancellationToken)
    {
        if (canManage)
        {
            return NewItemMembers.Describe(active) + "他還不是專案成員，可以把他放進 addMembers 一起加入。";
        }

        var managers = await MemberManagers.ListAsync(people, actor.OrganizationId, project.Members, roles, cancellationToken);
        return NewItemMembers.Describe(active) + "你不能把人加進專案。" + MemberManagers.WhoCanText(managers) + "或先指派給自己、不指派，之後再改。";
    }

    // 只在有項目用到時才查；讀不到（沒有 item.read）是 null，檢查時對應的欄位是錯誤。
    private async Task<NewItemChecker.PlanningIds> FindPlanningAsync(
        Actor actor, string projectKey, IReadOnlyList<NewItemDraft> drafts, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? cycles = new HashSet<Guid>();
        if (drafts.Any(d => d?.CycleId is not null))
        {
            var listed = await planning.ListCyclesAsync(actor, projectKey, cancellationToken);
            cycles = listed.Failure is null ? listed.Value.Select(c => c.Id).ToHashSet() : null;
        }

        IReadOnlySet<Guid>? milestones = new HashSet<Guid>();
        if (drafts.Any(d => d?.MilestoneId is not null))
        {
            var listed = await planning.ListMilestonesAsync(actor, projectKey, cancellationToken);
            milestones = listed.Failure is null ? listed.Value.Select(m => m.Id).ToHashSet() : null;
        }

        return new NewItemChecker.PlanningIds(cycles, milestones);
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
