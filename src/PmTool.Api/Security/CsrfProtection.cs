using PmTool.Api.Responses;

namespace PmTool.Api.Security;

/// <summary>標在不需要防偽標頭的端點上。</summary>
internal sealed class CsrfExemption
{
    public static readonly CsrfExemption Instance = new();

    private CsrfExemption()
    {
    }
}

/// <summary>
/// /v1/ 底下會改資料的請求必須帶 X-PmTool-Csrf: 1。別的網站的表單送不出自訂標頭，
/// 跨來源的 fetch 要帶它又得先過 CORS 預檢（這個 API 不開 CORS），所以帶得了它的只有同一個來源的網頁。
/// 帶 <c>Authorization: Bearer</c> 的請求不檢查：瀏覽器不會自己帶這個標頭，跨站的請求也送不出它（同樣要先過 CORS 預檢），
/// 所以 CSRF 打不到用存取權杖的呼叫。權杖無效時照常回 401，不會先被防偽標頭擋成 403。
/// 放在路由之後才看得到端點的豁免標記；放在確認登入之前，因為它只看請求本身，不需要知道是誰。
/// </summary>
internal static class CsrfProtection
{
    public const string HeaderName = "X-PmTool-Csrf";

    private const string RequiredValue = "1";

    private static readonly PathString ApiPrefix = "/v1";

    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) => app.Use(CheckAsync);

    public static TBuilder ExemptFromCsrfCheck<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.WithMetadata(CsrfExemption.Instance);

    private static Task CheckAsync(HttpContext context, RequestDelegate next) =>
        !NeedsHeader(context) || HasHeader(context.Request)
            ? next(context)
            : ApiFailure.CsrfRequired.WriteAsync(context.Response, context.RequestAborted);

    private static bool NeedsHeader(HttpContext context)
    {
        var method = context.Request.Method;
        var isSafe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);
        return !isSafe
            && context.Request.Path.StartsWithSegments(ApiPrefix, StringComparison.OrdinalIgnoreCase)
            && context.GetEndpoint()?.Metadata.GetMetadata<CsrfExemption>() is null
            && !AccessTokenAuthentication.IsBearerRequest(context.Request);
    }

    private static bool HasHeader(HttpRequest request) =>
        request.Headers.TryGetValue(HeaderName, out var values) && values is [RequiredValue];
}
