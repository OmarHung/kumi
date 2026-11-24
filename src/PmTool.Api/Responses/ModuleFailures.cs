using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Responses;

/// <summary>模組回的失敗轉成 HTTP：代碼、訊息與細項照原樣交給用戶端，API 只決定狀態碼。</summary>
internal static class ModuleFailures
{
    public static ApiFailure ToApiFailure(this Failure failure) =>
        new ApiFailure(StatusOf(failure), failure.Code, failure.Message)
        {
            Details = [.. failure.Details.Select(detail => new ApiErrorDetail(detail.Code, detail.Path, detail.Message))],
        };

    // 附件太大是 413，不是一般的驗證失敗（422）：用戶端與代理都用這個狀態碼辨認「內容太大」。
    private static int StatusOf(Failure failure) =>
        failure.Code == WorkErrorCodes.AttachmentTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusOf(failure.Kind);

    // 不認得的種類是合約加了新值、這裡沒跟上，屬於伺服器的錯，不猜一個看起來像用戶端錯誤的狀態碼。
    private static int StatusOf(FailureKind kind) => kind switch
    {
        FailureKind.Invalid => StatusCodes.Status422UnprocessableEntity,
        FailureKind.NotFound => StatusCodes.Status404NotFound,
        FailureKind.Conflict => StatusCodes.Status409Conflict,
        FailureKind.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };
}
