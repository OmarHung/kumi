using Microsoft.Net.Http.Headers;

namespace PmTool.Api.Security;

/// <summary>
/// 每個回應都帶的安全標頭。網頁是 Vite 建置的 React，腳本與樣式都是外部檔案，所以不需要 unsafe-inline；
/// 圖片多開 data:：有未讀時分頁的圖示是網頁現組的 SVG（data: 網址，web/src/features/inbox/faviconBadge.ts）。
/// </summary>
internal static class SecurityHeaders
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    /// <summary>
    /// 放在最外層，在回應開始送出時才寫標頭：例外處理會清掉已經寫好的標頭，
    /// 先寫的話 500 的回應就沒有它們了。一律走 HTTPS（<see cref="TransportSecurity.RequireHttps"/>）時多帶 HSTS，
    /// 轉到 https 的 308 也帶，瀏覽器下次就直接走 https。
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var requireHttps = app.ApplicationServices.GetRequiredService<TransportSecurity>().RequireHttps;
        Func<object, Task> apply = requireHttps ? ApplyWithHsts : Apply;
        return app.Use((context, next) =>
        {
            context.Response.OnStarting(apply, context.Response);
            return next(context);
        });
    }

    private static Task Apply(object state)
    {
        var headers = ((HttpResponse)state).Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.XFrameOptions = "DENY";

        // 端點自己寫了 CSP 就用它的：下載使用者上傳的檔案時，比整站的政策更嚴（default-src 'none'; sandbox）。
        if (!headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
        {
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
        }

        return Task.CompletedTask;
    }

    private static Task ApplyWithHsts(object state)
    {
        ((HttpResponse)state).Headers.StrictTransportSecurity = TransportSecurity.StrictTransportSecurity;
        return Apply(state);
    }
}
