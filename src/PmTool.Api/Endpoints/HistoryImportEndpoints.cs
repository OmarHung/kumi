using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 歷史匯入（把別的系統搬過來）。規則在工作引擎（<see cref="IHistoryImportService"/>），這裡只負責請求的形狀與回應的格式。
/// 一次性的搬家，只給命令列（權杖）用；網頁沒有對應的畫面。
/// </summary>
internal static class HistoryImportEndpoints
{
    private const string SourceField = "source";
    private const string ExternalIdField = "externalId";
    private const string UploadedByField = "uploadedBy";
    private const string UploadedAtField = "uploadedAt";

    private static readonly IReadOnlySet<string> AttachmentFields =
        new HashSet<string>(StringComparer.Ordinal) { SourceField, ExternalIdField, UploadedByField, UploadedAtField };

    public static RouteGroupBuilder MapHistoryImportEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/projects/{projectKey}/history/items", ImportItemsAsync)
            .WithName("ImportHistoryItems")
            .RequireTokenScope(AccessTokenScope.ItemsWrite)
            .RequireRateLimiting(ApiRateLimiting.ImportPolicy)
            .WithJsonBodyLimit(ApiLimits.MaxImportBodyBytes);
        v1.MapPost("/projects/{projectKey}/history/comments", ImportCommentsAsync)
            .WithName("ImportHistoryComments")
            .RequireTokenScope(AccessTokenScope.ItemsWrite)
            .RequireRateLimiting(ApiRateLimiting.ImportPolicy)
            .WithJsonBodyLimit(ApiLimits.MaxImportBodyBytes);
        v1.MapPost("/items/{itemKey}/history/attachments", ImportAttachmentAsync)
            .WithName("ImportHistoryAttachment")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPost("/history/lookup", LookupAsync)
            .WithName("LookupHistory")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        return v1;
    }

    private static async Task<IResult> ImportItemsAsync(
        string projectKey,
        HttpContext context,
        [FromServices] IHistoryImportService history,
        CancellationToken cancellationToken)
    {
        if (await ImportAccessGate.CheckAsync(context, projectKey, WorkPermissions.ProjectManage, rejectArchived: false) is { } denied)
        {
            return denied;
        }

        var body = (await JsonBody<HistoryItemsRequest>.BindAsync(context))!;
        var principal = context.User;
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return ItemRequestPaths.UseModuleNames(bodyFailure).ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await history.ImportItemsAsync(actor, projectKey, request.ToHistoryItems(), cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(HistoryItemsData.From(result.Value));
    }

    private static async Task<IResult> ImportCommentsAsync(
        string projectKey,
        HttpContext context,
        [FromServices] IHistoryImportService history,
        CancellationToken cancellationToken)
    {
        if (await ImportAccessGate.CheckAsync(context, projectKey, WorkPermissions.ProjectManage, rejectArchived: false) is { } denied)
        {
            return denied;
        }

        var body = (await JsonBody<HistoryCommentsRequest>.BindAsync(context))!;
        var principal = context.User;
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await history.ImportCommentsAsync(actor, projectKey, request.ToHistoryComments(), cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(HistoryCommentsData.From(result.Value));
    }

    private static async Task<IResult> LookupAsync(
        JsonBody<HistoryLookupRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IHistoryImportService history,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await history.LookupAsync(actor, request.ToHistoryLookup(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(result.Value.Select(HistoryRefData.From).ToArray());
    }

    /// <summary>
    /// multipart：欄位 source、externalId、uploadedBy、uploadedAt 在前，檔案（欄位 file）在最後；檔案是串流，過了它就讀不到後面的欄位。
    /// 新建立回 201，之前匯入過回 200（不讀檔案內容）。
    /// </summary>
    private static async Task<IResult> ImportAttachmentAsync(
        string itemKey,
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IHistoryImportService history,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        AttachmentUpload.RaiseKestrelLimit(context);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var (file, openFailure) = await AttachmentUpload.OpenAsync(context.Request, cancellationToken, fields, AttachmentFields);
        if (file is null)
        {
            return openFailure!.ToResult();
        }

        if (ParseFields(fields, file) is not { } upload)
        {
            return FieldsInvalid.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        Result<HistoryAttachmentResult> result;
        try
        {
            result = await history.ImportAttachmentAsync(actor, itemKey, upload, cancellationToken);
        }
        catch (AttachmentUpload.RequestTooLargeException)
        {
            return AttachmentUpload.TooLarge.ToResult();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return AttachmentUpload.TooLarge.ToResult();
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            return ApiFailure.BadRequest.ToResult();
        }

        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var data = (await AttachmentData.FromManyAsync([result.Value.Attachment], people, actor.OrganizationId, cancellationToken))[0];
        return result.Value.Existing ? EndpointResults.Ok(data) : EndpointResults.Created(null, data);
    }

    private static readonly ApiFailure FieldsInvalid = ApiFailure.BadRequest with
    {
        Message = "上傳歷史附件要在檔案（欄位 file）之前放欄位 source、externalId、uploadedBy（人員編號）、uploadedAt（ISO 8601，帶時區）。",
    };

    private static HistoryAttachment? ParseFields(IReadOnlyDictionary<string, string> fields, AttachmentUpload.OpenedFile file)
    {
        if (!fields.TryGetValue(SourceField, out var source)
            || !fields.TryGetValue(ExternalIdField, out var externalId)
            || !fields.TryGetValue(UploadedByField, out var uploadedByText) || !Guid.TryParseExact(uploadedByText, "D", out var uploadedBy)
            || !fields.TryGetValue(UploadedAtField, out var uploadedAtText) || !ZonedTimestamp.TryParse(uploadedAtText, out var uploadedAt))
        {
            return null;
        }

        return new HistoryAttachment
        {
            Source = source,
            ExternalId = externalId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Content = file.Content,
            UploadedBy = uploadedBy,
            UploadedAt = uploadedAt,
        };
    }
}
