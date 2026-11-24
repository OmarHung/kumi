using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.ChangeSets;
using PmTool.Work.Common;
using PmTool.Work.Persistence;
using PmTool.Work.Templates;

namespace PmTool.Work.Projects;

internal sealed partial class ProjectService(
    IDbContextFactory<WorkDbContext> contexts,
    TemplateCatalog templates,
    BlueprintCache cache,
    IBlueprintValidator validator,
    IBlueprintPlanner planner,
    ChangeSetLimits limits,
    IOrganizationClock organizationClock,
    TimeProvider time) : IProjectService
{
    private const long FirstVersion = 1;

    public IReadOnlyList<TemplateSummary> ListTemplates() => templates.Summaries;

    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await VisibleProjects.Of(db, actor)
            .AsNoTracking()
            .OrderBy(x => x.Project.CreatedAt)
            .ThenBy(x => x.Project.Id)
            .Select(x => new ProjectSummary(
                x.Project.Id, x.Project.Key, x.Project.Name, x.Project.CurrentBlueprintVersion, x.Role, x.Project.CreatedAt)
            {
                ArchivedAt = x.Project.ArchivedAt,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<ProjectSummary>> CreateAsync(Actor actor, NewProject project, CancellationToken cancellationToken)
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

        var (key, name, template, description) = checkedProject.Value;
        var (document, schema) = PrepareBlueprint(template, key, name);
        var examples = project.IncludeExamples ? template.ExampleSet : ExampleSet.Empty;
        var entity = new ProjectEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            Key = key,
            Name = name,
            Template = template.Key,
            CurrentBlueprintVersion = FirstVersion,
            LastItemNumber = examples.Items.Count,
            LastChangeSetNumber = 0,
            CreatedBy = actor.PersonId,
            CreatedAt = WorkClock.Now(time),
        };
        var access = ProjectAccess.For(actor, entity, schema, ProjectRoles.Admin);
        // 只有要附範例時才需要時區；不附就不必多查一次。
        var zone = project.IncludeExamples
            ? await organizationClock.TimeZoneAsync(actor.OrganizationId, cancellationToken)
            : TimeZoneInfo.Utc;
        if (!await TryInsertAsync(actor, access, document, examples, description, zone, cancellationToken))
        {
            return WorkFailures.ProjectKeyTaken(key);
        }

        cache.Remember(entity.Id, FirstVersion, schema);
        return access.Summary;
    }

    public async Task<Result<ProjectDetail>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        var members = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == actor.OrganizationId && m.ProjectId == access.Project.Id)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => new ProjectMember(m.PersonId, m.Role))
            .ToListAsync(cancellationToken);
        return new ProjectDetail(access.Summary, VisibleBlueprint(access), members, access.Permissions);
    }

    public async Task<Result<ProjectMember>> SetMemberAsync(
        Actor actor, string projectKey, Guid personId, string role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 讀藍圖之前先對專案取共用鎖：套用中的變更集可能拿掉這個角色，要等它做完再照新的藍圖檢查；
        // 套用也會等這裡提交，才數得到這位成員。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.ProjectManage))
        {
            return access.Missing(WorkPermissions.ProjectManage);
        }

        if (role is null || !ProjectRoles.IsKnown(role, access.Schema))
        {
            var known = string.Join("、", ProjectRoles.All(access.Schema).Select(r => TextRules.Quote(ProjectRoles.DisplayName(r, access.Schema))));
            return WorkFailures.Invalid(
                WorkErrorCodes.MemberUnknownRole, "role", $"這個專案沒有{TextRules.Quote(role)}這個角色。可以用的角色：{known}。");
        }

        // 把唯一的 admin 換成別的角色，專案就沒有人能管理成員了；組織管理員不擋，理由見 MemberLocks.GuardsLastAdmin。
        if (role != ProjectRoles.Admin
            && MemberLocks.GuardsLastAdmin(actor)
            && (await MemberLocks.LockAsync(db, access.Project, personId, cancellationToken)).RemovesLastAdmin)
        {
            return MemberLocks.LastAdmin(access.Project.Key);
        }

        // 有就改角色、沒有就加入，一個陳述式做完；同時加同一個人也不會撞到唯一限制。
        var now = WorkClock.Now(time);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO work.project_members (id, organization_id, project_id, person_id, role, created_at, updated_at)
            VALUES ({WorkClock.NewId(time)}, {actor.OrganizationId}, {access.Project.Id}, {personId}, {role}, {now}, {now})
            ON CONFLICT (project_id, person_id) DO UPDATE SET role = excluded.role, updated_at = excluded.updated_at
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProjectMember(personId, role);
    }

    public async Task<Result<ProjectMember>> RemoveMemberAsync(
        Actor actor, string projectKey, Guid personId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 和設定成員一樣先取專案的共用鎖：套用中的變更集在數成員的角色，要等它做完。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.ProjectManage))
        {
            return access.Missing(WorkPermissions.ProjectManage);
        }

        var locked = await MemberLocks.LockAsync(db, access.Project, personId, cancellationToken);
        if (locked.Target is not { } member)
        {
            return MemberLocks.MemberNotFound(access.Project.Key);
        }

        if (MemberLocks.GuardsLastAdmin(actor) && locked.RemovesLastAdmin)
        {
            return MemberLocks.LastAdmin(access.Project.Key);
        }

        // 他負責的項目與成員欄位的值都留著；之後要把他填進去時，成員的檢查才會擋下。
        await db.ProjectMembers.Where(m => m.OrganizationId == actor.OrganizationId && m.Id == member.Id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProjectMember(member.PersonId, member.Role);
    }

    /// <summary>
    /// 沒有 blueprint.read 的人（例如接案專案的客戶）也要靠藍圖顯示與編輯項目，所以類型、欄位、流程、角色、檢視照給；
    /// 自動化與綁定寫著外部系統的資源名稱與內部的運作方式，只給讀得到藍圖的人。
    /// </summary>
    private static ProjectBlueprint VisibleBlueprint(ProjectAccess access) =>
        access.Can(WorkPermissions.BlueprintRead)
            ? access.Schema.Blueprint
            : access.Schema.Blueprint with { Automations = [], Bindings = [] };

    /// <summary>
    /// 模板換上這個專案的 metadata，先算一次計畫、再寫成文件並照讀回來的方式驗證一次：
    /// 存進去的文件之後每次都要讀得回來，現在不通過就是模板或程式有錯，不能開出這個專案。
    /// </summary>
    private (string Document, ProjectSchema Schema) PrepareBlueprint(WorkTemplate template, string key, string name)
    {
        var blueprint = template.Blueprint with
        {
            Metadata = new BlueprintMetadata
            {
                Key = key.ToLowerInvariant(),
                Name = name,
                Description = template.Blueprint.Metadata.Description,
                BasedOn = new TemplateReference { Template = template.Key, Version = template.Version },
            },
        };
        var plan = planner.Plan(null, 0, new ChangeSetRequest { BaseVersion = 0, Blueprint = blueprint });
        var document = BlueprintSerializer.ToJson(blueprint);
        var readBack = validator.ValidateBlueprint(document, DocumentFormat.Json);
        var issues = plan.Report.Issues.Concat(readBack.Report.Issues).ToArray();
        return issues.Length == 0 && readBack.Blueprint is { } stored
            ? (document, ProjectSchema.From(stored))
            : throw new InvalidOperationException(
                $"模板 {template.Key} 開不出專案：{string.Join("；", issues.Take(5).Select(i => $"{i.Path} {i.Message}"))}");
    }

    /// <returns>代號已經有人用了就回 false。重複靠唯一限制把關，同時開兩個同代號的專案只會成功一個。</returns>
    private async Task<bool> TryInsertAsync(
        Actor actor,
        ProjectAccess access,
        string document,
        ExampleSet examples,
        string? description,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var project = access.Project;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.Projects.Add(project);
        db.BlueprintVersions.Add(new BlueprintVersionEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            ProjectId = project.Id,
            Version = FirstVersion,
            Document = document,
            CreatedBy = actor.PersonId,
            CreatedAt = project.CreatedAt,
        });
        db.ProjectMembers.Add(new ProjectMemberEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            ProjectId = project.Id,
            PersonId = actor.PersonId,
            Role = ProjectRoles.Admin,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.CreatedAt,
        });

        // 說明也在同一次儲存裡：專案說明不合格時整個專案都不建立，不會留下沒有說明的專案。
        if (description is not null)
        {
            db.ProjectDescriptions.Add(new ProjectDescriptionEntity
            {
                ProjectId = project.Id,
                OrganizationId = actor.OrganizationId,
                Text = description,
                UpdatedAt = project.CreatedAt,
                UpdatedBy = actor.PersonId,
            });
        }

        // 範例和專案在同一次儲存裡：同一個交易，專案代號撞到時範例也跟著退回。
        ExampleItems.AddTo(db, actor, access, examples, project.CreatedAt, zone);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.ProjectKeyIndex))
        {
            return false;
        }
    }
}
