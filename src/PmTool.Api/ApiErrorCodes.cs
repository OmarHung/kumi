namespace PmTool.Api;

/// <summary>API 這一層自己的錯誤代碼，放在回應信封的 <c>error.code</c>。和 IssueCodes 一樣是對外合約，不能改名。</summary>
public static class ApiErrorCodes
{
    public const string UnsupportedMediaType = "request.unsupported_media_type";
    public const string UnsupportedCharset = "request.unsupported_charset";
    public const string PayloadTooLarge = "request.too_large";
    public const string InvalidEncoding = "request.invalid_encoding";
    public const string BadRequest = "request.bad_request";
    public const string NotFound = "request.not_found";
    public const string MethodNotAllowed = "request.method_not_allowed";
    public const string RateLimited = "request.rate_limited";
    public const string RequestTimeout = "request.timeout";
    public const string RequestFailed = "request.failed";
    public const string InternalError = "server.internal_error";
    public const string Busy = "server.busy";
    public const string NotReady = "server.not_ready";

    public const string AuthRequired = "auth.required";
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string CsrfRequired = "auth.csrf_required";
    public const string Forbidden = "auth.forbidden";

    /// <summary>用存取權杖呼叫，但權杖沒有這個端點要的範圍，或這個端點不給權杖用（403）。</summary>
    public const string InsufficientScope = "auth.insufficient_scope";

    /// <summary>同意頁的授權請求有問題（422）。<c>error.data</c> 是 <c>{ error, redirectTo }</c>，見 <c>internal/HANDOFF-mcp.md</c> 4.6。</summary>
    public const string OAuthRequestInvalid = "oauth.request_invalid";

    public const string PersonNotFound = "person.not_found";

    /// <summary>伺服器沒有這個平台的 kumi 執行檔（404）。</summary>
    public const string CliPackageNotFound = "cli.package_not_found";
}
