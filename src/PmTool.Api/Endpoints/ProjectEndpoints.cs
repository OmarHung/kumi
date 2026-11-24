using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Live;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

internal sealed record TemplateWorkflowData(
    string Name, IReadOnlyList<string> ItemTypes, IReadOnlyList<string> States, IReadOnlyList<StateCategory> StateCategories,
    IReadOnlyList<string> CanceledStates);

internal sealed record TemplateParentData(string ItemType, IReadOnlyList<string> Parents);

internal sealed record TemplateData(
    string Key, string Name, string? Description, IReadOnlyList<TemplateWorkflowData> Workflows,
    IReadOnlyList<TemplateParentData> Parents, IReadOnlyList<string> Fields, int ExampleCount)
{
    public static TemplateData From(TemplateSummary template) => new(
        template.Key,
        template.Name,
        template.Description,
        [.. template.Workflows.Select(w => new TemplateWorkflowData(w.Name, w.ItemTypeNames, w.StateNames, w.StateCategories, w.CanceledStateNames))],
        [.. template.Parents.Select(p => new TemplateParentData(p.ItemTypeName, p.ParentTypeNames))],
        template.FieldNames,
        template.ExampleCount);
}

/// <param name="ArchivedAt">封存的時刻；沒有封存是 null（序列化時照樣寫出 null，網頁靠它分辨）。</param>
internal sealed record ProjectData(
    Guid Id, string Key, string Name, long BlueprintVersion, string? MyRole, DateTimeOffset CreatedAt, DateTimeOffset? ArchivedAt)
{
    public static ProjectData From(ProjectSummary project) => new(
        project.Id, project.Key, project.Name, project.BlueprintVersion, project.MyRole, project.CreatedAt, project.ArchivedAt);
}

internal sealed record MemberData(Guid PersonId, string DisplayName, string Role);

internal sealed record ProjectDetailData(
    ProjectData Project, JsonElement Blueprint, IReadOnlyList<MemberData> Members, IReadOnlyList<string> MyPermissions);

internal sealed record CreateProjectRequest(
    string Key, string Name, string Template, bool IncludeExamples = false, string? Description = null);

internal sealed record SetMemberRequest(string Role);

internal sealed record RenameProjectRequest(string Name);

/// <summary>被移除前的成員資料。不帶顯示名稱：被移除的人可能已經不在組織裡，不必為了回應去查人員目錄。</summary>
internal sealed record RemovedMemberData(Guid PersonId, string Role);

internal static class ProjectEndpoints
{
    // 和工作引擎自己擋下時回的一樣（同一個代碼、同一種說法），網頁不必分辨是哪一層擋的。
    private static readonly Failure CannotManageMembers =
        new(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "你在這個專案沒有管理專案成員的權限。");

    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/templates", ListTemplates)
            .WithName("ListTemplates")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapGet("/projects", ListProjectsAsync)
            .WithName("ListProjects")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPost("/projects", CreateProjectAsync).WithName("CreateProject");
        v1.MapGet("/projects/{projectKey}", GetProjectAsync)
            .WithName("GetProject")
            .RequireTokenScope(AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead);
        v1.MapPut("/projects/{projectKey}/name", RenameProjectAsync)
            .WithName("RenameProject")
            .RequireTokenScope(AccessTokenScope.BlueprintWrite);
        v1.MapPost("/projects/{projectKey}/archive", ArchiveProjectAsync)
            .WithName("ArchiveProject")
            .RequireTokenScope(AccessTokenScope.BlueprintWrite);
        v1.MapPost("/projects/{projectKey}/unarchive", UnarchiveProjectAsync)
            .WithName("UnarchiveProject")
            .RequireTokenScope(AccessTokenScope.BlueprintWrite);
        v1.MapPut("/projects/{projectKey}/members/{personId:guid}", SetMemberAsync)
            .WithName("SetProjectMember")
            .RequireTokenScope(AccessTokenScope.BlueprintWrite);
        v1.MapDelete("/projects/{projectKey}/members/{personId:guid}", RemoveMemberAsync).WithName("RemoveProjectMember");
        return v1;
    }

    private static IResult ListTemplates([FromServices] IProjectService projects) =>
        EndpointResults.Ok(projects.ListTemplates().Select(TemplateData.From).ToArray());

    private static async Task<IResult> ListProjectsAsync(
        ClaimsPrincipal principal, [FromServices] IProjectService projects, CancellationToken cancellationToken)
    {
        var list = await projects.ListAsync(SessionPrincipal.Read(principal).ToActor(), cancellationToken);
        return EndpointResults.Ok(list.Select(ProjectData.From).ToArray());
    }

    private static async Task<IResult> CreateProjectAsync(
        JsonBody<CreateProjectRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IProjectService projects,
        [FromServices] ILiveNotifier live,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var newProject = new NewProject(request.Key, request.Name, request.Template, request.IncludeExamples)
        {
            Description = request.Description,
        };
        var result = await projects.CreateAsync(actor, newProject, cancellationToken);
        if (result.Failure is null)
        {
            // 管理員看得到所有專案，所以所有人的連線都要重算。
            live.AccessChanged(null);
        }

        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created($"/v1/projects/{Uri.EscapeDataString(result.Value.Key)}", ProjectData.From(result.Value));
    }

    private static async Task<IResult> RenameProjectAsync(
        string projectKey,
        JsonBody<RenameProjectRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IProjectService projects,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await projects.RenameAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, request.Name, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ProjectData.From(result.Value));
    }

    private static async Task<IResult> ArchiveProjectAsync(
        string projectKey, ClaimsPrincipal principal, [FromServices] IProjectService projects, CancellationToken cancellationToken)
    {
        var result = await projects.ArchiveAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ProjectData.From(result.Value));
    }

    private static async Task<IResult> UnarchiveProjectAsync(
        string projectKey, ClaimsPrincipal principal, [FromServices] IProjectService projects, CancellationToken cancellationToken)
    {
        var result = await projects.UnarchiveAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ProjectData.From(result.Value));
    }

    private static async Task<IResult> GetProjectAsync(
        string projectKey,
        ClaimsPrincipal principal,
        [FromServices] IProjectService projects,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await projects.GetAsync(actor, projectKey, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async detail =>
        {
            var names = await PeopleNames.LookupAsync(
                people, actor.OrganizationId, detail.Members.Select(m => m.PersonId), cancellationToken);
            return EndpointResults.Ok(new ProjectDetailData(
                ProjectData.From(detail.Project),
                BlueprintSerializer.ToElement(detail.Blueprint),
                [.. detail.Members.Select(m => new MemberData(m.PersonId, names.NameOf(m.PersonId), m.Role))],
                detail.MyPermissions));
        });
    }

    /// <summary>
    /// 看不看得到專案、能不能管理成員、這個人是不是成員都由工作引擎依序判斷：沒有權限的人分辨不出一個人員編號是不是成員。
    /// 不像加入成員那樣先查人員目錄：已經離開組織的人也要移得掉。
    /// </summary>
    private static async Task<IResult> RemoveMemberAsync(
        string projectKey,
        Guid personId,
        ClaimsPrincipal principal,
        [FromServices] IProjectService projects,
        [FromServices] ILiveNotifier live,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await projects.RemoveMemberAsync(actor, projectKey, personId, cancellationToken);
        if (result.Failure is null)
        {
            live.AccessChanged([personId]);
        }

        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new RemovedMemberData(result.Value.PersonId, result.Value.Role));
    }

    /// <summary>
    /// 先確認呼叫者看得到這個專案、而且能管理成員，再確認這個人在呼叫者的組織裡
    /// （工作引擎只存人員編號，不知道這個人是誰、屬於哪個組織）。順序反過來的話，
    /// 看不到專案的人也能拿這個端點分辨一個人員編號存不存在。
    /// </summary>
    private static async Task<IResult> SetMemberAsync(
        string projectKey,
        Guid personId,
        JsonBody<SetMemberRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IProjectService projects,
        [FromServices] IPeopleDirectory people,
        [FromServices] ILiveNotifier live,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var project = await projects.GetAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } notVisible)
        {
            return EndpointResults.Fail(notVisible);
        }

        if (!project.Value.MyPermissions.Contains(WorkPermissions.ProjectManage))
        {
            return EndpointResults.Fail(CannotManageMembers);
        }

        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, [personId], cancellationToken);
        if (names.Find(personId) is null)
        {
            return ApiFailure.PersonNotFound.ToResult();
        }

        var result = await projects.SetMemberAsync(actor, projectKey, personId, request.Role, cancellationToken);
        if (result.Failure is null)
        {
            live.AccessChanged([personId]);
        }

        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new MemberData(result.Value.PersonId, names.NameOf(result.Value.PersonId), result.Value.Role));
    }
}
