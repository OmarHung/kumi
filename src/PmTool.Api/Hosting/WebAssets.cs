using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace PmTool.Api.Hosting;

/// <summary>
/// 網頁（Vite 建置的 React）由 API 主機一起提供，同一個來源，Cookie 與防偽標頭才簡單。
/// </summary>
internal static class WebAssets
{
    private const string IndexFile = "index.html";

    // Vite 輸出的資產檔名帶內容的雜湊，內容變了檔名就變，可以放心長期快取。
    private const string ImmutableCaching = "public, max-age=31536000, immutable";

    // 其他檔案每次都要先問過伺服器（有 ETag，沒改就是 304）：index.html 一被快取，改版後使用者會拿到指向舊資產的頁面。
    private const string Revalidate = "no-cache";

    private static readonly PathString AssetsPrefix = "/assets";

    // 這些前綴底下找不到的路徑回 404 的信封，不回網頁：它們是給程式呼叫的，拿到 HTML 只會更難除錯。
    // /.well-known 尤其要：OAuth 與 MCP 的用戶端會依序試好幾個探索網址，對不存在的回 200 的網頁，它們會當成中繼資料來讀。
    private static readonly PathString[] ServerPrefixes = ["/v1", "/health", "/openapi", "/.well-known"];

    /// <summary>實際存在的檔案。排在限流與登入之前：檔案數量有限，而且登入頁本身就要能載入。</summary>
    public static IApplicationBuilder UseWebAssets(this IApplicationBuilder app) =>
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = SetCacheHeaders });

    /// <summary>
    /// 單頁應用的後備：網址是前端的路由（/projects/WEB），伺服器上沒有這個檔案，回 index.html 讓前端接手。
    /// 排在路由之後才知道有沒有端點接手；排在確認登入與限流之前：它只送一個小的靜態檔，和其他靜態檔一樣不限流。
    /// 限流的話，本機用 Docker 時所有瀏覽器共用同一個來源 IP，匿名的額度一用完連登入頁都打不開；
    /// 讀了 Cookie 的話，每次載入頁面都可能去問身分模組。/v1/ 這些前綴不會命中它，仍然照常限流並回 404 的信封。
    /// </summary>
    public static IApplicationBuilder UseSpaFallback(this IApplicationBuilder app) => app.Use(ServeIndexAsync);

    private static Task ServeIndexAsync(HttpContext context, RequestDelegate next)
    {
        if (!IsClientRoute(context))
        {
            return next(context);
        }

        var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var index = environment.WebRootFileProvider.GetFileInfo(IndexFile);
        if (!index.Exists)
        {
            // 網頁還沒建置（例如只跑 API 的開發環境），照一般找不到的路徑回 404。
            return next(context);
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = index.Length;
        context.Response.Headers.CacheControl = Revalidate;
        return context.Response.SendFileAsync(index, context.RequestAborted);
    }

    // 最後一段帶副檔名的路徑是在要檔案（/assets/x.js、/favicon.ico），找不到就該是 404；
    // 回 HTML 的話，瀏覽器只會報看不懂的 MIME 錯誤。ASP.NET Core 的 MapFallbackToFile 也是這樣分的。
    private static bool IsClientRoute(HttpContext context) =>
        context.GetEndpoint() is null
        && HttpMethods.IsGet(context.Request.Method)
        && !ServerPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
        && !Path.HasExtension(context.Request.Path.Value);

    private static void SetCacheHeaders(StaticFileResponseContext context) =>
        context.Context.Response.Headers[HeaderNames.CacheControl] =
            context.Context.Request.Path.StartsWithSegments(AssetsPrefix, StringComparison.OrdinalIgnoreCase)
                ? ImmutableCaching
                : Revalidate;
}
