namespace PmTool.Api.Responses;

/// <summary>API 這一層會回的錯誤：HTTP 狀態碼、錯誤代碼與給人看的訊息放在一起，避免同一種錯誤在不同地方回出不同的內容。</summary>
internal sealed record ApiFailure(int StatusCode, string Code, string Message)
{
    public static readonly ApiFailure UnsupportedMediaType = new(
        StatusCodes.Status415UnsupportedMediaType,
        ApiErrorCodes.UnsupportedMediaType,
        "不支援這個內文格式。JSON 請用 application/json；YAML 請用 application/yaml、application/x-yaml 或 text/yaml。");

    public static readonly ApiFailure JsonMediaTypeRequired = new(
        StatusCodes.Status415UnsupportedMediaType,
        ApiErrorCodes.UnsupportedMediaType,
        "內文必須是 JSON，Content-Type 請用 application/json。");

    public static readonly ApiFailure UnsupportedCharset = new(
        StatusCodes.Status415UnsupportedMediaType,
        ApiErrorCodes.UnsupportedCharset,
        "內文只接受 UTF-8 編碼。");

    public static readonly ApiFailure PayloadTooLarge = PayloadLargerThan(ApiLimits.MaxDocumentBytes);

    public static readonly ApiFailure InvalidEncoding = new(
        StatusCodes.Status400BadRequest,
        ApiErrorCodes.InvalidEncoding,
        "內文不是有效的 UTF-8。");

    public static readonly ApiFailure BadRequest = new(
        StatusCodes.Status400BadRequest,
        ApiErrorCodes.BadRequest,
        "請求的格式不正確。");

    public static readonly ApiFailure NotFound = new(
        StatusCodes.Status404NotFound,
        ApiErrorCodes.NotFound,
        "找不到這個路徑。");

    public static readonly ApiFailure MethodNotAllowed = new(
        StatusCodes.Status405MethodNotAllowed,
        ApiErrorCodes.MethodNotAllowed,
        "這個路徑不支援這個 HTTP 方法。");

    public static readonly ApiFailure RateLimited = new(
        StatusCodes.Status429TooManyRequests,
        ApiErrorCodes.RateLimited,
        "請求太頻繁，請稍後再試。");

    public static readonly ApiFailure TooManyLiveConnections = new(
        StatusCodes.Status429TooManyRequests,
        ApiErrorCodes.RateLimited,
        $"同時的即時連線太多了（每個人最多 {Live.LiveConnections.MaxPerPerson} 條），請關掉不用的分頁。");

    public static readonly ApiFailure RequestTimeout = new(
        StatusCodes.Status408RequestTimeout,
        ApiErrorCodes.RequestTimeout,
        $"內文沒有在 {ApiLimits.BodyReadTimeoutSeconds} 秒內送完。");

    public static readonly ApiFailure InternalError = new(
        StatusCodes.Status500InternalServerError,
        ApiErrorCodes.InternalError,
        "伺服器發生錯誤，請稍後再試。");

    public static readonly ApiFailure Busy = new(
        StatusCodes.Status503ServiceUnavailable,
        ApiErrorCodes.Busy,
        "伺服器正在處理的驗證請求已經到上限，請稍後再試。");

    public static readonly ApiFailure NotReady = new(
        StatusCodes.Status503ServiceUnavailable,
        ApiErrorCodes.NotReady,
        "服務還沒準備好，請稍後再試。");

    public static readonly ApiFailure AuthRequired = new(
        StatusCodes.Status401Unauthorized,
        ApiErrorCodes.AuthRequired,
        "請先登入。");

    // 不說是 Email 不存在還是密碼不對：分得出來的話，就能拿登入來探測哪些 Email 有帳號。
    public static readonly ApiFailure InvalidCredentials = new(
        StatusCodes.Status401Unauthorized,
        ApiErrorCodes.InvalidCredentials,
        "Email 或密碼不正確。");

    public static readonly ApiFailure CsrfRequired = new(
        StatusCodes.Status403Forbidden,
        ApiErrorCodes.CsrfRequired,
        "會修改資料的請求必須帶 X-PmTool-Csrf: 1 標頭。");

    public static readonly ApiFailure Forbidden = new(
        StatusCodes.Status403Forbidden,
        ApiErrorCodes.Forbidden,
        "你沒有權限做這件事。");

    /// <summary>用存取權杖呼叫，權杖的範圍不夠或這個端點不給權杖用。<paramref name="message"/> 說明要哪個範圍。</summary>
    public static ApiFailure InsufficientScope(string message) => new(
        StatusCodes.Status403Forbidden,
        ApiErrorCodes.InsufficientScope,
        message);

    public static readonly ApiFailure PersonNotFound = new(
        StatusCodes.Status404NotFound,
        ApiErrorCodes.PersonNotFound,
        "組織裡找不到這個人。");

    public static readonly ApiFailure CliPackageNotFound = new(
        StatusCodes.Status404NotFound,
        ApiErrorCodes.CliPackageNotFound,
        "這個伺服器沒有提供這個平台的 kumi。");

    /// <summary>
    /// 同意頁的授權請求有問題：422，說明是模組給的繁體中文，<c>error.data</c> 帶 OAuth 的錯誤代碼與要導回去的網址（沒驗過的用戶端是 null）。
    /// </summary>
    public static ApiFailure OAuthRequestInvalid(PmTool.Identity.OAuthFailure failure) => new(
        StatusCodes.Status422UnprocessableEntity,
        ApiErrorCodes.OAuthRequestInvalid,
        failure.Description)
    {
        Data = new OAuth.OAuthRequestErrorData(failure.Error, failure.RedirectTo),
    };

    public IReadOnlyList<ApiErrorDetail> Details { get; init; } = [];

    /// <summary>放進 <c>error.data</c> 的結構化資料；多數錯誤沒有。</summary>
    public object? Data { get; init; }

    public static ApiFailure PayloadLargerThan(int maxBytes) => new(
        StatusCodes.Status413PayloadTooLarge,
        ApiErrorCodes.PayloadTooLarge,
        $"內文超過 {maxBytes} 位元組的上限。");

    /// <summary>
    /// 給狀態碼頁面用：框架自己回、沒有內文的錯誤在這裡補上信封。目前會走到這裡的只有路由的 404 與 405；
    /// 400、401、403、413、415、429、500 都由各自的程式路徑直接寫信封。其他狀態碼保留原本的數字，用通用代碼。
    /// </summary>
    public static ApiFailure ForStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        >= StatusCodes.Status500InternalServerError => InternalError with { StatusCode = statusCode },
        _ => new ApiFailure(statusCode, ApiErrorCodes.RequestFailed, "請求無法處理。"),
    };

    public IResult ToResult() => Results.Json(ToEnvelope(), statusCode: StatusCode);

    public Task WriteAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCode;
        return response.WriteAsJsonAsync(ToEnvelope(), cancellationToken);
    }

    private ApiEnvelope<object> ToEnvelope() => ApiEnvelope.Fail(new ApiError(Code, Message, Details) { Data = Data });
}
