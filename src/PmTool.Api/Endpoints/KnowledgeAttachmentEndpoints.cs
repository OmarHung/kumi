using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 頁面的附件。權限與數量的規則在知識庫模組；上傳的讀法與下載的標頭和項目的附件共用
/// （<see cref="AttachmentUpload"/>、<see cref="AttachmentDownload"/>）。全部只能用 Cookie。
/// </summary>
internal static class KnowledgeAttachmentEndpoints
{
    public static RouteGroupBuilder MapKnowledgeAttachmentEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/knowledge/spaces/{spaceKey}/pages/{number}/attachments", ListAsync).WithName("ListKnowledgePageAttachments");
        v1.MapPost("/knowledge/spaces/{spaceKey}/pages/{number}/attachments", UploadAsync).WithName("UploadKnowledgePageAttachment");
        v1.MapGet("/knowledge/attachments/{id}/content", DownloadAsync).WithName("DownloadKnowledgePageAttachment")
            .RequireRateLimiting(ApiRateLimiting.AttachmentDownloadPolicy);
        return v1;
    }

    private static async Task<IResult> ListAsync(
        string spaceKey,
        string number,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeAttachmentService attachments,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await attachments.ListAsync(actor, spaceKey, pageNumber, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async list => EndpointResults.Ok(
            await KnowledgeAttachmentData.FromManyAsync(list, people, actor.OrganizationId, cancellationToken)));
    }

    /// <summary>一次一個檔案，欄位 file。內容是串流，不放進記憶體；請求的大小上限只對這個端點放寬。</summary>
    private static async Task<IResult> UploadAsync(
        string spaceKey,
        string number,
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeAttachmentService attachments,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        // 頁面編號不對先回 404：不必讀那個可能有 25 MiB 的內容。
        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        AttachmentUpload.RaiseKestrelLimit(context);
        var (file, failure) = await AttachmentUpload.OpenAsync(
            context.Request, cancellationToken, tooLargeCode: KnowledgeErrorCodes.AttachmentTooLarge);
        if (file is null)
        {
            return failure!.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var tooLarge = AttachmentUpload.TooLargeWith(KnowledgeErrorCodes.AttachmentTooLarge);
        Result<PageAttachment> result;
        try
        {
            var upload = new NewPageAttachment { FileName = file.FileName, ContentType = file.ContentType, Content = file.Content };
            result = await attachments.AddAsync(actor, spaceKey, pageNumber, upload, cancellationToken);
        }
        catch (AttachmentUpload.RequestTooLargeException)
        {
            // 內容讀到一半才發現整個請求超過上限（沒寫 Content-Length 的分塊上傳）；服務已經清掉寫了一半的檔案。
            return tooLarge.ToResult();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return tooLarge.ToResult();
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            return ApiFailure.BadRequest.ToResult();
        }

        return await EndpointResults.OkOrFailAsync(result, async attachment => EndpointResults.Created(
            null, (await KnowledgeAttachmentData.FromManyAsync([attachment], people, actor.OrganizationId, cancellationToken))[0]));
    }

    private static async Task<IResult> DownloadAsync(
        string id,
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(id, "D", out var attachmentId))
        {
            return EndpointResults.Fail(KnowledgeRoutes.AttachmentNotFound());
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await attachments.OpenAsync(actor, attachmentId, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        // 標頭在這裡寫好、回傳串流：Results.Stream 讀完或中斷時會把串流關掉，儲存空間的檔案控制代碼不會漏。
        var content = result.Value;
        var attachment = content.Attachment;
        AttachmentDownload.Apply(context.Response, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.Sha256);
        return Results.Stream(content.Content, contentType: context.Response.ContentType, enableRangeProcessing: false);
    }
}
