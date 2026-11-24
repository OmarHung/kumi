using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <param name="Text">null 或整段空白是清掉說明。</param>
internal sealed record SetProjectDescriptionRequest(string? Text);

/// <summary>專案說明在 API 上的形狀。更新的人和其他回應一樣帶顯示名稱；沒寫過時三個欄位都是 null。</summary>
internal sealed record ProjectDescriptionData(string? Text, DateTimeOffset? UpdatedAt, PersonReference? UpdatedBy)
{
    public static async Task<ProjectDescriptionData> FromAsync(
        ProjectDescription description, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        if (description.UpdatedBy is not { } updatedBy)
        {
            return new ProjectDescriptionData(description.Text, description.UpdatedAt, null);
        }

        var names = await PeopleNames.LookupAsync(people, organizationId, [updatedBy], cancellationToken);
        return new ProjectDescriptionData(description.Text, description.UpdatedAt, new PersonReference(updatedBy, names.NameOf(updatedBy)));
    }
}

/// <summary>專案搜尋的一筆。<c>Project</c> 和專案清單的一筆同一個形狀；代號或名稱就比對到的 <c>Snippet</c> 是 null。</summary>
internal sealed record ProjectSearchHitData(ProjectData Project, string? Snippet)
{
    public static ProjectSearchHitData From(ProjectSearchHit hit) => new(ProjectData.From(hit.Project), hit.Snippet);
}

/// <param name="IsDefault">省略就是 false。</param>
internal sealed record DescriptionTemplateRequest(string Name, string Body, bool IsDefault = false)
{
    public DescriptionTemplateInput ToInput() => new(Name, Body, IsDefault);
}

internal sealed record DescriptionTemplateData(string Id, string Name, string Body, bool IsBuiltIn, bool IsDefault)
{
    public static DescriptionTemplateData From(DescriptionTemplate template) =>
        new(template.Id, template.Name, template.Body, template.IsBuiltIn, template.IsDefault);
}

internal static class ProjectDescriptionEndpoints
{
    private const int DefaultSearchLimit = 8;

    public static RouteGroupBuilder MapProjectDescriptionEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/projects/{projectKey}/description", GetDescriptionAsync)
            .WithName("GetProjectDescription")
            .RequireTokenScope(AccessTokenScope.ItemsRead);

        // 和改名同一個範圍：兩個都是專案本身的設定。
        v1.MapPut("/projects/{projectKey}/description", SetDescriptionAsync)
            .WithName("SetProjectDescription")
            .RequireTokenScope(AccessTokenScope.BlueprintWrite);
        v1.MapGet("/description-templates", ListTemplatesAsync)
            .WithName("ListDescriptionTemplates")
            .RequireTokenScope(AccessTokenScope.ItemsRead);

        // 管理說明格式是組織設定，和其他組織設定一樣只能在網頁上做：不宣告範圍，權杖一律不給用。
        v1.MapPost("/description-templates", CreateTemplateAsync).WithName("CreateDescriptionTemplate");
        v1.MapPut("/description-templates/{id}", UpdateTemplateAsync).WithName("UpdateDescriptionTemplate");
        v1.MapDelete("/description-templates/{id}", DeleteTemplateAsync).WithName("DeleteDescriptionTemplate");

        // 搜尋的範圍是呼叫者看得到的專案，和專案清單一樣讀，所以和項目的搜尋同一個權杖範圍。
        v1.MapGet("/search/projects", SearchProjectsAsync)
            .WithName("SearchProjects")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        return v1;
    }

    private static async Task<IResult> SearchProjectsAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        [FromServices] IProjectDescriptionService descriptions,
        CancellationToken cancellationToken)
    {
        var (text, limit, failure) = QueryParameters.SearchOf(request.Query, DefaultSearchLimit);
        if (failure is not null)
        {
            return EndpointResults.Fail(failure);
        }

        var result = await descriptions.SearchProjectsAsync(SessionPrincipal.Read(principal).ToActor(), text!, limit, cancellationToken);
        return result.Failure is { } moduleFailure
            ? EndpointResults.Fail(moduleFailure)
            : EndpointResults.Ok(result.Value.Select(ProjectSearchHitData.From).ToArray());
    }

    private static async Task<IResult> GetDescriptionAsync(
        string projectKey,
        ClaimsPrincipal principal,
        [FromServices] IProjectDescriptionService descriptions,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await descriptions.GetAsync(actor, projectKey, cancellationToken);
        return await RespondAsync(result, people, actor.OrganizationId, cancellationToken);
    }

    private static async Task<IResult> SetDescriptionAsync(
        string projectKey,
        JsonBody<SetProjectDescriptionRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IProjectDescriptionService descriptions,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await descriptions.SetAsync(actor, projectKey, request.Text, cancellationToken);
        return await RespondAsync(result, people, actor.OrganizationId, cancellationToken);
    }

    private static async Task<IResult> ListTemplatesAsync(
        ClaimsPrincipal principal, [FromServices] IProjectDescriptionService descriptions, CancellationToken cancellationToken)
    {
        var templates = await descriptions.ListTemplatesAsync(SessionPrincipal.Read(principal).ToActor(), cancellationToken);
        return EndpointResults.Ok(templates.Select(DescriptionTemplateData.From).ToArray());
    }

    private static async Task<IResult> CreateTemplateAsync(
        JsonBody<DescriptionTemplateRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IProjectDescriptionService descriptions,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await descriptions.CreateTemplateAsync(actor, request.ToInput(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created(
                $"/v1/description-templates/{Uri.EscapeDataString(result.Value.Id)}", DescriptionTemplateData.From(result.Value));
    }

    private static async Task<IResult> UpdateTemplateAsync(
        string id,
        JsonBody<DescriptionTemplateRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IProjectDescriptionService descriptions,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var templateId = ParseId(id);
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await descriptions.UpdateTemplateAsync(actor, templateId, request.ToInput(), cancellationToken);
        return RespondTemplate(result);
    }

    private static async Task<IResult> DeleteTemplateAsync(
        string id,
        ClaimsPrincipal principal,
        [FromServices] IProjectDescriptionService descriptions,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return RespondTemplate(await descriptions.DeleteTemplateAsync(actor, ParseId(id), cancellationToken));
    }

    /// <summary>
    /// 內建的 key 與亂打的字不是編號：換成 Guid.Empty 照樣交給模組，讓它照「權限先、找不到後」回
    /// （管理員 404、非管理員 403），不在這裡先回 404 而洩漏誰有權限。
    /// </summary>
    private static Guid ParseId(string id) => Guid.TryParse(id, out var templateId) ? templateId : Guid.Empty;

    private static IResult RespondTemplate(Result<DescriptionTemplate> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(DescriptionTemplateData.From(result.Value));

    private static async Task<IResult> RespondAsync(
        Result<ProjectDescription> result, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken) =>
        result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(await ProjectDescriptionData.FromAsync(result.Value, people, organizationId, cancellationToken));
}
