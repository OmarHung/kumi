using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理訊息的附件：上傳（待送）、刪掉待送的、下載。權限、類型判斷、數量與容量的規則都在 AI 模組，
/// 這裡只讀 multipart、轉換格式與寫下載的標頭（呈現的規則和項目的附件共用，<see cref="AttachmentDownload"/>）。
/// 全部只能用 Cookie：不宣告權杖範圍。規格見 <c>internal/HANDOFF-ai-attachments.md</c> 第 3 節。
/// </summary>
internal static class AssistantAttachmentEndpoints
{
    private const string Route = "/projects/{projectKey}/assistant/attachments";

    /// <summary>請求的大小上限：檔案最大的那一種加上 multipart 的開銷。依類型的上限由模組判斷。</summary>
    private const long MaxRequestBytes = AssistantAttachmentRules.MaxUploadBytes + AttachmentUpload.MultipartOverheadBytes;

    private static readonly ApiFailure TooLarge = ApiFailure.PayloadLargerThan(0) with
    {
        Code = AiErrorCodes.AttachmentTooLarge,
        Message = $"附件不能超過 {AssistantAttachmentRules.MaxUploadBytes / (1024 * 1024)} MB。",
    };

    public static RouteGroupBuilder MapAssistantAttachmentEndpoints(this RouteGroupBuilder v1)
    {
        // 每一次上傳都會讓伺服器寫檔、判斷內容，依人限流。
        v1.MapPost(Route, UploadAsync)
            .WithName("UploadAssistantAttachment")
            .RequireRateLimiting(ApiRateLimiting.AssistantAttachmentUploadPolicy);
        v1.MapDelete(Route + "/{id}", DeleteAsync).WithName("DeleteAssistantAttachment");
        v1.MapGet(Route + "/{id}/content", DownloadAsync)
            .WithName("DownloadAssistantAttachment")
            .RequireRateLimiting(ApiRateLimiting.AttachmentDownloadPolicy);
        return v1;
    }

    /// <summary>一次一個檔案，欄位 file。內容是串流，不放進記憶體；請求的大小上限只對這個端點放寬。</summary>
    private static async Task<IResult> UploadAsync(
        string projectKey,
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IAssistantAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        AttachmentUpload.RaiseKestrelLimit(context, MaxRequestBytes);
        var (file, failure) = await AttachmentUpload.OpenAsync(
            context.Request, cancellationToken, maxRequestBytes: MaxRequestBytes, tooLargeFailure: TooLarge);
        if (file is null)
        {
            return failure!.ToResult();
        }

        Result<AssistantAttachment> result;
        try
        {
            var upload = new NewAssistantAttachment { FileName = file.FileName, ContentType = file.ContentType, Content = file.Content };
            result = await attachments.UploadAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, upload, cancellationToken);
        }
        catch (AttachmentUpload.RequestTooLargeException)
        {
            // 內容讀到一半才發現整個請求超過上限（沒寫 Content-Length 的分塊上傳）；模組已經清掉寫了一半的檔案。
            return TooLarge.ToResult();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge.ToResult();
        }
        // 客戶端中途斷線時，讀內容的 IOException 是在模組裡丟出來的（內容一路串流進去）：是壞請求，不是伺服器出錯。
        catch (Exception exception) when (exception is InvalidDataException or IOException or BadHttpRequestException)
        {
            return ApiFailure.BadRequest.ToResult();
        }

        if (result.Failure is { } uploadFailure)
        {
            // 上傳時太大是 413（用戶端與代理都用它辨認內容太大）；送訊息時的合計超過是 422，所以狀態碼只在這裡改。
            var mapped = uploadFailure.ToApiFailure();
            return uploadFailure.Code == AiErrorCodes.AttachmentTooLarge
                ? (mapped with { StatusCode = StatusCodes.Status413PayloadTooLarge }).ToResult()
                : mapped.ToResult();
        }

        return EndpointResults.Created(null, AssistantAttachmentData.From(result.Value));
    }

    private static async Task<IResult> DeleteAsync(
        string projectKey,
        string id,
        ClaimsPrincipal principal,
        [FromServices] IAssistantAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(id, "D", out var attachmentId))
        {
            return EndpointResults.Fail(NotFound());
        }

        var result = await attachments.DeletePendingAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, attachmentId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }

    private static async Task<IResult> DownloadAsync(
        string projectKey,
        string id,
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IAssistantAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(id, "D", out var attachmentId))
        {
            return EndpointResults.Fail(NotFound());
        }

        var result = await attachments.OpenAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, attachmentId, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        // 標頭在這裡寫好、回傳串流：Results.Stream 讀完或中斷時會把串流關掉，儲存空間的檔案控制代碼不會漏。
        var content = result.Value;
        var attachment = content.Attachment;
        AttachmentDownload.Apply(context.Response, attachment.FileName, attachment.ContentType, attachment.SizeBytes, content.Sha256);
        return Results.Stream(content.Content, contentType: context.Response.ContentType, enableRangeProcessing: false);
    }

    /// <summary>不是編號格式的路徑參數，和找不到附件同一個回答：不讓人從回應分辨格式錯誤與不存在。</summary>
    private static Failure NotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, "找不到這個附件。");
}
