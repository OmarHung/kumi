using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>匯入的一列：新增項目的內容，再加一個可以不給的狀態（直接設定，不走轉換）。</summary>
internal sealed record ImportRowRequest(
    string Type,
    string Title,
    Guid? AssigneeId = null,
    Priority? Priority = null,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null,
    decimal? Estimate = null,
    string? ParentKey = null,
    string? Description = null,
    IReadOnlyDictionary<string, JsonElement>? Fields = null,
    Guid? CycleId = null,
    Guid? MilestoneId = null,
    string? State = null)
    : CreateItemRequest(Type, Title, AssigneeId, Priority, StartDate, DueDate, Estimate, ParentKey, Description, Fields, CycleId, MilestoneId)
{
    public ImportedItem ToImportedItem() => new() { Item = ToNewWorkItem(), State = State };
}

internal sealed record ImportItemsRequest(IReadOnlyList<ImportRowRequest>? Rows = null, bool DryRun = false)
{
    public ItemImport ToImport() => new() { Rows = [.. (Rows ?? []).Select(row => row?.ToImportedItem() ?? new ImportedItem { Item = null! })], DryRun = DryRun };
}

internal sealed record ImportResultData(bool DryRun, int Count, IReadOnlyList<string> CreatedKeys);

/// <summary>試算表匯入（PM-4b）。規則在工作引擎（<see cref="IItemImportService"/>），這裡只負責請求的形狀。</summary>
internal static class ItemImportEndpoints
{
    public static RouteGroupBuilder MapItemImportEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/projects/{projectKey}/items/import", ImportAsync)
            .WithName("ImportItems")
            .RequireTokenScope(AccessTokenScope.ItemsWrite)
            .WithJsonBodyLimit(ApiLimits.MaxImportBodyBytes);
        return v1;
    }

    private static async Task<IResult> ImportAsync(
        string projectKey,
        JsonBody<ImportItemsRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IItemImportService import,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return ItemRequestPaths.UseModuleNames(bodyFailure).ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await import.ImportAsync(actor, projectKey, request.ToImport(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new ImportResultData(result.Value.DryRun, result.Value.Count, result.Value.CreatedKeys));
    }
}
