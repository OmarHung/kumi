using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>項目的附件（PM-5）。權限、數量、檔名的規則都在工作引擎，這裡只讀 multipart、轉換格式與寫下載的標頭。</summary>
internal static class AttachmentEndpoints
{
    public static RouteGroupBuilder MapAttachmentEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/items/{itemKey}/attachments", ListAttachmentsAsync)
            .WithName("ListItemAttachments")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPost("/items/{itemKey}/attachments", UploadAttachmentAsync)
            .WithName("UploadItemAttachment")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapGet("/attachments/{id}/content", DownloadAttachmentAsync)
            .WithName("DownloadAttachment")
            .RequireTokenScope(AccessTokenScope.ItemsRead)
            .RequireRateLimiting(ApiRateLimiting.AttachmentDownloadPolicy);
        v1.MapDelete("/attachments/{id}", DeleteAttachmentAsync)
            .WithName("DeleteAttachment")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        return v1;
    }

    private static async Task<IResult> ListAttachmentsAsync(
        string itemKey,
        ClaimsPrincipal principal,
        [FromServices] IAttachmentService attachments,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await attachments.ListAsync(actor, itemKey, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async list => EndpointResults.Ok(
            await AttachmentData.FromManyAsync(list, people, actor.OrganizationId, cancellationToken)));
    }

    /// <summary>
    /// 一次一個檔案，欄位 file。內容是串流，不放進記憶體；請求的大小上限只對這個端點放寬。
    /// 回 201 但不帶 Location：附件沒有自己的詳情網址，內容在 /attachments/{id}/content。
    /// </summary>
    private static async Task<IResult> UploadAttachmentAsync(
        string itemKey,
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IAttachmentService attachments,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        AttachmentUpload.RaiseKestrelLimit(context);
        var (file, failure) = await AttachmentUpload.OpenAsync(context.Request, cancellationToken);
        if (file is null)
        {
            return failure!.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        Result<Attachment> result;
        try
        {
            var upload = new NewAttachment { FileName = file.FileName, ContentType = file.ContentType, Content = file.Content };
            result = await attachments.AddAsync(actor, itemKey, upload, cancellationToken);
        }
        catch (AttachmentUpload.RequestTooLargeException)
        {
            // 內容讀到一半才發現整個請求超過上限（沒寫 Content-Length 的分塊上傳）；工作引擎已經清掉寫了一半的檔案。
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

        return await EndpointResults.OkOrFailAsync(result, async attachment => EndpointResults.Created(
            null, (await AttachmentData.FromManyAsync([attachment], people, actor.OrganizationId, cancellationToken))[0]));
    }

    private static async Task<IResult> DownloadAttachmentAsync(
        string id,
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(id, "D", out var attachmentId))
        {
            return EndpointResults.Fail(NotFound());
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await attachments.OpenAsync(actor, attachmentId, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        // 標頭在這裡寫好、回傳串流：Results.Stream 讀完或中斷時會把串流關掉，儲存空間的檔案控制代碼不會漏。
        var content = result.Value;
        AttachmentDownload.Apply(context.Response, content.Attachment);
        return Results.Stream(content.Content, contentType: context.Response.ContentType, enableRangeProcessing: false);
    }

    private static async Task<IResult> DeleteAttachmentAsync(
        string id,
        ClaimsPrincipal principal,
        [FromServices] IAttachmentService attachments,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(id, "D", out var attachmentId))
        {
            return EndpointResults.Fail(NotFound());
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await attachments.DeleteAsync(actor, attachmentId, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async attachment => EndpointResults.Ok(
            (await AttachmentData.FromManyAsync([attachment], people, actor.OrganizationId, cancellationToken))[0]));
    }

    /// <summary>不是編號格式的路徑參數，和找不到附件同一個回答：不讓人從回應分辨格式錯誤與不存在。</summary>
    private static Failure NotFound() =>
        new(FailureKind.NotFound, WorkErrorCodes.AttachmentNotFound, "找不到這個附件。");
}
