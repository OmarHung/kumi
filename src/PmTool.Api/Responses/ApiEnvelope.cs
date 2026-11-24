namespace PmTool.Api.Responses;

/// <summary>失敗的細項，例如哪一個欄位的值不對。Path 用文件或請求裡的屬性名稱，根是空字串。</summary>
internal sealed record ApiErrorDetail(string Code, string Path, string Message);

/// <summary>Details 一律寫出陣列，沒有細項時是空的：用戶端不必分辨「沒有這個屬性」和「沒有細項」。</summary>
internal sealed record ApiError(string Code, string Message, IReadOnlyList<ApiErrorDetail> Details)
{
    /// <summary>少數錯誤帶給用戶端的結構化資料（例如 OAuth 的 error 與 redirectTo）。沒有時整個屬性省略，其他錯誤的形狀不變。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }
}

/// <summary>所有回應共用的信封。用不到的 Data 或 Error 會寫成 null 而不是省略，用戶端可以只看固定的三個欄位。</summary>
internal sealed record ApiEnvelope<T>(bool Success, T? Data, ApiError? Error);

internal static class ApiEnvelope
{
    public static ApiEnvelope<T> Ok<T>(T data) => new(true, data, null);

    public static ApiEnvelope<object> Fail(ApiError error) => new(false, null, error);
}
