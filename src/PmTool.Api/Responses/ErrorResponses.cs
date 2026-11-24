using Microsoft.AspNetCore.Diagnostics;

namespace PmTool.Api.Responses;

internal static class ErrorResponses
{
    /// <summary>
    /// 讓所有非 2xx 的回應都用信封。例外處理放最外層，例外內容只進記錄（ExceptionHandlerMiddleware 會記），不回給用戶端；
    /// 狀態碼頁面負責框架自己回、沒有內文的錯誤。
    /// </summary>
    public static IApplicationBuilder UseEnvelopeErrorResponses(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = WriteInternalErrorAsync });
        app.UseStatusCodePages(WriteStatusCodeAsync);
        return app;
    }

    private static Task WriteInternalErrorAsync(HttpContext context) =>
        ApiFailure.InternalError.WriteAsync(context.Response, context.RequestAborted);

    private static Task WriteStatusCodeAsync(StatusCodeContext context)
    {
        var response = context.HttpContext.Response;
        return ApiFailure.ForStatusCode(response.StatusCode).WriteAsync(response, context.HttpContext.RequestAborted);
    }
}
